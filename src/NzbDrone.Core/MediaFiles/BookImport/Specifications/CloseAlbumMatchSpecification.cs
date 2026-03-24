using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class CloseBookMatchSpecification : IImportDecisionEngineSpecification<LocalEdition>
    {
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public CloseBookMatchSpecification(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalEdition item, DownloadClientItem downloadClientItem)
        {
            var thresholdPercent = Math.Max(50, Math.Min(100, _configService.BookImportMatchThresholdPercent));
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
                    return Decision.Reject($"Book match is not close enough: {1 - dist:P1} vs {thresholdPercent}% {reasons}");
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
                    return Decision.Reject($"Book match is not close enough: {1 - dist:P1} vs {thresholdPercent}% {reasons}");
                }
            }

            _logger.Debug($"Accepting release {item}: dist {dist} vs {distanceThreshold} {reasons}");
            return Decision.Accept();
        }
    }
}
