using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public class AuthorFormatProfile : ModelBase
    {
        public int AuthorId { get; set; }
        public FormatType FormatType { get; set; }
        public int QualityProfileId { get; set; }
        public string RootFolderPath { get; set; }
        public HashSet<int> Tags { get; set; }
        public bool Monitored { get; set; }
        public string Path { get; set; }

        public AuthorFormatProfile()
        {
            Tags = new HashSet<int>();
            Monitored = true;
            Path = string.Empty;
        }
    }
}
