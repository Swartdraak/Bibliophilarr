using NzbDrone.Core.Books;

namespace Bibliophilarr.Api.V1.Books
{
    public class BookFormatStatusResource
    {
        public FormatType FormatType { get; set; }
        public bool Monitored { get; set; }
        public bool HasFile { get; set; }
        public int? QualityProfileId { get; set; }
        public string QualityProfileName { get; set; }
    }
}
