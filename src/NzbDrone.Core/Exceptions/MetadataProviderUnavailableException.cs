using System;
using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Exceptions
{
    /// <summary>
    /// Raised when a metadata operation could not be completed because every
    /// provider failed with a transport/network error (e.g. "No route to host",
    /// DNS failure, timeout) rather than explicitly reporting that the requested
    /// entity does not exist.
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

        public MetadataProviderUnavailableException(string operation, string entityId, Exception innerException)
            : base($"All metadata providers failed for operation '{operation}' (entity '{entityId}') due to a transport/network error. The entity was kept with its existing metadata.", innerException)
        {
            Operation = operation;
            EntityId = entityId;
        }
    }
}
