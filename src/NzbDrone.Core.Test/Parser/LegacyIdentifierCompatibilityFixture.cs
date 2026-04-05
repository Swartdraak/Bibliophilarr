using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.ParserCompatibility
{
    [TestFixture]
    public class LegacyIdentifierCompatibilityFixture
    {
        [Test]
        public void import_list_item_info_should_deserialize_legacy_goodreads_keys_into_openlibrary_fields()
        {
            const string payload = "{\"Author\":\"Author Name\",\"Book\":\"Book Name\",\"AuthorGoodreadsId\":\"OL123A\",\"BookGoodreadsId\":\"OL456W\",\"EditionGoodreadsId\":\"OL789M\"}";

            var item = JsonConvert.DeserializeObject<ImportListItemInfo>(payload);

            item.AuthorOpenLibraryId.Should().Be("OL123A");
            item.BookOpenLibraryId.Should().Be("OL456W");
            item.EditionOpenLibraryId.Should().Be("OL789M");
        }

        [Test]
        public void parsed_track_info_should_deserialize_legacy_goodreads_key_into_openlibrary_id()
        {
            const string payload = "{\"Title\":\"Track\",\"GoodreadsId\":\"OL999M\"}";

            var info = JsonConvert.DeserializeObject<ParsedTrackInfo>(payload);

            info.OpenLibraryId.Should().Be("OL999M");
        }
    }
}
