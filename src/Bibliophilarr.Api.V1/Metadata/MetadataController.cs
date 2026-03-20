using Bibliophilarr.Http;
using NzbDrone.Core.Extras.Metadata;

namespace Bibliophilarr.Api.V1.Metadata
{
    [V1ApiController]
    public class MetadataController : ProviderControllerBase<MetadataResource, MetadataBulkResource, IMetadata, MetadataDefinition>
    {
        public static readonly MetadataResourceMapper ResourceMapper = new ();
        public static readonly MetadataBulkResourceMapper BulkResourceMapper = new ();

        public MetadataController(IMetadataFactory metadataFactory)
            : base(metadataFactory, "metadata", ResourceMapper, BulkResourceMapper)
        {
        }
    }
}
