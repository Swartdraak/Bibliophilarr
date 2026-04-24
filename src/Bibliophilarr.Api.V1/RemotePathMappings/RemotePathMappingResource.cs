using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http.REST;
using NzbDrone.Core.Books;
using NzbDrone.Core.RemotePathMappings;

namespace Bibliophilarr.Api.V1.RemotePathMappings
{
    public class RemotePathMappingResource : RestResource
    {
        public string Host { get; set; }
        public string RemotePath { get; set; }
        public string LocalPath { get; set; }
        public int? FormatType { get; set; }
    }

    public static class RemotePathMappingResourceMapper
    {
        public static RemotePathMappingResource ToResource(this RemotePathMapping model)
        {
            if (model == null)
            {
                return null;
            }

            return new RemotePathMappingResource
            {
                Id = model.Id,

                Host = model.Host,
                RemotePath = model.RemotePath,
                LocalPath = model.LocalPath,
                FormatType = model.FormatType.HasValue ? (int)model.FormatType.Value : null
            };
        }

        public static RemotePathMapping ToModel(this RemotePathMappingResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            return new RemotePathMapping
            {
                Id = resource.Id,

                Host = resource.Host,
                RemotePath = resource.RemotePath,
                LocalPath = resource.LocalPath,
                FormatType = resource.FormatType.HasValue ? (FormatType)resource.FormatType.Value : null
            };
        }

        public static List<RemotePathMappingResource> ToResource(this IEnumerable<RemotePathMapping> models)
        {
            return models.Select(ToResource).ToList();
        }
    }
}
