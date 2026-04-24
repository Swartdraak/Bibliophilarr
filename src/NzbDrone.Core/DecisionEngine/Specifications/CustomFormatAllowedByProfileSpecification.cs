using NzbDrone.Common.Extensions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class CustomFormatAllowedbyProfileSpecification : IDecisionEngineSpecification
    {
        private readonly UpgradableSpecification _upgradableSpecification;

        public CustomFormatAllowedbyProfileSpecification(UpgradableSpecification upgradableSpecification)
        {
            _upgradableSpecification = upgradableSpecification;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var minScore = _upgradableSpecification.ResolveProfile(subject).MinFormatScore;
            var score = subject.CustomFormatScore;

            if (score < minScore)
            {
                return Decision.Reject("Custom Formats {0} have score {1} below Author profile minimum {2}", subject.CustomFormats.ConcatToString(), score, minScore);
            }

            return Decision.Accept();
        }
    }
}
