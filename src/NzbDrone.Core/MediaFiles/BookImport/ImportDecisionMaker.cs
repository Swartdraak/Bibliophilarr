using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public interface IMakeImportDecision
    {
        List<ImportDecision<LocalBook>> GetImportDecisions(List<IFileInfo> musicFiles, IdentificationOverrides idOverrides, ImportDecisionMakerInfo itemInfo, ImportDecisionMakerConfig config);
    }

    public class IdentificationOverrides
    {
        public Author Author { get; set; }
        public Book Book { get; set; }
        public Edition Edition { get; set; }
    }

    public class ImportDecisionMakerInfo
    {
        public DownloadClientItem DownloadClientItem { get; set; }
        public ParsedBookInfo ParsedBookInfo { get; set; }
    }

    public class ImportDecisionMakerConfig
    {
        public FilterFilesType Filter { get; set; }
        public bool NewDownload { get; set; }
        public bool SingleRelease { get; set; }
        public bool IncludeExisting { get; set; }
        public bool AddNewAuthors { get; set; }
        public bool KeepAllEditions { get; set; }
    }

    public class ImportDecisionMaker : IMakeImportDecision
    {
        internal const string UnverifiedAuthorReviewMessage = "An existing catalogue author has the same name. Select the intended author manually.";

        private readonly IEnumerable<IImportDecisionEngineSpecification<LocalBook>> _trackSpecifications;
        private readonly IEnumerable<IImportDecisionEngineSpecification<LocalEdition>> _bookSpecifications;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IAugmentingService _augmentingService;
        private readonly IIdentificationService _identificationService;
        private readonly IImportRunTracker _importRunTracker;
        private readonly IConfigService _configService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IMetadataProviderOrchestrator _metadataOrchestrator;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly Logger _logger;

        public ImportDecisionMaker(IEnumerable<IImportDecisionEngineSpecification<LocalBook>> trackSpecifications,
                                   IEnumerable<IImportDecisionEngineSpecification<LocalEdition>> bookSpecifications,
                                   IMediaFileService mediaFileService,
                                   IMetadataTagService metadataTagService,
                                   IAugmentingService augmentingService,
                                   IIdentificationService identificationService,
                                   IImportRunTracker importRunTracker,
                                   IConfigService configService,
                                   IRootFolderService rootFolderService,
                                   IQualityProfileService qualityProfileService,
                                   IMetadataProviderOrchestrator metadataOrchestrator,
                                   IAuthorService authorService,
                                   IBookService bookService,
                                   Logger logger)
        {
            _trackSpecifications = trackSpecifications;
            _bookSpecifications = bookSpecifications;
            _mediaFileService = mediaFileService;
            _metadataTagService = metadataTagService;
            _augmentingService = augmentingService;
            _identificationService = identificationService;
            _importRunTracker = importRunTracker;
            _configService = configService;
            _rootFolderService = rootFolderService;
            _qualityProfileService = qualityProfileService;
            _metadataOrchestrator = metadataOrchestrator;
            _authorService = authorService;
            _bookService = bookService;
            _logger = logger;
        }

        private Tuple<List<LocalBook>, List<ImportDecision<LocalBook>>> GetLocalTracks(List<IFileInfo> musicFiles, DownloadClientItem downloadClientItem, ParsedBookInfo folderInfo, ImportDecisionMakerConfig config)
        {
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            var files = _mediaFileService.FilterUnchangedFiles(musicFiles, config.Filter);

            _logger.Debug("Analyzing {0}/{1} files.", files.Count, musicFiles.Count);

            if (!files.Any())
            {
                return Tuple.Create(new List<LocalBook>(), new List<ImportDecision<LocalBook>>());
            }

            var localTrackResults = new LocalBook[files.Count];
            var decisionResults = new ImportDecision<LocalBook>[files.Count];

            ParsedBookInfo downloadClientItemInfo = null;

            if (downloadClientItem != null)
            {
                downloadClientItemInfo = Parser.Parser.ParseBookTitle(downloadClientItem.Title);
            }

            var maxWorkers = GetSafeWorkerCount(_configService.ImportTagReadWorkerCount, files.Count);

            _logger.Debug($"Using up to {maxWorkers} tag read worker(s) for {files.Count} file(s)");

            const int progressInterval = 25;
            var checkedFiles = 0;
            var progressLock = new object();
            var options = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };
            _logger.ProgressInfo("Reading metadata for {0} files.", files.Count);

            Parallel.ForEach(Enumerable.Range(0, files.Count), options, index =>
            {
                var file = files[index];

                var localTrack = new LocalBook
                {
                    DownloadClientBookInfo = downloadClientItemInfo,
                    FolderTrackInfo = folderInfo,
                    Path = file.FullName,
                    Part = 1,
                    AdditionalFile = false
                };

                try
                {
                    localTrack.Size = file.Length;
                    localTrack.Modified = file.LastWriteTimeUtc;
                    localTrack.FileTrackInfo = _metadataTagService.ReadTags(file);
                }
                catch (Exception error) when (!config.NewDownload && (error is IOException or UnauthorizedAccessException))
                {
                    _logger.Warn("Could not read media file {0}: {1}", LogSanitizer.Sanitize(localTrack.Path), error.GetType().Name);
                    localTrack.Quality = new QualityModel();
                    decisionResults[index] = new ImportDecision<LocalBook>(localTrack, new Rejection("Unable to read file", holdReason: LibraryImportHoldReason.ReadError));
                    return;
                }
                finally
                {
                    // Keep progress ordered when multiple workers finish together.
                    lock (progressLock)
                    {
                        checkedFiles++;
                        if (checkedFiles % progressInterval == 0 || checkedFiles == files.Count)
                        {
                            _logger.ProgressInfo("Checked metadata for {0} of {1} files.", checkedFiles, files.Count);
                        }
                    }
                }

                var fileTrackInfo = localTrack.FileTrackInfo;
                localTrack.Part = fileTrackInfo.TrackNumbers.Any() ? fileTrackInfo.TrackNumbers.First() : 1;
                localTrack.ReliableTaggedBookTitle = IdentificationService.CaptureReliableTaggedBookTitle(fileTrackInfo);

                try
                {
                    // NOTE fix otherfiles?
                    _augmentingService.Augment(localTrack, true);
                    localTrackResults[index] = localTrack;
                }
                catch (AugmentingFailedException)
                {
                    decisionResults[index] = new ImportDecision<LocalBook>(localTrack, new Rejection("Unable to parse file", holdReason: LibraryImportHoldReason.ReadError));
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't import file. {0}", localTrack.Path);

                    decisionResults[index] = new ImportDecision<LocalBook>(localTrack, new Rejection("Unexpected error processing file"));
                }
            });

            var localTracks = localTrackResults.Where(x => x != null).ToList();
            var decisions = decisionResults.Where(x => x != null).ToList();

            _logger.Debug($"Tags parsed for {files.Count} files in {watch.ElapsedMilliseconds}ms");

            return Tuple.Create(localTracks, decisions);
        }

        private static int GetSafeWorkerCount(int configuredWorkers, int itemCount)
        {
            if (itemCount <= 0)
            {
                return 1;
            }

            return Math.Max(1, Math.Min(configuredWorkers, itemCount));
        }

        public List<ImportDecision<LocalBook>> GetImportDecisions(List<IFileInfo> musicFiles, IdentificationOverrides idOverrides, ImportDecisionMakerInfo itemInfo, ImportDecisionMakerConfig config)
        {
            var totalWatch = System.Diagnostics.Stopwatch.StartNew();
            var runSummary = _importRunTracker.CreateRun();
            runSummary.FilesScanned = musicFiles.Count;

            idOverrides = idOverrides ?? new IdentificationOverrides();
            itemInfo = itemInfo ?? new ImportDecisionMakerInfo();

            var trackData = GetLocalTracks(musicFiles, itemInfo.DownloadClientItem, itemInfo.ParsedBookInfo, config);
            var localTracks = trackData.Item1;
            var decisions = trackData.Item2;

            runSummary.FilesFiltered = musicFiles.Count - localTracks.Count - decisions.Count;
            runSummary.TagReadMs = totalWatch.ElapsedMilliseconds;

            localTracks.ForEach(x => x.ExistingFile = !config.NewDownload);

            if (localTracks.Count > 0)
            {
                _logger.ProgressInfo("Matching {0} files to the catalogue.", localTracks.Count);
            }

            var releases = _identificationService.Identify(localTracks, idOverrides, config, runSummary);

            foreach (var release in releases)
            {
                var conflictingOrdinal = IdentificationService.HasConflictingWorkOrdinal(release, idOverrides, config);
                var authorNeedsReview = false;
                if (!conflictingOrdinal)
                {
                    EnsureData(release);

                    var attemptedExactIsbn = false;

                    // Issue #275: only search directly when identification found no edition
                    // and the file's own work identity is sufficiently consistent.
                    if (release.Edition == null && release.IdentificationHoldReason != LibraryImportHoldReason.CompetingEditions)
                    {
                        var exactIsbn = GetUnattendedFallbackIsbn(release, config, idOverrides);
                        if (exactIsbn != null)
                        {
                            attemptedExactIsbn = true;
                            IdentifyReleaseFallback(release, config, exactIsbn, requireTypedEdition: true);
                        }

                        if (release.Edition == null && (!attemptedExactIsbn || release.IdentificationHoldReason != LibraryImportHoldReason.ProviderIdentityConflict))
                        {
                            IdentifyReleaseFallback(release, config);
                        }
                    }

                    authorNeedsReview = NeedsReviewForNewSyntheticAuthor(release, idOverrides, config);
                    if (authorNeedsReview && !attemptedExactIsbn)
                    {
                        var fileIsbns = release.LocalBooks.Select(file => file.FileTrackInfo)
                            .Where(tags => tags?.IsbnConfidence >= 0.9)
                            .Select(tags => Isbn.AsIsbn13(tags.Isbn))
                            .Where(isbn => isbn != null).Distinct().Take(2).ToList();
                        if (fileIsbns.Count == 1)
                        {
                            IdentifyReleaseFallback(release, config, fileIsbns[0]);
                            authorNeedsReview = NeedsReviewForNewSyntheticAuthor(release, idOverrides, config);
                        }
                    }
                }

                release.NewDownload = config.NewDownload;

                ImportDecision<LocalEdition> releaseDecision;
                if (conflictingOrdinal)
                {
                    releaseDecision = new ImportDecision<LocalEdition>(release, new Rejection(
                        "The filename and embedded title name different volumes of the same series. Select the intended work manually.",
                        holdReason: LibraryImportHoldReason.ConflictingIdentity));
                }
                else if (authorNeedsReview)
                {
                    releaseDecision = new ImportDecision<LocalEdition>(release, new Rejection(
                        UnverifiedAuthorReviewMessage,
                        holdReason: LibraryImportHoldReason.AuthorIdentityConfirmationRequired));
                }
                else
                {
                    releaseDecision = GetDecision(release, itemInfo.DownloadClientItem);
                }

                foreach (var localTrack in release.LocalBooks)
                {
                    if (releaseDecision.Approved)
                    {
                        var trackDecision = GetDecision(localTrack, itemInfo.DownloadClientItem);
                        decisions.AddIfNotNull(trackDecision);
                        if (trackDecision?.Approved == true)
                        {
                            runSummary.Imported++;
                        }
                        else
                        {
                            runSummary.Rejected++;
                        }
                    }
                    else
                    {
                        decisions.Add(new ImportDecision<LocalBook>(localTrack, releaseDecision.Rejections.ToArray()));
                        runSummary.Rejected++;
                    }
                }
            }

            totalWatch.Stop();
            runSummary.TotalMs = totalWatch.ElapsedMilliseconds;
            _importRunTracker.LogSummary(runSummary, _logger);

            return decisions;
        }

        private void EnsureData(LocalEdition edition)
        {
            var author = edition.Edition?.Book?.Value?.Author?.Value;

            if (author != null && author.QualityProfileId == 0)
            {
                var rootFolder = _rootFolderService.GetBestRootFolder(edition.LocalBooks.First().Path);

                if (rootFolder == null)
                {
                    return;
                }

                var qualityProfile = _qualityProfileService.Get(rootFolder.DefaultQualityProfileId);

                author.QualityProfileId = qualityProfile.Id;
                author.QualityProfile = qualityProfile;
            }
        }

        private static string GetUnattendedFallbackIsbn(LocalEdition release, ImportDecisionMakerConfig config, IdentificationOverrides overrides)
        {
            if (config.NewDownload || !config.IncludeExisting ||
                overrides.Author != null || overrides.Book != null || overrides.Edition != null ||
                release.IdentificationHoldReason is not null and not LibraryImportHoldReason.LowConfidence ||
                release.LocalBooks is not { Count: 1 })
            {
                return null;
            }

            var file = release.LocalBooks[0];
            var tags = file.FileTrackInfo;
            if (ImportFormat.ForPath(file.Path) != FormatType.Ebook ||
                tags is not { IsbnConfidence: >= 0.9, BookTitleConfidence: >= 0.9, AuthorConfidence: >= 0.9 } ||
                tags.OpenLibraryId.IsNotNullOrWhiteSpace() ||
                (tags.AsinConfidence >= 0.9 && NormalizeLibraryAsin(tags.Asin) != null))
            {
                return null;
            }

            return Isbn.AsIsbn13(tags.Isbn);
        }

        private bool NeedsReviewForNewSyntheticAuthor(LocalEdition release, IdentificationOverrides overrides, ImportDecisionMakerConfig config)
        {
            if (config.NewDownload || !config.AddNewAuthors || overrides?.Author != null || overrides?.Book != null || overrides?.Edition != null)
            {
                return false;
            }

            return NeedsReviewForNewSyntheticAuthor(release.Edition?.Book?.Value?.Author?.Value, _authorService);
        }

        internal static bool NeedsReviewForNewSyntheticAuthor(Author author, IAuthorService authorService)
        {
            if (author == null || author.Id > 0 || author.Name.IsNullOrWhiteSpace() ||
                author.ForeignAuthorId?.StartsWith("googlebooks:author:", StringComparison.OrdinalIgnoreCase) != true ||
                authorService.FindById(author.ForeignAuthorId) != null)
            {
                return false;
            }

            return authorService.GetAuthorsByCleanName(author.Name.CleanAuthorName())?.Any(candidate =>
                CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, candidate.ForeignAuthorId, null) != null) == true;
        }

        // Issue #275: bounded fallback for releases whose identification left the
        // catalogue Edition null. The regular candidate path (local DB candidates +
        // CandidateService remote search) requires a usable identity in the file tags
        // (author/book tags, or an ISBN/ASIN/OpenLibrary id) to build candidates; when
        // none is present it finds nothing and the item would be permanently rejected
        // with "Couldn't find similar book". If the file metadata still carries a book
        // title and an author, look the catalogue up directly and adopt the best
        // candidate edition so the scan/import can proceed. Genuinely unresolvable
        // items (no title, no author, or no remote hit) keep the original rejection.
        private void IdentifyReleaseFallback(LocalEdition release, ImportDecisionMakerConfig config, string requiredIsbn = null, bool requireTypedEdition = false)
        {
            try
            {
                var bookTitle = release.LocalBooks
                    .Select(x => x.FileTrackInfo?.BookTitle)
                    .Where(x => x != null && x.Trim().Length > 0)
                    .OrderByDescending(x => x.Trim().Length)
                    .FirstOrDefault();
                var authorName = release.LocalBooks
                    .SelectMany(x => x.FileTrackInfo?.Authors ?? new List<string>())
                    .Where(x => x != null && x.Trim().Length > 0)
                    .OrderByDescending(x => x.Trim().Length)
                    .FirstOrDefault();

                if (bookTitle == null || authorName == null)
                {
                    return;
                }

                _logger.Debug("Attempting direct catalogue lookup for an import release");

                var (book, bestEdition) = config.NewDownload
                    ? SelectRequestedFallback(_metadataOrchestrator.SearchForNewBook(bookTitle, authorName, true), release)
                    : FindLibraryFallback(release, bookTitle, authorName, config.AddNewAuthors, requiredIsbn, requireTypedEdition);

                if (bestEdition == null)
                {
                    return;
                }

                if (!config.NewDownload || bestEdition.Book == null || !bestEdition.Book.IsLoaded)
                {
                    bestEdition.Book = book;
                }

                if (!config.NewDownload)
                {
                    release.Distance = DistanceCalculator.BookDistance(release.LocalBooks, bestEdition);
                }

                release.Edition = bestEdition;
                release.ExistingTracks = new List<LocalBook>();
                if (!config.NewDownload)
                {
                    EnsureData(release);
                }

                release.PopulateMatch(config.KeepAllEditions);
                release.IdentificationHoldReason = null;

                _logger.Debug("Direct catalogue lookup matched an edition");
            }
            catch (Exception e)
            {
                _logger.Warn("Direct catalogue lookup failed ({0})", e.GetType().Name);
            }
        }

        private static (Book Book, Edition Edition) SelectRequestedFallback(List<Book> books, LocalEdition release)
        {
            // Preserve requested manual/download ranking. Format only refines the first work.
            var match = (books ?? new List<Book>())
                .Where(book => book?.Editions?.Value != null)
                .Select(book => (Book: book, Editions: book.Editions.Value
                    .Where(edition => edition?.ForeignEditionId.IsNotNullOrWhiteSpace() == true)
                    .ToList()))
                .FirstOrDefault(candidate => candidate.Editions.Count > 0);

            var taggedBookTitle = IdentificationService.GetReliableTaggedBookTitle(release);
            return (match.Book, match.Editions?.FirstOrDefault(edition =>
                release.LocalBooks.All(file => ImportFormat.IsCompatible(file.Path, edition)) &&
                !IdentificationService.ConflictsWithGuideTitle(taggedBookTitle, edition, match.Book)));
        }

        private (Book Book, Edition Edition) FindLibraryFallback(LocalEdition release, string title, string author, bool addNewAuthors, string requiredIsbn, bool requireTypedEdition)
        {
            var exactTitle = NormalizeLibraryIdentity(title);
            var exactAuthor = NormalizeLibraryAuthor(author);
            if (exactTitle == null || exactAuthor == null ||
                release.LocalBooks.Any(file => ConflictsWithLibraryIdentity(file.FileTrackInfo, exactTitle, exactAuthor)))
            {
                return default;
            }

            var formats = release.LocalBooks.Select(file => ImportFormat.ForPath(file.Path)).Distinct().ToList();
            if (formats.Count != 1 || !formats[0].HasValue)
            {
                return default;
            }

            (Book Book, Edition Edition, bool Hold) selection = default;
            bool Select(List<Book> books)
            {
                selection = SelectLibraryFallback(books, release, exactTitle, exactAuthor, formats[0].Value, addNewAuthors, requiredIsbn, requireTypedEdition);
                return selection.Edition != null || selection.Hold;
            }

            if (requiredIsbn == null)
            {
                _metadataOrchestrator.SearchForLibraryImport(title, author, Select);
            }
            else
            {
                _metadataOrchestrator.SearchForLibraryImportByIsbn(requiredIsbn, Select);
            }

            if (selection.Hold &&
                (release.IdentificationHoldReason == null || release.IdentificationHoldReason == LibraryImportHoldReason.LowConfidence))
            {
                release.IdentificationHoldReason = LibraryImportHoldReason.ProviderIdentityConflict;
            }

            return (selection.Book, selection.Edition);
        }

        private (Book Book, Edition Edition, bool Hold) SelectLibraryFallback(
            List<Book> books,
            LocalEdition release,
            string exactTitle,
            string exactAuthor,
            FormatType format,
            bool addNewAuthors,
            string requiredIsbn,
            bool requireTypedEdition)
        {
            // Resolve work ambiguity before consulting editions, so a matching format
            // cannot select one of two independently plausible work identities.
            var works = books.Where(book => book != null &&
                    NormalizeLibraryIdentity(book.Title) == exactTitle &&
                    NormalizeLibraryAuthor(book.AuthorMetadata?.Value?.Name ?? book.Author?.Value?.Name) == exactAuthor)
                .GroupBy(GetLibraryWorkIdentity).Take(2).ToList();
            if (works.Count == 0)
            {
                return default;
            }

            if (works.Count > 1 || works[0].Where(book => book.Id > 0).Select(book => book.Id).Distinct().Take(2).Count() > 1)
            {
                return (null, null, true);
            }

            if (works[0].Key == null)
            {
                return default;
            }

            var taggedBookTitle = IdentificationService.GetReliableTaggedBookTitle(release);
            var editions = works[0].SelectMany(book => (book.Editions?.Value ?? new List<Edition>())
                    .Where(edition => edition?.ForeignEditionId.IsNotNullOrWhiteSpace() == true &&
                        edition.Title.IsNotNullOrWhiteSpace() &&
                        (requiredIsbn == null || Isbn.AreEquivalent(edition.Isbn13, requiredIsbn)) &&
                        (book.Id <= 0 || edition.BookId <= 0 || edition.BookId == book.Id) &&
                        !IdentificationService.ConflictsWithGuideTitle(taggedBookTitle, edition, book) &&
                        !IdentificationService.HasConflictingCandidateOrdinal(taggedBookTitle, exactTitle, NormalizeLibraryIdentity(edition.Title)))
                    .Select(edition => (Book: book, Edition: edition)))
                .Where(item => ImportFormat.ForEdition(item.Edition) == format || (!requireTypedEdition && ImportFormat.NeedsDigitalEdition(item.Edition)))
                .Where(item => !HasConflictingFileIdentifiers(item.Edition, release.LocalBooks))
                .OrderByDescending(item => ImportFormat.ForEdition(item.Edition) == format)
                .ThenByDescending(item => CountMatchingFileIdentifiers(item.Edition, release.LocalBooks))
                .ThenByDescending(item => item.Book.Id > 0);

            foreach (var (candidate, edition) in editions)
            {
                var authorId = GetLibraryAuthorId(candidate);
                var scopedAuthor = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, authorId, null) != null;
                var localAuthor = ResolveLibraryAuthor(candidate, scopedAuthor, authorId);
                if (localAuthor != null)
                {
                    if (NormalizeLibraryAuthor(localAuthor.Name) != exactAuthor ||
                        (requiredIsbn != null && CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, localAuthor.ForeignAuthorId, null) == null))
                    {
                        continue;
                    }

                    candidate.Author = localAuthor;
                    candidate.AuthorMetadata = localAuthor.Metadata.Value;
                    candidate.AuthorMetadataId = localAuthor.AuthorMetadataId;
                    return (candidate, edition, false);
                }

                if (requiredIsbn == null && addNewAuthors && scopedAuthor && NormalizeLibraryAuthor(candidate.Author?.Value?.Name) == exactAuthor)
                {
                    return (candidate, edition, false);
                }
            }

            return default;
        }

        private static bool HasConflictingFileIdentifiers(Edition edition, List<LocalBook> files)
        {
            var isbn = Isbn.AsIsbn13(edition.Isbn13);
            var asin = NormalizeLibraryAsin(edition.Asin);
            return files.Any(file =>
            {
                var tags = file.FileTrackInfo;
                return (isbn != null && tags?.IsbnConfidence >= 0.9 && Isbn.AsIsbn13(tags.Isbn) is { } fileIsbn && fileIsbn != isbn) ||
                    (asin != null && tags?.AsinConfidence >= 0.9 && NormalizeLibraryAsin(tags.Asin) is { } fileAsin && fileAsin != asin);
            });
        }

        private static int CountMatchingFileIdentifiers(Edition edition, List<LocalBook> files)
        {
            var asin = NormalizeLibraryAsin(edition.Asin);
            return files.Count(file =>
                (file.FileTrackInfo?.IsbnConfidence >= 0.9 && Isbn.AreEquivalent(file.FileTrackInfo.Isbn, edition.Isbn13)) ||
                (asin != null && file.FileTrackInfo?.AsinConfidence >= 0.9 && NormalizeLibraryAsin(file.FileTrackInfo.Asin) == asin));
        }

        private static string NormalizeLibraryAsin(string value)
        {
            var asin = value?.Trim();
            return asin?.Length == 10 && asin.All(char.IsAsciiLetterOrDigit) ? asin.ToUpperInvariant() : null;
        }

        private Author ResolveLibraryAuthor(Book candidate, bool scopedAuthor, string authorId)
        {
            if (candidate.Author?.Value is { Id: > 0 } author)
            {
                return author;
            }

            if (CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, candidate.ForeignBookId, null) != null &&
                _bookService.FindById(candidate.ForeignBookId)?.Author?.Value is { Id: > 0 } owner)
            {
                return owner;
            }

            return scopedAuthor && _authorService.FindById(authorId) is { Id: > 0 } existing ? existing : null;
        }

        private static string GetLibraryWorkIdentity(Book book)
        {
            var source = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, book.ForeignBookId, null);
            return source != null ? $"{source.Provider}:{source.ProviderId}" : book.Id > 0 ? $"local:{book.Id}" : null;
        }

        internal static string GetLibraryAuthorId(Book book)
        {
            return book?.Author?.Value?.Metadata?.Value?.ForeignAuthorId ?? book?.AuthorMetadata?.Value?.ForeignAuthorId;
        }

        private static bool ConflictsWithLibraryIdentity(ParsedTrackInfo tags, string title, string author)
        {
            var tagTitle = NormalizeLibraryIdentity(tags?.BookTitle);
            var names = (tags?.Authors ?? new List<string>()).Select(NormalizeLibraryAuthor).Where(name => name != null).ToList();
            return (tagTitle != null && tagTitle != title) || (names.Count > 0 && !names.Contains(author));
        }

        private static string NormalizeLibraryAuthor(string value)
        {
            var normalized = NormalizeLibraryIdentity(value);
            return normalized == null ? null : Regex.Replace(normalized, @"(?<=\b\p{L}\.) (?=\p{L}\.)", string.Empty);
        }

        internal static string NormalizeLibraryIdentity(string value)
        {
            return value.IsNullOrWhiteSpace() ? null : string.Join(" ", value.Normalize(NormalizationForm.FormKC)
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        private ImportDecision<LocalEdition> GetDecision(LocalEdition localEdition, DownloadClientItem downloadClientItem)
        {
            ImportDecision<LocalEdition> decision = null;

            if (localEdition.Edition == null)
            {
                var reason = localEdition.IdentificationHoldReason ?? LibraryImportHoldReason.NoAcceptedWork;
                var message = reason switch
                {
                    LibraryImportHoldReason.LowConfidence => "The match could not confidently identify a new author. Select the intended author manually.",
                    LibraryImportHoldReason.CompetingEditions => "More than one edition matches this file's ISBN and format. Select the intended edition manually.",
                    LibraryImportHoldReason.ProviderIdentityConflict => "Metadata provider results conflict with this file's book identity. Select the intended work manually.",
                    _ => $"Couldn't find similar book for {localEdition}"
                };
                decision = new ImportDecision<LocalEdition>(localEdition, new Rejection(message, holdReason: reason));
            }
            else
            {
                var reasons = _bookSpecifications.Select(c => EvaluateSpec(c, localEdition, downloadClientItem))
                    .Where(c => c != null);

                decision = new ImportDecision<LocalEdition>(localEdition, reasons.ToArray());
            }

            if (decision == null)
            {
                _logger.Error("Unable to make a decision on {0}", localEdition);
            }
            else if (decision.Rejections.Any())
            {
                _logger.Debug("Book rejected for the following reasons: {0}", string.Join(", ", decision.Rejections));
            }
            else
            {
                _logger.Debug("Book accepted");
            }

            return decision;
        }

        private ImportDecision<LocalBook> GetDecision(LocalBook localBook, DownloadClientItem downloadClientItem)
        {
            ImportDecision<LocalBook> decision = null;

            if (localBook.Book == null)
            {
                decision = new ImportDecision<LocalBook>(localBook, new Rejection($"Couldn't parse book from: {localBook.FileTrackInfo}", holdReason: LibraryImportHoldReason.NoAcceptedWork));
            }
            else
            {
                var reasons = _trackSpecifications.Select(c => EvaluateSpec(c, localBook, downloadClientItem))
                    .Where(c => c != null);

                decision = new ImportDecision<LocalBook>(localBook, reasons.ToArray());
            }

            if (decision == null)
            {
                _logger.Error("Unable to make a decision on {0}", localBook.Path);
            }
            else if (decision.Rejections.Any())
            {
                _logger.Debug("File rejected for the following reasons: {0}", string.Join(", ", decision.Rejections));
            }
            else
            {
                _logger.Debug("File accepted");
            }

            return decision;
        }

        private Rejection EvaluateSpec<T>(IImportDecisionEngineSpecification<T> spec, T item, DownloadClientItem downloadClientItem)
        {
            try
            {
                var result = spec.IsSatisfiedBy(item, downloadClientItem);

                if (!result.Accepted)
                {
                    return new Rejection(result.Reason, holdReason: result.HoldReason);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't evaluate decision on {0}", item);
                return new Rejection($"{spec.GetType().Name}: {e.Message}");
            }

            return null;
        }
    }
}
