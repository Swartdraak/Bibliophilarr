using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
    }

    public class MediaCoverService :
        IHandleAsync<AuthorRefreshCompleteEvent>,
        IHandleAsync<AuthorDeletedEvent>,
        IHandleAsync<BookDeletedEvent>,
        IMapCoversToLocal
    {
        private const string USER_AGENT = "Dalvik/2.1.0 (Linux; U; Android 10; SM-G975U Build/QP1A.190711.020)";
        private const string OpenLibraryCoversHost = "covers.openlibrary.org";
        private const double CoverTokensPerSecond = 2.0;
        private const int CoverTokenBurst = 4;

        private readonly IMediaCoverProxy _mediaCoverProxy;
        private readonly IImageResizer _resizer;
        private readonly IBookService _bookService;
        private readonly IHttpClient _httpClient;
        private readonly IDiskProvider _diskProvider;
        private readonly ICoverExistsSpecification _coverExistsSpecification;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        private readonly string _coverRootFolder;

        private static readonly ConcurrentDictionary<string, CoverThrottleState> _coverThrottle = new ConcurrentDictionary<string, CoverThrottleState>(StringComparer.OrdinalIgnoreCase);

        private sealed class CoverThrottleState
        {
            public readonly object Sync = new object();
            public double Tokens = CoverTokenBurst;
            public DateTime LastRefillUtc = DateTime.UtcNow;
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
                                 IHttpClient httpClient,
                                 IDiskProvider diskProvider,
                                 IAppFolderInfo appFolderInfo,
                                 ICoverExistsSpecification coverExistsSpecification,
                                 IConfigFileProvider configFileProvider,
                                 IEventAggregator eventAggregator,
                                 Logger logger)
        {
            _mediaCoverProxy = mediaCoverProxy;
            _resizer = resizer;
            _bookService = bookService;
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _coverExistsSpecification = coverExistsSpecification;
            _configFileProvider = configFileProvider;
            _eventAggregator = eventAggregator;
            _logger = logger;

            _coverRootFolder = appFolderInfo.GetMediaCoverPath();
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
                foreach (var mediaCover in covers)
                {
                    if (mediaCover.CoverType == MediaCoverTypes.Unknown)
                    {
                        continue;
                    }

                    var filePath = GetCoverPath(entityId, coverEntity, mediaCover.CoverType, mediaCover.Extension, null);

                    mediaCover.RemoteUrl = mediaCover.Url;

                    if (coverEntity == MediaCoverEntity.Book)
                    {
                        mediaCover.Url = _configFileProvider.UrlBase + @"/MediaCover/Books/" + entityId + "/" + mediaCover.CoverType.ToString().ToLower() + GetExtension(mediaCover.CoverType, mediaCover.Extension);
                    }
                    else
                    {
                        mediaCover.Url = _configFileProvider.UrlBase + @"/MediaCover/" + entityId + "/" + mediaCover.CoverType.ToString().ToLower() + GetExtension(mediaCover.CoverType, mediaCover.Extension);
                    }

                    if (_diskProvider.FileExists(filePath))
                    {
                        var lastWrite = _diskProvider.FileGetLastWrite(filePath);
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

        private string GetAuthorCoverPath(int authorId)
        {
            return Path.Combine(_coverRootFolder, authorId.ToString());
        }

        private string GetBookCoverPath(int bookId)
        {
            return Path.Combine(_coverRootFolder, "Books", bookId.ToString());
        }

        private void EnsureAuthorCovers(Author author)
        {
            var toResize = new List<Tuple<MediaCover, bool>>();

            foreach (var cover in author.Metadata.Value.Images)
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
                        DownloadCover(author, cover, serverFileHeaders.LastModified ?? DateTime.Now);
                    }
                }
                catch (CoverRequestSuppressedException e)
                {
                    _logger.Debug("Skipping media cover download for {0}: {1}", author, e.Message);
                }
                catch (HttpException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", author, e.Message);
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
            foreach (var cover in book.Editions.Value.Single(x => x.Monitored).Images.Where(e => e.CoverType == MediaCoverTypes.Cover))
            {
                if (cover.CoverType == MediaCoverTypes.Unknown)
                {
                    continue;
                }

                var fileName = GetCoverPath(book.Id, MediaCoverEntity.Book, cover.CoverType, cover.Extension, null);
                var alreadyExists = false;

                try
                {
                    var serverFileHeaders = GetServerHeaders(cover.Url);

                    alreadyExists = _coverExistsSpecification.AlreadyExists(serverFileHeaders.LastModified, GetContentLength(serverFileHeaders), fileName);

                    if (!alreadyExists)
                    {
                        DownloadBookCover(book, cover, serverFileHeaders.LastModified ?? DateTime.Now);
                    }
                }
                catch (CoverRequestSuppressedException e)
                {
                    _logger.Debug("Skipping media cover download for {0}: {1}", book, e.Message);
                }
                catch (HttpException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", book, e.Message);
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
        }

        private void DownloadCover(Author author, MediaCover cover, DateTime lastModified)
        {
            var fileName = GetCoverPath(author.Id, MediaCoverEntity.Author, cover.CoverType, cover.Extension);

            _logger.Info("Downloading {0} for {1} {2}", cover.CoverType, author, cover.Url);
            EnforceCoverRateLimit(cover.Url);

            try
            {
                _httpClient.DownloadFile(cover.Url, fileName, USER_AGENT);
                RecordCoverRequestSuccess(cover.Url);
            }
            catch (HttpException ex)
            {
                RecordCoverRequestFailure(cover.Url, ex.Response?.StatusCode);
                throw;
            }

            try
            {
                _diskProvider.FileSetLastWriteTime(fileName, lastModified);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to set modified date for {0} image for author {1}", cover.CoverType, author);
            }
        }

        private void DownloadBookCover(Book book, MediaCover cover, DateTime lastModified)
        {
            var fileName = GetCoverPath(book.Id, MediaCoverEntity.Book, cover.CoverType, cover.Extension, null);

            _logger.Info("Downloading {0} for {1} {2}", cover.CoverType, book, cover.Url);
            EnforceCoverRateLimit(cover.Url);

            try
            {
                _httpClient.DownloadFile(cover.Url, fileName, USER_AGENT);
                RecordCoverRequestSuccess(cover.Url);
            }
            catch (HttpException ex)
            {
                RecordCoverRequestFailure(cover.Url, ex.Response?.StatusCode);
                throw;
            }

            try
            {
                _diskProvider.FileSetLastWriteTime(fileName, lastModified);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to set modified date for {0} image for book {1}", cover.CoverType, book);
            }
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
                RecordCoverRequestFailure(url, ex.Response?.StatusCode);
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
            EnsureAuthorCovers(message.Author);
            ReconcileCoverFiles(message.Author.Id, MediaCoverEntity.Author, message.Author.Metadata?.Value?.Images ?? new List<MediaCover>());

            var books = _bookService.GetBooksByAuthor(message.Author.Id);
            foreach (var book in books)
            {
                EnsureBookCovers(book);
                var monitored = book.Editions?.Value?.SingleOrDefault(x => x.Monitored);
                ReconcileCoverFiles(book.Id, MediaCoverEntity.Book, monitored?.Images ?? new List<MediaCover>());
            }

            _eventAggregator.PublishEvent(new MediaCoversUpdatedEvent(message.Author));
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

            var now = DateTime.UtcNow;
            var state = _coverThrottle.GetOrAdd(host, _ => new CoverThrottleState());

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

            var state = _coverThrottle.GetOrAdd(host, _ => new CoverThrottleState());
            lock (state.Sync)
            {
                state.ConsecutiveFailures = 0;
                if (state.CooldownUntilUtc < DateTime.UtcNow)
                {
                    state.CooldownUntilUtc = DateTime.MinValue;
                }
            }
        }

        private void RecordCoverRequestFailure(string url, HttpStatusCode? statusCode)
        {
            if (!TryGetCoverHost(url, out var host))
            {
                return;
            }

            if (statusCode != HttpStatusCode.TooManyRequests &&
                statusCode != HttpStatusCode.ServiceUnavailable &&
                statusCode != HttpStatusCode.RequestTimeout)
            {
                return;
            }

            var state = _coverThrottle.GetOrAdd(host, _ => new CoverThrottleState());
            var now = DateTime.UtcNow;

            lock (state.Sync)
            {
                state.ConsecutiveFailures++;

                var baseSeconds = statusCode == HttpStatusCode.TooManyRequests ? 15 : 8;
                var penalty = Math.Min(45, state.ConsecutiveFailures * 4);
                var jitterMillis = ComputeJitterMilliseconds(host);
                state.CooldownUntilUtc = now.AddSeconds(baseSeconds + penalty).AddMilliseconds(jitterMillis);
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

        private void ReconcileCoverFiles(int entityId, MediaCoverEntity coverEntity, IEnumerable<MediaCover> expectedCovers)
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

            foreach (var existing in _diskProvider.GetFiles(directory, true))
            {
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
