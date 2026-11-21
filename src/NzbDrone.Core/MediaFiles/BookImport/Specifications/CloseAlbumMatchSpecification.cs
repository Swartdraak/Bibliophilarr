using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class CloseBookMatchSpecification : IImportDecisionEngineSpecification<LocalEdition>
    {
        private static readonly Regex MultipartLabel = new Regex(
            @"\b(?:chapter|disc|part|track)\s*[-#]?\s*\d+\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private readonly IConfigService _configService;
        private readonly IBookService _bookService;
        private readonly Logger _logger;

        public CloseBookMatchSpecification(IConfigService configService, IBookService bookService, Logger logger)
        {
            _configService = configService;
            _bookService = bookService;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalEdition item, DownloadClientItem downloadClientItem)
        {
            if (HasConflictingKnownAudioWork(item))
            {
                return Decision.RejectWithHoldReason("The embedded audiobook title and filename identify different catalogued works. Select the intended work manually.", LibraryImportHoldReason.ConflictingIdentity);
            }

            var thresholdPercent = Math.Max(50, Math.Min(100, _configService.BookImportMatchThresholdPercent));

            // For app-initiated downloads (tracked downloads), use a more lenient threshold.
            // The search already verified author/book match, so verbose filenames (especially
            // audiobooks with subtitles and series notation) should not cause false rejections.
            if (item.NewDownload && downloadClientItem != null)
            {
                thresholdPercent = Math.Max(50, Math.Min(thresholdPercent, 50));
            }

            var distanceThreshold = 1.0 - (thresholdPercent / 100.0);

            double dist;
            string reasons;

            // strict when a new download
            if (item.NewDownload)
            {
                dist = item.Distance.NormalizedDistance();
                reasons = item.Distance.Reasons;
                if (dist > distanceThreshold)
                {
                    _logger.Debug($"Book match is not close enough: {dist} vs {distanceThreshold} {reasons}. Skipping {item}");
                    return Decision.RejectWithHoldReason($"Book match is not close enough: {1 - dist:P1} vs {thresholdPercent}% {reasons}", LibraryImportHoldReason.LowConfidence);
                }
            }

            // otherwise importing existing files in library
            else
            {
                // get book distance ignoring whether tracks are missing
                dist = item.Distance.NormalizedDistanceExcluding(new List<string> { "missing_tracks", "unmatched_tracks", "ebook_format" });
                reasons = item.Distance.Reasons;
                if (dist > distanceThreshold)
                {
                    _logger.Debug($"Book match is not close enough: {dist} vs {distanceThreshold} {reasons}. Skipping {item}");
                    return Decision.RejectWithHoldReason($"Book match is not close enough: {1 - dist:P1} vs {thresholdPercent}% {reasons}", LibraryImportHoldReason.LowConfidence);
                }
            }

            _logger.Debug($"Accepting release {item}: dist {dist} vs {distanceThreshold} {reasons}");
            return Decision.Accept();
        }

        private bool HasConflictingKnownAudioWork(LocalEdition item)
        {
            if (item.NewDownload || item.LocalBooks is not { Count: 1 })
            {
                return false;
            }

            var track = item.LocalBooks[0];
            var tags = track.FileTrackInfo;
            if (!MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(track.Path) ?? string.Empty) ||
                tags == null || tags.BookTitleConfidence < 0.9 || tags.AuthorConfidence < 0.9 ||
                tags.DiscCount > 1 || tags.DiscNumber > 1 || tags.TrackNumbers?.Any(number => number > 1) == true)
            {
                return false;
            }

            var book = item.Edition?.Book?.Value;
            if (book == null || book.Id <= 0 || book.AuthorMetadataId <= 0)
            {
                return false;
            }

            if (Isbn.AreEquivalent(tags.Isbn, item.Edition.Isbn13) ||
                (tags.Asin?.Length == 10 && string.Equals(tags.Asin, item.Edition.Asin, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // A complete author-prefixed filename can contradict an exact embedded
            // match. Ordinary track labels do not provide independent work identity.
            var filename = Path.GetFileNameWithoutExtension(track.Path);
            var pieces = filename.Split(" - ", StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || MultipartLabel.IsMatch(pieces[1]))
            {
                return false;
            }

            var author = pieces[0].CleanAuthorName();
            var fileTitle = pieces[1].CleanAuthorName();
            var embeddedTitle = tags.BookTitle.CleanAuthorName();
            if (string.IsNullOrEmpty(author) || string.IsNullOrEmpty(fileTitle) || string.IsNullOrEmpty(embeddedTitle) ||
                tags.Authors?.Any(name => name.CleanAuthorName() == author) != true ||
                embeddedTitle != book.Title.CleanAuthorName() || embeddedTitle == fileTitle)
            {
                return false;
            }

            var alternative = _bookService.FindByTitle(book.AuthorMetadataId, pieces[1]);
            return alternative != null && alternative.Id > 0 && alternative.Id != book.Id;
        }
    }
}
