using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http.REST;
using NzbDrone.Core.Books;

namespace Bibliophilarr.Api.V1.Author
{
    public class AuthorFormatProfileResource : RestResource
    {
        public int AuthorId { get; set; }
        public FormatType FormatType { get; set; }
        public int QualityProfileId { get; set; }
        public string RootFolderPath { get; set; }
        public HashSet<int> Tags { get; set; }
        public bool Monitored { get; set; }
        public NewItemMonitorTypes MonitorNewItems { get; set; }
    }

    public static class AuthorFormatProfileResourceMapper
    {
        public static AuthorFormatProfileResource ToResource(this AuthorFormatProfile model)
        {
            if (model == null)
            {
                return null;
            }

            return new AuthorFormatProfileResource
            {
                Id = model.Id,
                AuthorId = model.AuthorId,
                FormatType = model.FormatType,
                QualityProfileId = model.QualityProfileId,
                RootFolderPath = model.RootFolderPath,
                Tags = model.Tags,
                Monitored = model.Monitored,
                MonitorNewItems = model.MonitorNewItems
            };
        }

        public static AuthorFormatProfile ToModel(this AuthorFormatProfileResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            return new AuthorFormatProfile
            {
                Id = resource.Id,
                AuthorId = resource.AuthorId,
                FormatType = resource.FormatType,
                QualityProfileId = resource.QualityProfileId,
                RootFolderPath = resource.RootFolderPath,
                Tags = resource.Tags ?? new HashSet<int>(),
                Monitored = resource.Monitored,
                MonitorNewItems = resource.MonitorNewItems
            };
        }

        public static List<AuthorFormatProfileResource> ToResource(this IEnumerable<AuthorFormatProfile> models)
        {
            return models?.Select(ToResource).ToList();
        }
    }
}
