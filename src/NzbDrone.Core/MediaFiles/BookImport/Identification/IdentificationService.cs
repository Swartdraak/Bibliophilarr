using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public interface IIdentificationService
    {
        List<LocalEdition> Identify(List<LocalBook> localTracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config, ImportRunSummary runSummary = null);
    }

    public class IdentificationService : IIdentificationService
    {
        // Maximum distance allowed when auto-adding a new author during import.
        // Lower distance = better match. 0.25 allows reasonably confident matches
        // while preventing false positives from badly tagged files.
        private const double AutoAddAuthorDistanceThreshold = 0.25;

        private static readonly Regex GuideSuffix = new Regex(@"^(?:\s*[:(,–—]\s*|\s+-\s+|\s+)(?:TEACHER(?:['’]S)?|TEACHERS['’]|TEACHING|STUDY|READING) GUIDE\b", RegexOptions.CultureInvariant);
        private static readonly Regex ExplicitSeriesVolume = new Regex(@"^(.+?)[\s:,(–—-]+(?:BOOK|VOL(?:UME)?\.?)[\s#]+([0-9]+)\)?$", RegexOptions.CultureInvariant);
        private static readonly Regex BareSeriesVolume = new Regex(@"^(.+\p{L})\s+([0-9]{1,3})$", RegexOptions.CultureInvariant);
        private static readonly Regex TrackTitleSuffix = new Regex(@"(?:^|[\s:,(–—-]+)(?:CHAPTERS?|DISCS?|PARTS?|TRACKS?)\s*[-#]?\s*[0-9]+(?:\s*[/–-]\s*[0-9]+)?\)?$", RegexOptions.CultureInvariant);
        private static readonly Regex SubtitleDelimiter = new Regex(@"\s*:\s*|\s+[-–—]\s+", RegexOptions.CultureInvariant);
        private static readonly Regex GenreLine = new Regex(@"(?:: | \()A LITRPG ADVENTURE\)?$", RegexOptions.CultureInvariant);
        private static readonly Regex FileCopySuffix = new Regex(@"\s+\([1-9][0-9]*\)$", RegexOptions.CultureInvariant);
        private static readonly Regex NumberedSeriesWork = new Regex(@"^[^:]+\s+(?:(?:BOOK|VOLUME|VOL\.?)\s+[0-9]+|0[0-9]{1,2}): (.+)$", RegexOptions.CultureInvariant);
        private static readonly Regex DistinctWorkScope = new Regex(@"^(?:(?:THE )?(?:LITTLE )?BOOK OF QUOTES|OMNIBUS|BOX(?:ED)? SET|BOOKS? [0-9]+(?:\s*[:–-]\s*| TO )[0-9]+)\b", RegexOptions.CultureInvariant);

        private readonly ITrackGroupingService _trackGroupingService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IAugmentingService _augmentingService;
        private readonly ICandidateService _candidateService;
        private readonly IAuthorService _authorService;
        private readonly IConfigService _configService;
        private readonly IImportRunTracker _importRunTracker;
        private readonly Logger _logger;

        public IdentificationService(ITrackGroupingService trackGroupingService,
                                     IMetadataTagService metadataTagService,
                                     IAugmentingService augmentingService,
                                     ICandidateService candidateService,
                                     IAuthorService authorService,
                                     IConfigService configService,
                                     IImportRunTracker importRunTracker,
                                     Logger logger)
        {
            _trackGroupingService = trackGroupingService;
            _metadataTagService = metadataTagService;
            _augmentingService = augmentingService;
            _candidateService = candidateService;
            _authorService = authorService;
            _configService = configService;
            _importRunTracker = importRunTracker;
            _logger = logger;
        }

        public List<LocalEdition> GetLocalBookReleases(List<LocalBook> localTracks, bool singleRelease)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<LocalEdition> releases;
            if (singleRelease)
            {
                releases = localTracks.GroupBy(x => ImportFormat.ForPath(x.Path))
                    .Select(group => new LocalEdition(group.ToList()))
                    .ToList();
            }
            else
            {
                releases = _trackGroupingService.GroupTracks(localTracks);
            }

            _logger.Debug($"Sorted {localTracks.Count} tracks into {releases.Count} releases in {watch.ElapsedMilliseconds}ms");

            foreach (var localRelease in releases)
            {
                try
                {
                    _augmentingService.Augment(localRelease);
                }
                catch (AugmentingFailedException)
                {
                    _logger.Warn($"Augmentation failed for {localRelease}");
                }
            }

            return releases;
        }

        public List<LocalEdition> Identify(List<LocalBook> localTracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config, ImportRunSummary runSummary = null)
        {
            // 1 group localTracks so that we think they represent a single release
            // 2 get candidates given specified author, book and release.  Candidates can include extra files already on disk.
            // 3 find best candidate
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Starting book identification");

            // Preserve the independent tag author before release augmentation can fill it from the filename.
            var libraryAuthors = !config.NewDownload && idOverrides.Edition == null
                ? localTracks.Distinct().ToDictionary(track => track, ReadCorroboratedFilenameAuthor)
                : null;
            var releases = GetLocalBookReleases(localTracks, config.SingleRelease);

            if (runSummary != null)
            {
                runSummary.GroupingMs = watch.ElapsedMilliseconds;
                runSummary.ReleasesGrouped = releases.Count;
            }

            var maxWorkers = GetSafeWorkerCount(_configService.IdentificationWorkerCount, releases.Count);

            _logger.Debug($"Using up to {maxWorkers} identification worker(s) for {releases.Count} release(s)");

            var processed = 0;
            var options = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };

            Parallel.ForEach(releases, options, localRelease =>
            {
                var current = Interlocked.Increment(ref processed);
                _logger.ProgressInfo($"Identifying book {current}/{releases.Count}");
                _logger.Debug($"Identifying book files:\n{localRelease.LocalBooks.Select(x => x.Path).ConcatToString("\n")}");

                try
                {
                    IdentifyRelease(localRelease, idOverrides, config, runSummary, libraryAuthors);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Error identifying release");
                    if (runSummary != null)
                    {
                        Interlocked.Increment(ref runSummary.ErrorsRef);
                    }
                }
            });

            watch.Stop();

            if (runSummary != null)
            {
                runSummary.IdentificationMs = watch.ElapsedMilliseconds;
            }

            _logger.Debug($"Track identification for {localTracks.Count} tracks took {watch.ElapsedMilliseconds}ms");

            return releases;
        }

        private static int GetSafeWorkerCount(int configuredWorkers, int itemCount)
        {
            if (itemCount <= 0)
            {
                return 1;
            }

            return Math.Max(1, Math.Min(configuredWorkers, itemCount));
        }

        private List<LocalBook> ToLocalTrack(IEnumerable<BookFile> trackfiles, LocalEdition localRelease)
        {
            var scanned = trackfiles.Join(localRelease.LocalBooks, t => t.Path, l => l.Path, (track, localTrack) => localTrack);
            var toScan = trackfiles.ExceptBy(t => t.Path, scanned, s => s.Path, StringComparer.InvariantCulture);
            var localTracks = scanned.Concat(toScan.Select(x => new LocalBook
            {
                Path = x.Path,
                Size = x.Size,
                Modified = x.Modified,
                FileTrackInfo = _metadataTagService.ReadTags((FileInfoBase)new FileInfo(x.Path)),
                ExistingFile = true,
                AdditionalFile = true,
                Quality = x.Quality
            }))
            .ToList();

            localTracks.ForEach(x => _augmentingService.Augment(x, true));

            return localTracks;
        }

        private void IdentifyRelease(LocalEdition localBookRelease, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config, ImportRunSummary runSummary, IReadOnlyDictionary<LocalBook, string> libraryAuthors)
        {
            if (HasConflictingWorkOrdinal(localBookRelease, idOverrides, config))
            {
                _logger.Debug("Holding {0} because the filename and embedded title give different numbers for the same series", localBookRelease);
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var usedRemote = false;
            var taggedBookTitle = GetReliableTaggedBookTitle(localBookRelease);
            var corroboratedAuthors = libraryAuthors == null || taggedBookTitle == null
                ? null
                : localBookRelease.LocalBooks.Select(track => libraryAuthors.GetValueOrDefault(track)).Where(author => author != null).ToHashSet();

            bool AllowsCandidate(CandidateEdition candidate) =>
                AllowsGuideMatch(taggedBookTitle, candidate.Edition, idOverrides.Edition) &&
                (corroboratedAuthors == null || AllowsLibraryScanMatch(localBookRelease, taggedBookTitle, corroboratedAuthors, candidate.Edition));

            IEnumerable<CandidateEdition> candidateReleases = _candidateService.GetDbCandidatesFromTags(localBookRelease, idOverrides, config.IncludeExisting)
                .Where(candidate => localBookRelease.LocalBooks.All(track => ImportFormat.IsCompatible(track.Path, candidate.Edition)))
                .Where(AllowsCandidate)
                .ToList();

            // Read companion tracks only for eligible editions.
            var allLocalTracks = ToLocalTrack(candidateReleases
                .SelectMany(candidate => GetExistingFiles(candidate, localBookRelease))
                .DistinctBy(x => x.Path), localBookRelease);

            _logger.Debug($"Retrieved {allLocalTracks.Count} possible tracks in {watch.ElapsedMilliseconds}ms");

            var preferredFormat = _configService.EnableDualFormatTracking
                ? GetDominantFormatType(localBookRelease.LocalBooks)
                : (FormatType?)null;

            if (!candidateReleases.Any())
            {
                _logger.Debug("No local candidates found, trying remote");
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides)
                    .Where(AllowsCandidate);
                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => AuthorExistsLocally(x.Edition, config.NewDownload));
                }

                usedRemote = true;
                _importRunTracker.IncrementRemoteSearches(runSummary);
            }

            GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, out var seenCandidate, preferredFormat);

            if (!seenCandidate)
            {
                // can't find any candidates even after using remote search
                // populate the overrides and return
                foreach (var localTrack in localBookRelease.LocalBooks)
                {
                    localTrack.Edition = ImportFormat.IsCompatible(localTrack.Path, idOverrides.Edition) ? idOverrides.Edition : null;
                    localTrack.Book = idOverrides.Book;
                    localTrack.Author = idOverrides.Author;
                }

                return;
            }

            // If the result isn't great and we haven't tried remote candidates, try looking for remote candidates
            // OpenLibrary may have a better edition of a local book
            if (localBookRelease.Distance.NormalizedDistance() > 0.15 && !usedRemote)
            {
                _logger.Debug("Match not good enough, trying remote candidates");
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides)
                    .Where(AllowsCandidate);

                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => AuthorExistsLocally(x.Edition, config.NewDownload));
                }

                _importRunTracker.IncrementRemoteSearches(runSummary);
                GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, out _, preferredFormat);
            }

            _logger.Debug($"Best release found in {watch.ElapsedMilliseconds}ms");

            // If auto-adding authors is enabled and this match would add a new author,
            // verify the match quality meets the threshold before proceeding.
            // This prevents adding authors based on poorly-tagged or misidentified files.
            if (config.AddNewAuthors &&
                localBookRelease.Edition != null &&
                !AuthorExistsLocally(localBookRelease.Edition, config.NewDownload) &&
                localBookRelease.Distance.NormalizedDistance() > AutoAddAuthorDistanceThreshold)
            {
                var authorName = localBookRelease.Edition.Book?.Value?.AuthorMetadata?.Value?.Name ?? "Unknown";
                _logger.Info("Rejecting match: would add author '{0}' but distance {1:F3} exceeds auto-add threshold {2:F2}. " +
                             "Add the author manually if this is correct.",
                             authorName,
                             localBookRelease.Distance.NormalizedDistance(),
                             AutoAddAuthorDistanceThreshold);

                // Clear the match to prevent auto-adding a low-confidence author
                localBookRelease.Edition = null;
                localBookRelease.Distance = new Distance();
                localBookRelease.ExistingTracks = new List<LocalBook>();
            }

            if (runSummary != null && localBookRelease.Edition != null)
            {
                _importRunTracker.RecordMatchQuality(runSummary, localBookRelease.Distance.NormalizedDistance());
            }

            localBookRelease.PopulateMatch(config.KeepAllEditions);

            _logger.Debug($"IdentifyRelease done in {watch.ElapsedMilliseconds}ms");
        }

        internal static string CaptureReliableTaggedBookTitle(ParsedTrackInfo tags)
        {
            var bookTitle = tags.BookTitleConfidence >= 0.9 ? ImportDecisionMaker.NormalizeLibraryIdentity(tags.BookTitle) : null;

            // Audio can identify a guide in its track title even when the album names the original work.
            // Require the full base and a guide suffix so ordinary chapter labels do not override the album.
            return bookTitle != null && HasGuideSuffix(tags.Title, bookTitle)
                ? ImportDecisionMaker.NormalizeLibraryIdentity(tags.Title)
                : bookTitle;
        }

        internal static string GetReliableTaggedBookTitle(LocalEdition release)
        {
            var titles = release.LocalBooks.Select(file => file.ReliableTaggedBookTitle)
                .Where(title => title != null)
                .Distinct()
                .Take(2)
                .ToList();
            return titles.Count == 1 ? titles[0] : null;
        }

        internal static bool HasConflictingWorkOrdinal(LocalEdition release, IdentificationOverrides overrides, ImportDecisionMakerConfig config)
        {
            return !config.NewDownload && overrides.Edition == null && release.LocalBooks.Any(HasConflictingWorkOrdinal);
        }

        private static bool HasConflictingWorkOrdinal(LocalBook track)
        {
            if (track.ReliableTaggedBookTitle == null)
            {
                return false;
            }

            var (_, filename) = ReadFilenameIdentity(track);
            var (tagSeries, tagPosition, _) = ReadWorkOrdinal(track.ReliableTaggedBookTitle);
            var title = ImportDecisionMaker.NormalizeLibraryIdentity(filename);
            var (fileSeries, filePosition, _) = ReadWorkOrdinal(title == null ? null : FileCopySuffix.Replace(title, string.Empty));
            return tagPosition > 0 && filePosition > 0 && tagPosition != filePosition && tagSeries == fileSeries;
        }

        private static (string Series, int Position, bool IsExplicit) ReadWorkOrdinal(string title, string explicitSeries = null)
        {
            if (title == null)
            {
                return default;
            }

            var work = NormalizeSubtitleNotation(TrackTitleSuffix.Replace(title, string.Empty));

            // The other title already names this series and explicitly labels an installment.
            // Only then can text after the installment be treated as a subtitle.
            if (explicitSeries != null && work.Length > explicitSeries.Length && work.StartsWith(explicitSeries, StringComparison.Ordinal))
            {
                var subtitleStart = work.IndexOf(": ", explicitSeries.Length + 1, StringComparison.Ordinal);
                if (subtitleStart >= 0)
                {
                    work = work[..subtitleStart];
                }
            }

            var explicitlyNumbered = ExplicitSeriesVolume.Match(work);
            if (explicitlyNumbered.Success && int.TryParse(explicitlyNumbered.Groups[2].Value, out var explicitPosition))
            {
                return (explicitlyNumbered.Groups[1].Value, explicitPosition, true);
            }

            // Require a complete common stem. Four-digit bare suffixes can be publication years.
            var bare = BareSeriesVolume.Match(work);
            return bare.Success && int.TryParse(bare.Groups[2].Value, out var position)
                ? (bare.Groups[1].Value, position, false)
                : default;
        }

        internal static bool ConflictsWithGuideTitle(string taggedBookTitle, Edition edition, Book book = null)
        {
            return taggedBookTitle != null &&
                (HasGuideSuffix(edition?.Title, taggedBookTitle) ||
                 HasGuideSuffix((book ?? edition?.Book?.Value)?.Title, taggedBookTitle));
        }

        private static bool HasGuideSuffix(string title, string taggedBookTitle)
        {
            var normalized = ImportDecisionMaker.NormalizeLibraryIdentity(title);
            return normalized != null && normalized.StartsWith(taggedBookTitle, StringComparison.Ordinal) &&
                GuideSuffix.IsMatch(normalized.AsSpan(taggedBookTitle.Length));
        }

        private bool AllowsGuideMatch(string taggedBookTitle, Edition candidate, Edition selected)
        {
            if (selected != null &&
                (selected == candidate || (selected.Id > 0 && selected.Id == candidate?.Id) ||
                 (selected.ForeignEditionId.IsNotNullOrWhiteSpace() && selected.ForeignEditionId == candidate?.ForeignEditionId)))
            {
                return true;
            }

            if (ConflictsWithGuideTitle(taggedBookTitle, candidate))
            {
                _logger.Debug("Skipping guide candidate {0} because the embedded title identifies its base work", candidate);
                return false;
            }

            return true;
        }

        private static string ReadCorroboratedFilenameAuthor(LocalBook track)
        {
            var tags = track.FileTrackInfo;
            if (track.ReliableTaggedBookTitle == null || tags == null || tags.AuthorConfidence < 0.9 || track.CalibreId != 0)
            {
                return null;
            }

            var (author, title) = ReadFilenameIdentity(track);
            if (author == null)
            {
                return null;
            }

            var embeddedTitle = NormalizeSubtitleNotation(track.ReliableTaggedBookTitle);
            var filenameTitle = NormalizeSubtitleNotation(ImportDecisionMaker.NormalizeLibraryIdentity(title));
            if (filenameTitle == null ||
                (!IsFullWorkTitle(embeddedTitle, filenameTitle) &&
                 !IsFullWorkTitle(embeddedTitle, FileCopySuffix.Replace(filenameTitle, string.Empty))))
            {
                return null;
            }

            return author;
        }

        private static (string Author, string Title) ReadFilenameIdentity(LocalBook track)
        {
            var filename = Path.GetFileNameWithoutExtension(track.Path);
            var split = filename?.IndexOf(" - ", StringComparison.Ordinal) ?? -1;
            if (split <= 0)
            {
                return (null, filename);
            }

            var author = filename[..split].CleanAuthorName();
            return author.Length > 0 && track.FileTrackInfo?.Authors?.Any(name => AuthorNameMatches(name, author)) == true
                ? (author, filename.Substring(split + 3))
                : (null, filename);
        }

        private static bool AuthorNameMatches(string name, string normalizedAuthor)
        {
            if (name.CleanAuthorName() == normalizedAuthor)
            {
                return true;
            }

            var components = name?.Split(',');
            return components is { Length: 2 } && components.All(component => component.IsNotNullOrWhiteSpace()) &&
                $"{components[1]} {components[0]}".CleanAuthorName() == normalizedAuthor;
        }

        private static string NormalizeSubtitleNotation(string title)
        {
            // This generic genre label does not identify the work. Preserve every numeral
            // in the remaining title so similarly named installments cannot corroborate one another.
            return title == null ? null : GenreLine.Replace(SubtitleDelimiter.Replace(title, ": "), string.Empty);
        }

        private static bool IsFullWorkTitle(string title, string workTitle)
        {
            if (title == workTitle)
            {
                return true;
            }

            var series = NumberedSeriesWork.Match(title);
            return series.Success && series.Groups[1].Value == workTitle;
        }

        private bool AllowsLibraryScanMatch(LocalEdition release, string taggedTitle, IReadOnlySet<string> corroboratedAuthors, Edition edition)
        {
            var book = edition?.Book?.Value;
            var workTitle = ImportDecisionMaker.NormalizeLibraryIdentity(book?.Title);
            var editionTitle = ImportDecisionMaker.NormalizeLibraryIdentity(edition?.Title);
            var ordinal = ReadWorkOrdinal(taggedTitle);
            if (HasDifferentVolume(workTitle, taggedTitle, ordinal) || HasDifferentVolume(editionTitle, taggedTitle, ordinal))
            {
                _logger.Debug("Skipping library scan candidate {0} because it names a different volume of the same series", edition);
                return false;
            }

            var author = book?.AuthorMetadata?.Value?.Name ?? book?.Author?.Value?.Name;
            var confirmed = NormalizeSubtitleNotation(TrackTitleSuffix.Replace(taggedTitle, string.Empty));
            if (confirmed.Length > 0 && corroboratedAuthors.Any(knownAuthor => AuthorNameMatches(author, knownAuthor)) &&
                !TitlesAgree(confirmed, workTitle) && !TitlesAgree(confirmed, editionTitle) &&
                !HasNonconflictingEditionIdentifier(release, edition))
            {
                _logger.Debug("Skipping library scan candidate {0} because its title contradicts the embedded title and filename", edition);
                return false;
            }

            return true;
        }

        private static bool HasDifferentVolume(string title, string taggedTitle, (string Series, int Position, bool IsExplicit) ordinal)
        {
            var other = ReadWorkOrdinal(title, ordinal.IsExplicit ? ordinal.Series : null);
            if (other.IsExplicit && !ordinal.IsExplicit)
            {
                ordinal = ReadWorkOrdinal(taggedTitle, other.Series);
            }

            return (ordinal.IsExplicit || other.IsExplicit) && ordinal.Position > 0 && other.Position > 0 &&
                other.Position != ordinal.Position && other.Series == ordinal.Series;
        }

        private static bool TitlesAgree(string taggedTitle, string candidateTitle)
        {
            if (candidateTitle == null)
            {
                return false;
            }

            candidateTitle = NormalizeSubtitleNotation(TrackTitleSuffix.Replace(candidateTitle, string.Empty));
            return candidateTitle.Length > 0 &&
                (IsFullWorkTitle(taggedTitle, candidateTitle) || IsFullWorkTitle(candidateTitle, taggedTitle) ||
                 HasSubtitle(taggedTitle, candidateTitle) || HasSubtitle(candidateTitle, taggedTitle));
        }

        private static bool HasSubtitle(string title, string longerTitle)
        {
            if (!longerTitle.StartsWith(title, StringComparison.Ordinal))
            {
                return false;
            }

            var suffix = longerTitle.AsSpan(title.Length).TrimStart();
            return suffix.Length > 1 && suffix[0] is '(' or ':' &&
                !GuideSuffix.IsMatch(suffix) && !DistinctWorkScope.IsMatch(suffix[1..].TrimStart());
        }

        private static bool HasNonconflictingEditionIdentifier(LocalEdition release, Edition edition)
        {
            var editionIsbn = Isbn.AsIsbn13(edition?.Isbn13);
            var editionAsin = NormalizeAsin(edition?.Asin);
            var match = false;
            foreach (var track in release.LocalBooks)
            {
                var tags = track.FileTrackInfo;
                var isbn = tags?.IsbnConfidence >= 0.9 ? Isbn.AsIsbn13(tags.Isbn) : null;
                var asin = tags?.AsinConfidence >= 0.9 ? NormalizeAsin(tags.Asin) : null;
                if ((isbn != null && editionIsbn != null && isbn != editionIsbn) ||
                    (asin != null && editionAsin != null && asin != editionAsin))
                {
                    return false;
                }

                match |= (isbn != null && isbn == editionIsbn) || (asin != null && asin == editionAsin);
            }

            return match;
        }

        private static string NormalizeAsin(string value)
        {
            return value?.Length == 10 && value.All(char.IsAsciiLetterOrDigit) ? value.ToUpperInvariant() : null;
        }

        private void GetBestRelease(LocalEdition localBookRelease, IEnumerable<CandidateEdition> candidateReleases, List<LocalBook> extraTracksOnDisk, out bool seenCandidate, FormatType? preferredFormat = null)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Matching {0} track files against candidates", localBookRelease.TrackCount);
            _logger.Trace("Processing files:\n{0}", string.Join("\n", localBookRelease.LocalBooks.Select(x => x.Path)));

            var bestEffectiveDistance = localBookRelease.Edition != null ? localBookRelease.Distance.NormalizedDistance() : 1.0;

            // Apply initial format bias to existing best distance if applicable
            if (preferredFormat.HasValue && localBookRelease.Edition != null)
            {
                var existingFormat = localBookRelease.Edition.IsEbook ? FormatType.Ebook : FormatType.Audiobook;
                if (existingFormat == preferredFormat.Value)
                {
                    bestEffectiveDistance -= 0.02;
                }
            }

            seenCandidate = false;

            foreach (var candidateRelease in candidateReleases)
            {
                var release = candidateRelease.Edition;
                if (localBookRelease.LocalBooks.Any(track => !ImportFormat.IsCompatible(track.Path, release)))
                {
                    continue;
                }

                seenCandidate = true;

                _logger.Debug($"Trying Release {release}");
                var rwatch = System.Diagnostics.Stopwatch.StartNew();

                var extraTrackPaths = GetExistingFiles(candidateRelease, localBookRelease).Select(x => x.Path).ToHashSet();
                var extraTracks = extraTracksOnDisk.Where(x => extraTrackPaths.Contains(x.Path)).ToList();
                var allLocalTracks = localBookRelease.LocalBooks.Concat(extraTracks).DistinctBy(x => x.Path).ToList();

                var distance = DistanceCalculator.BookDistance(allLocalTracks, release);
                var currDistance = distance.NormalizedDistance();

                rwatch.Stop();
                _logger.Debug("Release {0} has distance {1} vs best distance {2} [{3}ms]",
                              release,
                              currDistance,
                              bestEffectiveDistance,
                              rwatch.ElapsedMilliseconds);

                // Format preference: small bonus (0.02) for editions matching the
                // file's format type, acting as a tie-breaker when distances are close.
                var effectiveDistance = currDistance;
                if (preferredFormat.HasValue)
                {
                    var editionFormat = release.IsEbook ? FormatType.Ebook : FormatType.Audiobook;
                    if (editionFormat == preferredFormat.Value)
                    {
                        effectiveDistance -= 0.02;
                    }
                }

                if (effectiveDistance < bestEffectiveDistance)
                {
                    bestEffectiveDistance = effectiveDistance;
                    localBookRelease.Distance = distance;
                    localBookRelease.Edition = release;
                    localBookRelease.ExistingTracks = extraTracks;
                    if (currDistance == 0.0)
                    {
                        break;
                    }
                }
            }

            watch.Stop();
            _logger.Debug($"Best release: {localBookRelease.Edition} Distance {localBookRelease.Distance.NormalizedDistance()} found in {watch.ElapsedMilliseconds}ms");
        }

        private static IEnumerable<BookFile> GetExistingFiles(CandidateEdition candidate, LocalEdition localRelease)
        {
            var incomingFormat = ImportFormat.ForPath(localRelease.LocalBooks.First().Path);

            return candidate.ExistingFiles.Where(file =>
            {
                var existingFormat = ImportFormat.ForPath(file.Path);
                return file.EditionId == candidate.Edition.Id &&
                    ImportFormat.IsCompatible(file.Path, candidate.Edition) &&
                    (!incomingFormat.HasValue || !existingFormat.HasValue || incomingFormat == existingFormat);
            });
        }

        private bool AuthorExistsLocally(Edition edition, bool newDownload)
        {
            var book = edition?.Book?.Value;

            // Allow candidates already under a persisted work or author.
            if (book?.Id > 0 || (!newDownload && book?.Author?.Value?.Id > 0))
            {
                return true;
            }

            // Allow candidates whose author already exists in the local library,
            // even if this specific book hasn't been added to the bibliography yet.
            var foreignAuthorId = newDownload ? book?.Author?.Value?.ForeignAuthorId : ImportDecisionMaker.GetLibraryAuthorId(book);
            if (!newDownload)
            {
                return CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, foreignAuthorId, null) != null &&
                       _authorService.FindById(foreignAuthorId) is { Id: > 0 };
            }

            if (foreignAuthorId.IsNotNullOrWhiteSpace())
            {
                var localAuthor = _authorService.FindById(foreignAuthorId);
                if (localAuthor != null)
                {
                    return true;
                }
            }

            // Also try matching by author name for providers that may not set ForeignAuthorId
            var authorName = book?.Author?.Value?.Name;
            if (authorName.IsNotNullOrWhiteSpace())
            {
                var localAuthor = _authorService.FindByName(authorName);
                if (localAuthor != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static FormatType? GetDominantFormatType(List<LocalBook> localBooks)
        {
            if (localBooks == null || !localBooks.Any())
            {
                return null;
            }

            var formatTypes = localBooks
                .Where(b => b.Quality?.Quality != null)
                .Select(b => Quality.GetFormatType(b.Quality.Quality))
                .ToList();

            if (!formatTypes.Any())
            {
                return null;
            }

            // If all files share the same format, return that format
            if (formatTypes.All(f => f == formatTypes[0]))
            {
                return formatTypes[0];
            }

            // Mixed format files — no preference
            return null;
        }
    }
}
