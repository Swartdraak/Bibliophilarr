using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class QualityAllowedByProfileSpecification : IDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly IAuthorFormatProfileService _formatProfileService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly Logger _logger;

        public QualityAllowedByProfileSpecification(IConfigService configService,
                                                    IAuthorFormatProfileService formatProfileService,
                                                    IQualityProfileService qualityProfileService,
                                                    Logger logger)
        {
            _configService = configService;
            _formatProfileService = formatProfileService;
            _qualityProfileService = qualityProfileService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            _logger.Debug("Checking if report meets quality requirements. {0}", subject.ParsedBookInfo.Quality);

            var quality = subject.ParsedBookInfo.Quality.Quality;
            var profile = subject.Author.QualityProfile.Value;

            if (_configService.EnableDualFormatTracking)
            {
                var formatType = Quality.GetFormatType(quality);
                subject.ResolvedFormatType = formatType;

                var formatProfile = _formatProfileService.GetByAuthorIdAndFormat(subject.Author.Id, formatType);

                if (formatProfile != null)
                {
                    var formatQualityProfile = _qualityProfileService.Get(formatProfile.QualityProfileId);

                    if (formatQualityProfile != null)
                    {
                        _logger.Debug("Using format-specific quality profile '{0}' for {1}", formatQualityProfile.Name, formatType);
                        profile = formatQualityProfile;
                        subject.ResolvedQualityProfile = formatQualityProfile;
                    }
                }
            }

            var qualityIndex = profile.GetIndex(quality);
            var qualityOrGroup = profile.Items[qualityIndex.Index];

            if (!qualityOrGroup.Allowed)
            {
                _logger.Debug("Quality {0} rejected by quality profile", quality);
                return Decision.Reject("{0} is not wanted in profile", quality);
            }

            return Decision.Accept();
        }
    }
}
