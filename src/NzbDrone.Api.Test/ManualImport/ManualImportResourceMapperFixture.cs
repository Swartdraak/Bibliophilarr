using System.Collections.Generic;
using Bibliophilarr.Api.V1.ManualImport;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Manual;

namespace NzbDrone.Api.Test.ManualImport
{
    [TestFixture]
    public class ManualImportResourceMapperFixture
    {
        [Test]
        public void should_use_monitored_edition_when_multiple_exist()
        {
            var model = new ManualImportItem
            {
                Book = new Book
                {
                    Editions = new List<Edition>
                    {
                        new Edition { ForeignEditionId = "edition-1", Monitored = false },
                        new Edition { ForeignEditionId = "edition-2", Monitored = true }
                    }
                }
            };

            var resource = model.ToResource();

            resource.ForeignEditionId.Should().Be("edition-2");
        }

        [Test]
        public void should_fallback_to_first_edition_when_none_are_monitored()
        {
            var model = new ManualImportItem
            {
                Book = new Book
                {
                    Editions = new List<Edition>
                    {
                        new Edition { ForeignEditionId = "edition-1", Monitored = false },
                        new Edition { ForeignEditionId = "edition-2", Monitored = false }
                    }
                }
            };

            var resource = model.ToResource();

            resource.ForeignEditionId.Should().Be("edition-1");
        }

        [Test]
        public void should_return_null_foreign_edition_id_when_book_has_no_editions()
        {
            var model = new ManualImportItem
            {
                Book = new Book
                {
                    Editions = new List<Edition>()
                }
            };

            var resource = model.ToResource();

            resource.ForeignEditionId.Should().BeNull();
        }
    }
}
