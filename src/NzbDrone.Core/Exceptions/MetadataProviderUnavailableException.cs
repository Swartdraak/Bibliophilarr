using System;
using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Exceptions
{
    /// <summary>
    /// Raised when a metadata operation could not be completed due to a transport failure
    /// or an incomplete provider response. Neither means the entity no longer exists.
    ///
    /// This is deliberately distinct from <see cref="AuthorNotFoundException"/> /
    /// <see cref="BookNotFoundException"/>: a transient outage must never be
    /// interpreted as "the provider removed this entity", because doing so leads
    /// to destructive deletion of local data (see issue #204 / #209).
    /// </summary>
    public class MetadataProviderUnavailableException : NzbDroneException
    {
        public string Operation { get; }
        public string EntityId { get; }
        internal bool AuthoritativeBibliographyEndedEarly { get; }
        internal bool AuthoritativeBibliographyMissingWorkTitle { get; }
        internal bool AuthoritativeBibliographyIncomplete => AuthoritativeBibliographyEndedEarly || AuthoritativeBibliographyMissingWorkTitle;
        internal (int PageOffset, int DistinctEntries, int ReportedTotal)? AuthorBibliographyProgress { get; }

        public MetadataProviderUnavailableException(string operation, string entityId, Exception innerException)
            : this(operation, entityId, innerException, false)
        {
        }

        internal MetadataProviderUnavailableException(string operation,
                                                      string entityId,
                                                      Exception innerException,
                                                      bool authoritativeBibliographyEndedEarly,
                                                      (int PageOffset, int DistinctEntries, int ReportedTotal)? authorBibliographyProgress = null,
                                                      bool authoritativeBibliographyMissingWorkTitle = false)
            : base($"All metadata providers failed for operation '{operation}' (entity '{entityId}') before a complete response was available. The entity was kept with its existing metadata.", innerException)
        {
            Operation = operation;
            EntityId = entityId;
            AuthoritativeBibliographyEndedEarly = authoritativeBibliographyEndedEarly;
            AuthoritativeBibliographyMissingWorkTitle = authoritativeBibliographyMissingWorkTitle;
            AuthorBibliographyProgress = authorBibliographyProgress;
        }
    }
}
