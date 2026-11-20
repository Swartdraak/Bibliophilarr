using System;

namespace NzbDrone.Core.Datastore
{
    internal enum CatalogueIdentityConflictKind
    {
        PrimarySource,
        CorroboratedSource,
        CorroboratedRoute
    }

    internal sealed class CatalogueIdentityConflictException : InvalidOperationException
    {
        public CatalogueIdentityConflictException(CatalogueIdentityConflictKind kind, string message)
            : base(message)
        {
            Kind = kind;
        }

        public CatalogueIdentityConflictKind Kind { get; }
    }
}
