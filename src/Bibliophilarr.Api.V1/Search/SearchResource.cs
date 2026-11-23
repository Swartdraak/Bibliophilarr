using System.Collections.Generic;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Http.REST;
using NzbDrone.Core.MetadataSource;

namespace Bibliophilarr.Api.V1.Search
{
    public class SearchResource : RestResource
    {
        public string ForeignId { get; set; }
        public int? CatalogueOwnerId { get; set; }
        public AuthorResource Author { get; set; }
        public BookResource Book { get; set; }
        public SeriesSearchSummary Series { get; set; }
        public List<SeriesSearchCoverResource> SeriesCovers { get; set; }
    }

    public sealed class SeriesSearchCoverResource
    {
        public string BookTitle { get; set; }
        public string Url { get; set; }
    }
}
