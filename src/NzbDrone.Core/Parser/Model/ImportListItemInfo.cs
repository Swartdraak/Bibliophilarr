using System;
using Newtonsoft.Json;

namespace NzbDrone.Core.Parser.Model
{
    public class ImportListItemInfo
    {
        public int ImportListId { get; set; }
        public string ImportList { get; set; }
        public string Author { get; set; }
        public string AuthorOpenLibraryId { get; set; }
        public string Book { get; set; }
        public string BookOpenLibraryId { get; set; }
        public string EditionOpenLibraryId { get; set; }
        public DateTime ReleaseDate { get; set; }

        [JsonProperty("AuthorGoodreadsId")]
        public string LegacyAuthorGoodreadsId
        {
            set => AuthorOpenLibraryId ??= value;
        }

        [JsonProperty("BookGoodreadsId")]
        public string LegacyBookGoodreadsId
        {
            set => BookOpenLibraryId ??= value;
        }

        [JsonProperty("EditionGoodreadsId")]
        public string LegacyEditionGoodreadsId
        {
            set => EditionOpenLibraryId ??= value;
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1} [{2}]", ReleaseDate, Author, Book);
        }
    }
}
