using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.Parser.Model;

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
                releases = new List<LocalEdition> { new LocalEdition(localTracks) };
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
                    IdentifyRelease(localRelease, idOverrides, config, runSummary);
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

        private void IdentifyRelease(LocalEdition localBookRelease, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config, ImportRunSummary runSummary)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var usedRemote = false;

            IEnumerable<CandidateEdition> candidateReleases = _candidateService.GetDbCandidatesFromTags(localBookRelease, idOverrides, config.IncludeExisting);

            // convert all the TrackFiles that represent extra files to List<LocalTrack>
            // local candidates are actually a list so this is fine to enumerate
            var allLocalTracks = ToLocalTrack(candidateReleases
                .SelectMany(x => x.ExistingFiles)
                .DistinctBy(x => x.Path), localBookRelease);

            _logger.Debug($"Retrieved {allLocalTracks.Count} possible tracks in {watch.ElapsedMilliseconds}ms");

            if (!candidateReleases.Any())
            {
                _logger.Debug("No local candidates found, trying remote");
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides);
                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => AuthorExistsLocally(x.Edition));
                }

                usedRemote = true;
                _importRunTracker.IncrementRemoteSearches(runSummary);
            }

            GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, out var seenCandidate);

            if (!seenCandidate)
            {
                // can't find any candidates even after using remote search
                // populate the overrides and return
                foreach (var localTrack in localBookRelease.LocalBooks)
                {
                    localTrack.Edition = idOverrides.Edition;
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
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides);

                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => AuthorExistsLocally(x.Edition));
                }

                _importRunTracker.IncrementRemoteSearches(runSummary);
                GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, out _);
            }

            _logger.Debug($"Best release found in {watch.ElapsedMilliseconds}ms");

            // If auto-adding authors is enabled and this match would add a new author,
            // verify the match quality meets the threshold before proceeding.
            // This prevents adding authors based on poorly-tagged or misidentified files.
            if (config.AddNewAuthors &&
                localBookRelease.Edition != null &&
                !AuthorExistsLocally(localBookRelease.Edition) &&
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

        private void GetBestRelease(LocalEdition localBookRelease, IEnumerable<CandidateEdition> candidateReleases, List<LocalBook> extraTracksOnDisk, out bool seenCandidate)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Matching {0} track files against candidates", localBookRelease.TrackCount);
            _logger.Trace("Processing files:\n{0}", string.Join("\n", localBookRelease.LocalBooks.Select(x => x.Path)));

            var bestDistance = localBookRelease.Edition != null ? localBookRelease.Distance.NormalizedDistance() : 1.0;
            seenCandidate = false;

            foreach (var candidateRelease in candidateReleases)
            {
                seenCandidate = true;

                var release = candidateRelease.Edition;
                _logger.Debug($"Trying Release {release}");
                var rwatch = System.Diagnostics.Stopwatch.StartNew();

                var extraTrackPaths = candidateRelease.ExistingFiles.Select(x => x.Path).ToList();
                var extraTracks = extraTracksOnDisk.Where(x => extraTrackPaths.Contains(x.Path)).ToList();
                var allLocalTracks = localBookRelease.LocalBooks.Concat(extraTracks).DistinctBy(x => x.Path).ToList();

                var distance = DistanceCalculator.BookDistance(allLocalTracks, release);
                var currDistance = distance.NormalizedDistance();

                rwatch.Stop();
                _logger.Debug("Release {0} has distance {1} vs best distance {2} [{3}ms]",
                              release,
                              currDistance,
                              bestDistance,
                              rwatch.ElapsedMilliseconds);
                if (currDistance < bestDistance)
                {
                    bestDistance = currDistance;
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

        private bool AuthorExistsLocally(Edition edition)
        {
            // Allow candidates where the book is already in the local DB
            if (edition.Book.Value.Id > 0)
            {
                return true;
            }

            // Allow candidates whose author already exists in the local library,
            // even if this specific book hasn't been added to the bibliography yet.
            var foreignAuthorId = edition.Book?.Value?.Author?.Value?.ForeignAuthorId;
            if (foreignAuthorId.IsNotNullOrWhiteSpace())
            {
                var localAuthor = _authorService.FindById(foreignAuthorId);
                if (localAuthor != null)
                {
                    return true;
                }
            }

            // Also try matching by author name for providers that may not set ForeignAuthorId
            var authorName = edition.Book?.Value?.Author?.Value?.Name;
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
    }
}
