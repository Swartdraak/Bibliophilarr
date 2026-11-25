using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaCover
{
    public interface IMapCoversToLocal
    {
        void ConvertToLocalUrls(int entityId, MediaCoverEntity coverEntity, IEnumerable<MediaCover> covers);
        string GetCoverPath(int entityId, MediaCoverEntity coverEntity, MediaCoverTypes coverType, string extension, int? height = null);
        void EnsureBookCovers(Book book);
        void MapRecoveredBookCover(Book book, IEnumerable<MediaCover> images);
        void MapRecoveredAuthorPortrait(Author author, IEnumerable<MediaCover> images);
        bool HasBookCoverFolder(int bookId);
        MediaCover GetCachedBookCover(Book book, IList<Edition> editions);
    }

    public partial class MediaCoverService :
        IHandleAsync<AuthorRefreshCompleteEvent>,
        IHandleAsync<AuthorDeletedEvent>,
        IHandleAsync<BookDeletedEvent>,
        IMapCoversToLocal,
        IOpenLibraryBookCoverCache,
        IOpenLibraryAuthorPortraitCache
    {
        private const string USER_AGENT = "Dalvik/2.1.0 (Linux; U; Android 10; SM-G975U Build/QP1A.190711.020)";
        private const string OpenLibraryCoversHost = "covers.openlibrary.org";
        private const double CoverTokensPerSecond = 2.0;
        private const int CoverTokenBurst = 4;

        private static readonly object AlternativeDownloadLock = new object();
        private static readonly Regex AlternativeFile = new Regex(
            @"\A(?:cover|poster)\.source-[a-f0-9]{64}\.(?:jpe?g|png|gif|webp)\z",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private readonly IMediaCoverProxy _mediaCoverProxy;
        private readonly IImageResizer _resizer;
        private readonly IBookService _bookService;
        private readonly IAuthorRepository _authorRepository;
        private readonly IHttpClient _httpClient;
        private readonly IDiskProvider _diskProvider;
        private readonly ICoverExistsSpecification _coverExistsSpecification;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly ICatalogueIdentityRepository _catalogueIdentity;

        private readonly string _coverRootFolder;
        private readonly string _coverRetryStagingFolder;

        private static readonly ConcurrentDictionary<string, CoverThrottleState> _coverThrottle = new ConcurrentDictionary<string, CoverThrottleState>(StringComparer.OrdinalIgnoreCase);

        private sealed class CoverThrottleState(DateTime now)
        {
            public readonly object Sync = new object();
            public double Tokens = CoverTokenBurst;
            public DateTime LastRefillUtc = now;
            public DateTime CooldownUntilUtc = DateTime.MinValue;
            public int ConsecutiveFailures;
        }

        private sealed class CoverRequestSuppressedException : Exception
        {
            public CoverRequestSuppressedException(string message)
                : base(message)
            {
            }
        }

        // ImageSharp is slow on ARM (no hardware acceleration on mono yet)
        // So limit the number of concurrent resizing tasks
        private static SemaphoreSlim _semaphore = new SemaphoreSlim((int)Math.Ceiling(Environment.ProcessorCount / 2.0));

        public MediaCoverService(IMediaCoverProxy mediaCoverProxy,
                                 IImageResizer resizer,
                                 IBookService bookService,
                                 IAuthorRepository authorRepository,
                                 IHttpClient httpClient,
                                 IDiskProvider diskProvider,
                                 IAppFolderInfo appFolderInfo,
                                 ICoverExistsSpecification coverExistsSpecification,
                                 IConfigFileProvider configFileProvider,
                                 IEventAggregator eventAggregator,
                                 Logger logger,
                                 ICatalogueIdentityRepository catalogueIdentity)
            : this(mediaCoverProxy, resizer, bookService, authorRepository, httpClient, diskProvider, appFolderInfo, coverExistsSpecification, configFileProvider, eventAggregator, logger, TimeProvider.System, catalogueIdentity)
        {
        }

        internal MediaCoverService(IMediaCoverProxy mediaCoverProxy,
                                   IImageResizer resizer,
                                   IBookService bookService,
                                   IAuthorRepository authorRepository,
                                   IHttpClient httpClient,
                                   IDiskProvider diskProvider,
                                   IAppFolderInfo appFolderInfo,
                                   ICoverExistsSpecification coverExistsSpecification,
                                   IConfigFileProvider configFileProvider,
                                   IEventAggregator eventAggregator,
                                   Logger logger,
                                   TimeProvider timeProvider,
                                   ICatalogueIdentityRepository catalogueIdentity = null)
        {
            _mediaCoverProxy = mediaCoverProxy;
            _resizer = resizer;
            _bookService = bookService;
            _authorRepository = authorRepository;
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _coverExistsSpecification = coverExistsSpecification;
            _configFileProvider = configFileProvider;
            _eventAggregator = eventAggregator;
            _logger = logger;
            _timeProvider = timeProvider;
            _catalogueIdentity = catalogueIdentity;

            _coverRootFolder = appFolderInfo.GetMediaCoverPath();
            _coverRetryStagingFolder = Path.Combine(appFolderInfo.GetAppDataPath(), "MediaCoverRetry");
        }

        public string GetCoverPath(int entityId, MediaCoverEntity coverEntity, MediaCoverTypes coverType, string extension, int? height = null)
        {
            var heightSuffix = height.HasValue ? "-" + height.ToString() : "";

            if (coverEntity == MediaCoverEntity.Book)
            {
                return Path.Combine(GetBookCoverPath(entityId), coverType.ToString().ToLower() + heightSuffix + GetExtension(coverType, extension));
            }

            return Path.Combine(GetAuthorCoverPath(entityId), coverType.ToString().ToLower() + heightSuffix + GetExtension(coverType, extension));
        }

        public bool HasBookCoverFolder(int bookId) => bookId > 0 && _diskProvider.FolderExists(GetBookCoverPath(bookId));

        public MediaCover GetCachedBookCover(Book book, IList<Edition> editions)
        {
            var covers = editions.GetBookImages().Where(image => image.CoverType == MediaCoverTypes.Cover).Take(2).ToList();
            if (book.Id <= 0 || covers.Count != 1 ||
                !Uri.TryCreate(covers[0].Url, UriKind.Absolute, out var url) ||
                (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            var cover = covers[0].JsonClone();
            cover.RemoteUrl = cover.Url;
            cover.Url = null;
            var path = GetCoverPath(book.Id, MediaCoverEntity.Book, cover.CoverType, cover.Extension);
            if (GetCachedCoverLastWrite(path) is { } lastWrite)
            {
                cover.Url = LocalCoverUrl(book.Id, MediaCoverEntity.Book, cover.CoverType, cover.Extension, lastWrite);
            }
            else
            {
                MapRecoveredBookCover(book, new[] { cover }, editions);
            }

            return cover.Url == null ? null : cover;
        }

        public void ConvertToLocalUrls(int entityId, MediaCoverEntity coverEntity, IEnumerable<MediaCover> covers)
        {
            if (entityId == 0)
            {
                // Author isn't in Bibliophilarr yet, map via a proxy to circument referrer issues
                foreach (var mediaCover in covers)
                {
                    mediaCover.RemoteUrl = mediaCover.Url;
                    mediaCover.Url = _mediaCoverProxy.RegisterUrl(mediaCover.RemoteUrl);
                }
            }
            else
            {
                var cachedTypes = new HashSet<MediaCoverTypes>();
                foreach (var mediaCover in covers)
                {
                    if (mediaCover.CoverType == MediaCoverTypes.Unknown)
                    {
                        continue;
                    }

                    var filePath = GetCoverPath(entityId, coverEntity, mediaCover.CoverType, mediaCover.Extension, null);

                    mediaCover.RemoteUrl = mediaCover.Url;
                    if (!cachedTypes.Add(mediaCover.CoverType))
                    {
                        var extension = GetSourceBoundExtension(mediaCover);
                        var alternative = extension == null ? null :
                            GetCachedCoverLastWrite(GetCoverPath(entityId, coverEntity, mediaCover.CoverType, extension));
                        mediaCover.Url = alternative.HasValue
                            ? LocalCoverUrl(entityId, coverEntity, mediaCover.CoverType, extension, alternative.Value)
                            : _mediaCoverProxy.RegisterUrl(mediaCover.RemoteUrl);
                        continue;
                    }

                    if (coverEntity == MediaCoverEntity.Book)
                    {
                        mediaCover.Url = _configFileProvider.UrlBase + @"/MediaCover/Books/" + entityId + "/" + mediaCover.CoverType.ToString().ToLower() + GetExtension(mediaCover.CoverType, mediaCover.Extension);
                    }
                    else
                    {
                        mediaCover.Url = _configFileProvider.UrlBase + @"/MediaCover/" + entityId + "/" + mediaCover.CoverType.ToString().ToLower() + GetExtension(mediaCover.CoverType, mediaCover.Extension);
                    }

                    if (GetCachedCoverLastWrite(filePath) is DateTime lastWrite)
                    {
                        mediaCover.Url += "?lastWrite=" + lastWrite.Ticks;
                    }
                    else if (mediaCover.RemoteUrl.IsNotNullOrWhiteSpace())
                    {
                        // Prevent stale local file references from spamming missing-file warnings.
                        mediaCover.Url = _mediaCoverProxy.RegisterUrl(mediaCover.RemoteUrl);
                    }
                }
            }
        }

        private string LocalCoverUrl(int entityId, MediaCoverEntity entity, MediaCoverTypes type, string extension, DateTime lastWrite)
        {
            var folder = entity == MediaCoverEntity.Book ? "Books/" : string.Empty;
            return $"{_configFileProvider.UrlBase}/MediaCover/{folder}{entityId}/{type.ToString().ToLowerInvariant()}{extension}?lastWrite={lastWrite.Ticks}";
        }

        private DateTime? GetCachedCoverLastWrite(string filePath)
        {
            try
            {
                return _diskProvider.FileExists(filePath) && _diskProvider.GetFileSize(filePath) > 0
                    ? _diskProvider.FileGetLastWrite(filePath)
                    : null;
            }
            catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // The cache can be reconciled between checking the file and reading its metadata.
                return null;
            }
        }

        private string GetAuthorCoverPath(int authorId)
        {
            return Path.Combine(_coverRootFolder, authorId.ToString());
        }

        private string GetBookCoverPath(int bookId)
        {
            return Path.Combine(_coverRootFolder, "Books", bookId.ToString());
        }

        private void EnsureAuthorCovers(Author author, IReadOnlyList<MediaCover> alternatives)
        {
            var toResize = new List<Tuple<MediaCover, bool>>();

            foreach (var cover in author.Metadata.Value.Images.DistinctBy(image => image.CoverType))
            {
                if (cover.CoverType == MediaCoverTypes.Unknown)
                {
                    continue;
                }

                var fileName = GetCoverPath(author.Id, MediaCoverEntity.Author, cover.CoverType, cover.Extension);
                var alreadyExists = false;

                try
                {
                    var serverFileHeaders = GetServerHeaders(cover.Url);

                    alreadyExists = _coverExistsSpecification.AlreadyExists(serverFileHeaders.LastModified, GetContentLength(serverFileHeaders), fileName);

                    if (!alreadyExists)
                    {
                        DownloadCover(author, cover, fileName, serverFileHeaders.LastModified ?? DateTime.Now);
                    }
                }
                catch (CoverRequestSuppressedException e)
                {
                    _logger.Debug("Skipping media cover download for {0}: {1}", author, e.Message);
                }
                catch (HttpException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1} ({2})", author, e.GetType().Name, e.Response?.StatusCode);
                    TryCacheAlternative(author.Id, MediaCoverEntity.Author, author, fileName, cover.CoverType, alternatives, e);
                }
                catch (WebException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", author, e.Message);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't download media cover for {0}", author);
                }

                toResize.Add(Tuple.Create(cover, alreadyExists));
            }

            try
            {
                _semaphore.Wait();

                foreach (var tuple in toResize)
                {
                    EnsureResizedCovers(author, tuple.Item1, !tuple.Item2);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void EnsureBookCovers(Book book)
        {
            var covers = GetBookCoverCandidates(book).ToList();
            var alternatives = GetVerifiedBookAlternatives(book, covers).ToList();

            foreach (var cover in covers.Take(1))
            {
                var fileName = GetCoverPath(book.Id, MediaCoverEntity.Book, cover.CoverType, cover.Extension, null);
                var alreadyExists = false;

                try
                {
                    var serverFileHeaders = GetServerHeaders(cover.Url);

                    alreadyExists = _coverExistsSpecification.AlreadyExists(serverFileHeaders.LastModified, GetContentLength(serverFileHeaders), fileName);

                    if (!alreadyExists)
                    {
                        DownloadCover(book, cover, fileName, serverFileHeaders.LastModified ?? DateTime.Now);
                    }
                }
                catch (CoverRequestSuppressedException e)
                {
                    _logger.Debug("Skipping media cover download for {0}: {1}", book, e.Message);
                }
                catch (HttpException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1} ({2})", book, e.GetType().Name, e.Response?.StatusCode);
                    TryCacheAlternative(book.Id, MediaCoverEntity.Book, book, fileName, cover.CoverType, alternatives, e);
                }
                catch (WebException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", book, e.Message);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't download media cover for {0}", book);
                }
            }

            if (covers.Count > 0)
            {
                ReconcileAlternativeFiles(book.Id, MediaCoverEntity.Book, alternatives);
            }
        }

        private void DownloadCover(object entity, MediaCover cover, string fileName, DateTime lastModified)
        {
            _logger.Info("Downloading {0} for {1}", cover.CoverType, entity);
            EnforceCoverRateLimit(cover.Url);

            try
            {
                _httpClient.DownloadFile(cover.Url, fileName, USER_AGENT);
                RecordCoverRequestSuccess(cover.Url);
            }
            catch (HttpException ex)
            {
                RecordCoverRequestFailure(cover.Url, ex.Response);
                throw;
            }

            try
            {
                _diskProvider.FileSetLastWriteTime(fileName, lastModified);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to set modified date for {0} image for {1}", cover.CoverType, entity);
            }
        }

        private void TryCacheAlternative(
            int id,
            MediaCoverEntity kind,
            object entity,
            string primaryPath,
            MediaCoverTypes coverType,
            IReadOnlyList<MediaCover> alternatives,
            HttpException failure)
        {
            var status = failure.Response?.StatusCode;
            if (id <= 0 || (status != HttpStatusCode.NotFound && status != HttpStatusCode.Gone) ||
                alternatives.FirstOrDefault(cover => cover.CoverType == coverType) is not { } candidate)
            {
                return;
            }

            var path = GetCoverPath(id, kind, coverType, GetSourceBoundExtension(candidate));
            lock (AlternativeDownloadLock)
            {
                try
                {
                    if (GetCachedCoverLastWrite(primaryPath).HasValue || GetCachedCoverLastWrite(path).HasValue)
                    {
                        return;
                    }

                    var headers = GetServerHeaders(candidate.Url);
                    DownloadCover(entity, candidate, path, headers.LastModified ?? DateTime.Now);
                }
                catch (Exception e)
                {
                    _logger.Debug("Could not cache a secondary cover for {0}: {1}", entity, e.GetType().Name);
                }
            }
        }

        private static string GetSourceBoundExtension(MediaCover cover)
        {
            if (cover?.SourceProvider != "Hardcover" || string.IsNullOrWhiteSpace(cover.SourceId) ||
                (cover.CoverType != MediaCoverTypes.Cover && cover.CoverType != MediaCoverTypes.Poster) ||
                !Uri.TryCreate(cover.Url, UriKind.Absolute, out var url) ||
                (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            var key = $"{cover.SourceProvider}\n{cover.SourceId}\n{cover.Url}".SHA256Hash();
            var extension = $".source-{key}{Path.GetExtension(url.AbsolutePath).ToLowerInvariant()}";
            return IsAlternativePath($"{cover.CoverType}{extension}") ? extension : null;
        }

        private void EnsureResizedCovers(Author author, MediaCover cover, bool forceResize, Book book = null)
        {
            var heights = GetDefaultHeights(cover.CoverType);

            foreach (var height in heights)
            {
                var mainFileName = GetCoverPath(author.Id, MediaCoverEntity.Author, cover.CoverType, cover.Extension);
                var resizeFileName = GetCoverPath(author.Id, MediaCoverEntity.Author, cover.CoverType, cover.Extension, height);

                if (forceResize || !_diskProvider.FileExists(resizeFileName) || _diskProvider.GetFileSize(resizeFileName) == 0)
                {
                    _logger.Debug("Resizing {0}-{1} for {2}", cover.CoverType, height, author);

                    try
                    {
                        _resizer.Resize(mainFileName, resizeFileName, height);
                    }
                    catch
                    {
                        _logger.Debug("Couldn't resize media cover {0}-{1} for author {2}, using full size image instead.", cover.CoverType, height, author);
                    }
                }
            }
        }

        private int[] GetDefaultHeights(MediaCoverTypes coverType)
        {
            switch (coverType)
            {
                default:
                    return new int[] { };

                case MediaCoverTypes.Poster:
                case MediaCoverTypes.Disc:
                case MediaCoverTypes.Cover:
                case MediaCoverTypes.Logo:
                case MediaCoverTypes.Headshot:
                    return new[] { 500, 250 };

                case MediaCoverTypes.Banner:
                    return new[] { 70, 35 };

                case MediaCoverTypes.Fanart:
                case MediaCoverTypes.Screenshot:
                    return new[] { 360, 180 };
            }
        }

        private string GetExtension(MediaCoverTypes coverType, string defaultExtension)
        {
            return coverType switch
            {
                MediaCoverTypes.Clearlogo => ".png",
                _ => defaultExtension
            };
        }

        private HttpHeader GetServerHeaders(string url)
        {
            // OpenLibrary doesn't allow a HEAD, so request a zero byte range instead
            EnforceCoverRateLimit(url);

            var request = new HttpRequest(url)
            {
                AllowAutoRedirect = true,
            };

            request.Headers.Add("Range", "bytes=0-0");
            request.Headers.Add("User-Agent", USER_AGENT);

            try
            {
                var headers = _httpClient.Get(request).Headers;
                RecordCoverRequestSuccess(url);
                return headers;
            }
            catch (HttpException ex)
            {
                RecordCoverRequestFailure(url, ex.Response);
                throw;
            }
        }

        private long? GetContentLength(HttpHeader headers)
        {
            var range = headers.Get("content-range");

            if (range == null)
            {
                return null;
            }

            var split = range.Split('/');
            if (split.Length == 2 && long.TryParse(split[1], out var length))
            {
                return length;
            }

            return null;
        }

        public void HandleAsync(AuthorRefreshCompleteEvent message)
        {
            var author = _authorRepository.Find(message.Author.Id);
            var metadata = author?.Metadata?.Value;
            if (metadata?.Images == null || author.AuthorMetadataId != message.Author.AuthorMetadataId)
            {
                return;
            }

            if (metadata.Images.Count > 0)
            {
                var alternatives = metadata.Images.Where(cover => cover?.CoverType == MediaCoverTypes.Poster)
                    .DistinctBy(cover => cover.Url).Skip(1)
                    .Where(cover => metadata.Id > 0 && GetSourceBoundExtension(cover) != null && cover.SourceId == metadata.ForeignAuthorId &&
                        CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, cover.SourceId, null)?.Provider == cover.SourceProvider)
                    .ToList();
                EnsureAuthorCovers(author, alternatives);
                var portrait = OpenLibraryMissingAuthorPortrait.SelectArtwork(_authorRepository.Find(author.Id));
                var retrySuffix = portrait is { } current && OpenLibraryMissingAuthorPortrait.HasVerifiedOwner(_catalogueIdentity, current.MetadataId, current.SourceId)
                    ? current.FileSuffix
                    : null;
                ReconcileCoverFiles(author.Id, MediaCoverEntity.Author, metadata.Images, retrySuffix);
                ReconcileAlternativeFiles(author.Id, MediaCoverEntity.Author, alternatives);
            }

            var books = _bookService.GetBooksByAuthor(author.Id);
            foreach (var book in books)
            {
                EnsureBookCovers(book);
                var artwork = OpenLibraryMissingBookCover.SelectArtwork(book, book.Editions?.Value);
                var retrySuffix = artwork is { } selection && OpenLibraryMissingBookCover.HasVerifiedOwner(selection.WorkSourceId, book.Id, _catalogueIdentity)
                    ? selection.FileSuffix
                    : null;
                ReconcileCoverFiles(book.Id, MediaCoverEntity.Book, GetBookCoverCandidates(book).Take(1), retrySuffix);
            }

            _eventAggregator.PublishEvent(new MediaCoversUpdatedEvent(author));
        }

        private static IEnumerable<MediaCover> GetBookCoverCandidates(Book book)
        {
            return (book.Editions?.Value).GetBookImages().Where(image => image.CoverType == MediaCoverTypes.Cover);
        }

        private static IEnumerable<MediaCover> GetVerifiedBookAlternatives(Book book, IReadOnlyList<MediaCover> covers)
        {
            return covers.Skip(1).Where(cover => GetSourceBoundExtension(cover) != null &&
                book.Editions.Value.Any(edition => edition.Id > 0 && edition.BookId == book.Id &&
                    edition.Images?.Any(image => ReferenceEquals(image, cover)) == true &&
                    ((cover.SourceId == edition.ForeignEditionId && Regex.IsMatch(cover.SourceId, @"\Ahardcover:(?:source-)?edition:[1-9][0-9]*\z")) ||
                     (cover.SourceId == book.ForeignBookId &&
                      CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, cover.SourceId, null)?.Provider == cover.SourceProvider))));
        }

        public void HandleAsync(AuthorDeletedEvent message)
        {
            var path = GetAuthorCoverPath(message.Author.Id);
            if (_diskProvider.FolderExists(path))
            {
                _diskProvider.DeleteFolder(path, true);
            }
        }

        public void HandleAsync(BookDeletedEvent message)
        {
            var path = GetBookCoverPath(message.Book.Id);
            if (_diskProvider.FolderExists(path))
            {
                _diskProvider.DeleteFolder(path, true);
            }
        }

        private void EnforceCoverRateLimit(string url)
        {
            if (!TryGetCoverHost(url, out var host))
            {
                return;
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var state = _coverThrottle.GetOrAdd(host, static (_, timestamp) => new CoverThrottleState(timestamp), now);

            lock (state.Sync)
            {
                if (state.CooldownUntilUtc > now)
                {
                    throw new CoverRequestSuppressedException($"host cooldown active until {state.CooldownUntilUtc:o}");
                }

                var elapsedSeconds = Math.Max(0, (now - state.LastRefillUtc).TotalSeconds);
                state.Tokens = Math.Min(CoverTokenBurst, state.Tokens + (elapsedSeconds * CoverTokensPerSecond));
                state.LastRefillUtc = now;

                if (state.Tokens < 1)
                {
                    var wait = TimeSpan.FromMilliseconds(900 + ComputeJitterMilliseconds(host));
                    state.CooldownUntilUtc = now.Add(wait);
                    throw new CoverRequestSuppressedException($"token bucket depleted; next attempt after {wait.TotalMilliseconds:0}ms");
                }

                state.Tokens -= 1;
            }
        }

        private void RecordCoverRequestSuccess(string url)
        {
            if (!TryGetCoverHost(url, out var host))
            {
                return;
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var state = _coverThrottle.GetOrAdd(host, static (_, timestamp) => new CoverThrottleState(timestamp), now);
            lock (state.Sync)
            {
                state.ConsecutiveFailures = 0;
                if (state.CooldownUntilUtc < now)
                {
                    state.CooldownUntilUtc = DateTime.MinValue;
                }
            }
        }

        private void RecordCoverRequestFailure(string url, HttpResponse response)
        {
            if (!TryGetCoverHost(url, out var host))
            {
                return;
            }

            var statusCode = response?.StatusCode;
            if (statusCode != HttpStatusCode.TooManyRequests &&
                statusCode != HttpStatusCode.ServiceUnavailable &&
                statusCode != HttpStatusCode.RequestTimeout)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var state = _coverThrottle.GetOrAdd(host, static (_, timestamp) => new CoverThrottleState(timestamp), now);

            lock (state.Sync)
            {
                state.ConsecutiveFailures++;

                var baseSeconds = statusCode == HttpStatusCode.TooManyRequests ? 15 : 8;
                var penalty = Math.Min(45, state.ConsecutiveFailures * 4);
                var jitterMillis = ComputeJitterMilliseconds(host);
                var deadline = now.AddSeconds(baseSeconds + penalty).AddMilliseconds(jitterMillis);

                if (statusCode != HttpStatusCode.RequestTimeout &&
                    RetryConditionHeaderValue.TryParse(response.Headers["Retry-After"], out var retryAfter))
                {
                    var providerDeadline = retryAfter.Date?.UtcDateTime ?? now.Add(retryAfter.Delta.GetValueOrDefault());
                    if (providerDeadline > deadline)
                    {
                        deadline = providerDeadline;
                    }
                }

                if (deadline > state.CooldownUntilUtc)
                {
                    state.CooldownUntilUtc = deadline;
                }
            }
        }

        private static bool TryGetCoverHost(string url, out string host)
        {
            host = null;

            if (url.IsNullOrWhiteSpace() || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!uri.Host.Contains(OpenLibraryCoversHost, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            host = uri.Host;
            return true;
        }

        private static int ComputeJitterMilliseconds(string key)
        {
            unchecked
            {
                var seed = (key?.GetHashCode() ?? 0) ^ System.Environment.TickCount;
                var value = Math.Abs(seed % 401);
                return value;
            }
        }

        private void ReconcileAlternativeFiles(int id, MediaCoverEntity entity, IReadOnlyList<MediaCover> alternatives)
        {
            var directory = entity == MediaCoverEntity.Book ? GetBookCoverPath(id) : GetAuthorCoverPath(id);
            if (!_diskProvider.FolderExists(directory))
            {
                return;
            }

            var cached = _diskProvider.GetFiles(directory, false).Where(IsAlternativePath).ToList();
            if (cached.Count == 0)
            {
                return;
            }

            var expected = alternatives.Select(cover => GetCoverPath(id, entity, cover.CoverType, GetSourceBoundExtension(cover)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            lock (AlternativeDownloadLock)
            {
                foreach (var path in cached)
                {
                    if (!expected.Contains(path) || GetCachedCoverLastWrite(path) == null)
                    {
                        _diskProvider.DeleteFile(path);
                    }
                }
            }
        }

        private static bool IsAlternativePath(string path)
        {
            return AlternativeFile.IsMatch(Path.GetFileName(path));
        }

        private static bool IsAlternativeDownload(string path)
        {
            return IsAlternativePath(path) ||
                   (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && IsAlternativePath(path[..^5]));
        }

        private void ReconcileCoverFiles(int entityId, MediaCoverEntity coverEntity, IEnumerable<MediaCover> expectedCovers, string retrySuffix = null)
        {
            var directory = coverEntity == MediaCoverEntity.Book ? GetBookCoverPath(entityId) : GetAuthorCoverPath(entityId);
            if (!_diskProvider.FolderExists(directory))
            {
                return;
            }

            var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cover in expectedCovers.Where(c => c != null && c.CoverType != MediaCoverTypes.Unknown))
            {
                expectedPaths.Add(GetCoverPath(entityId, coverEntity, cover.CoverType, cover.Extension));

                foreach (var height in GetDefaultHeights(cover.CoverType))
                {
                    expectedPaths.Add(GetCoverPath(entityId, coverEntity, cover.CoverType, cover.Extension, height));
                }
            }

            var hasExpectedOrdinaryArt = expectedPaths.Count > 0;
            if (coverEntity == MediaCoverEntity.Book && OpenLibraryMissingBookCover.IsValidRetryFileSuffix(retrySuffix))
            {
                expectedPaths.Add(GetCoverPath(entityId, coverEntity, MediaCoverTypes.Cover, retrySuffix));
            }
            else if (coverEntity == MediaCoverEntity.Author && OpenLibraryMissingAuthorPortrait.IsValidRetryFileSuffix(retrySuffix))
            {
                expectedPaths.Add(GetCoverPath(entityId, coverEntity, MediaCoverTypes.Poster, retrySuffix));
            }

            if (expectedPaths.Count == 0 && coverEntity != MediaCoverEntity.Book)
            {
                return;
            }

            foreach (var existing in _diskProvider.GetFiles(directory, true).Where(file => !IsAlternativeDownload(file)))
            {
                if (coverEntity == MediaCoverEntity.Author && retrySuffix == null &&
                    OpenLibraryMissingAuthorPortrait.IsRetryPortraitFile(Path.GetFileName(existing)))
                {
                    // A missing or ambiguous association is not an authoritative cleanup list.
                    continue;
                }

                if (!hasExpectedOrdinaryArt && !OpenLibraryMissingBookCover.IsRetryCoverFile(Path.GetFileName(existing)))
                {
                    continue;
                }

                var isExpected = expectedPaths.Contains(existing);
                var isZeroByte = _diskProvider.FileExists(existing) && _diskProvider.GetFileSize(existing) == 0;

                if (!isExpected || isZeroByte)
                {
                    _diskProvider.DeleteFile(existing);
                }
            }
        }
    }
}
