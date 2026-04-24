using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.RssSync
{
    public class MonitoredBookSpecification : IDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly IAuthorFormatProfileService _formatProfileService;
        private readonly Logger _logger;

        public MonitoredBookSpecification(IConfigService configService,
                                          IAuthorFormatProfileService formatProfileService,
                                          Logger logger)
        {
            _configService = configService;
            _formatProfileService = formatProfileService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            if (searchCriteria != null)
            {
                if (!searchCriteria.MonitoredBooksOnly)
                {
                    _logger.Debug("Skipping monitored check during search");
                    return Decision.Accept();
                }
            }

            if (!subject.Author.Monitored)
            {
                _logger.Debug("{0} is present in the DB but not tracked. Rejecting.", subject.Author);
                return Decision.Reject("Author is not monitored");
            }

            // When dual-format tracking is enabled, check per-format monitoring
            if (_configService.EnableDualFormatTracking && subject.ResolvedFormatType.HasValue)
            {
                var formatProfile = _formatProfileService.GetByAuthorIdAndFormat(
                    subject.Author.Id,
                    subject.ResolvedFormatType.Value);

                if (formatProfile != null && !formatProfile.Monitored)
                {
                    _logger.Debug(
                        "{0} format is not monitored for {1}. Rejecting.",
                        subject.ResolvedFormatType.Value,
                        subject.Author);
                    return Decision.Reject("{0} format is not monitored", subject.ResolvedFormatType.Value);
                }
            }

            var monitoredCount = subject.Books.Count(book => book.Monitored);
            if (monitoredCount == subject.Books.Count)
            {
                return Decision.Accept();
            }

            if (subject.Books.Count == 1)
            {
                _logger.Debug("Book is not monitored. Rejecting", monitoredCount, subject.Books.Count);
                return Decision.Reject("Book is not monitored");
            }

            if (monitoredCount == 0)
            {
                _logger.Debug("No books in the release are monitored. Rejecting", monitoredCount, subject.Books.Count);
            }
            else
            {
                _logger.Debug("Only {0}/{1} books in the release are monitored. Rejecting", monitoredCount, subject.Books.Count);
            }

            return Decision.Reject("Book is not monitored");
        }
    }
}
