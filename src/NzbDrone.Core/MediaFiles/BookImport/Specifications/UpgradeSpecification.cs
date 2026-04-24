using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class UpgradeSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        private readonly IConfigService _configService;
        private readonly ICustomFormatCalculationService _customFormatCalculationService;
        private readonly IAuthorFormatProfileService _formatProfileService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly Logger _logger;

        public UpgradeSpecification(IConfigService configService,
                                    ICustomFormatCalculationService customFormatCalculationService,
                                    IAuthorFormatProfileService formatProfileService,
                                    IQualityProfileService qualityProfileService,
                                    Logger logger)
        {
            _configService = configService;
            _customFormatCalculationService = customFormatCalculationService;
            _formatProfileService = formatProfileService;
            _qualityProfileService = qualityProfileService;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalBook item, DownloadClientItem downloadClientItem)
        {
            var files = item.Book?.BookFiles?.Value;
            if (files == null || !files.Any())
            {
                // No existing books, skip.  This guards against new authors not having a QualityProfile.
                return Decision.Accept();
            }

            var downloadPropersAndRepacks = _configService.DownloadPropersAndRepacks;
            var incomingFormatType = Quality.GetFormatType(item.Quality?.Quality);

            // Resolve format-specific quality profile when dual format tracking is enabled
            QualityProfile qualityProfile = item.Author.QualityProfile;

            if (_configService.EnableDualFormatTracking)
            {
                var formatProfile = _formatProfileService.GetByAuthorIdAndFormat(item.Author.Id, incomingFormatType);
                if (formatProfile != null)
                {
                    var formatQP = _qualityProfileService.Get(formatProfile.QualityProfileId);
                    if (formatQP != null)
                    {
                        qualityProfile = formatQP;
                    }
                }
            }

            var qualityComparer = new QualityModelComparer(qualityProfile);

            foreach (var bookFile in files)
            {
                // When dual format tracking is active, only compare against files of the same format type
                if (_configService.EnableDualFormatTracking)
                {
                    var fileFormatType = Quality.GetFormatType(bookFile.Quality?.Quality);
                    if (fileFormatType != incomingFormatType)
                    {
                        continue;
                    }
                }

                var qualityCompare = qualityComparer.Compare(item.Quality.Quality, bookFile.Quality.Quality);

                if (qualityCompare < 0)
                {
                    _logger.Debug("This file isn't a quality upgrade for all books. Skipping {0}", item.Path);
                    return Decision.Reject("Not an upgrade for existing book file(s)");
                }

                if (qualityCompare == 0 && downloadPropersAndRepacks != ProperDownloadTypes.DoNotPrefer &&
                    item.Quality.Revision.CompareTo(bookFile.Quality.Revision) < 0)
                {
                    _logger.Debug("This file isn't a quality upgrade for all books. Skipping {0}", item.Path);
                    return Decision.Reject("Not an upgrade for existing book file(s)");
                }
            }

            return Decision.Accept();
        }
    }
}
