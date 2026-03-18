using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListSyncServiceFixture : CoreTest<ImportListSyncService>
    {
        [Test]
        public void should_map_openlibrary_edition_and_add_book_and_author()
        {
            var definition = new ImportListDefinition
            {
                Id = 10,
                Name = "OpenLibrary import",
                EnableAutomaticAdd = true,
                ShouldMonitor = ImportListMonitorType.None,
                ShouldMonitorExisting = false,
                ShouldSearch = false,
                ProfileId = 1,
                MetadataProfileId = 1,
                RootFolderPath = "/books"
            };

            var report = new ImportListItemInfo
            {
                ImportListId = definition.Id,
                ImportList = definition.Name,
                Book = "Unknown Book",
                Author = "Unknown Author",
                EditionOpenLibraryId = "123"
            };

            var mappedMetadata = new AuthorMetadata
            {
                ForeignAuthorId = "OL999A",
                Name = "Mapped Author"
            };

            var mappedBook = new Book
            {
                ForeignBookId = "OL123W",
                ForeignEditionId = "123",
                Title = "Mapped Book",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(mappedMetadata)
            };

            Mocker.GetMock<IImportListFactory>()
                .Setup(x => x.Get(definition.Id))
                .Returns(definition);

            Mocker.GetMock<IFetchAndParseImportList>()
                .Setup(x => x.FetchSingleList(definition))
                .Returns(new List<ImportListItemInfo> { report });

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(x => x.All())
                .Returns(new List<ImportListExclusion>());

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.GetEditionByForeignEditionId("123"))
                .Returns((Edition)null);

            Mocker.GetMock<ISearchForNewBook>()
                .Setup(x => x.SearchByExternalId("openlibrary", "123"))
                .Returns(new List<Book> { mappedBook });

            Mocker.GetMock<IBookService>()
                .Setup(x => x.FindById("OL123W"))
                .Returns((Book)null);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.FindById("OL999A"))
                .Returns((Author)null);

            Mocker.GetMock<IAddAuthorService>()
                .Setup(x => x.AddAuthors(It.IsAny<List<Author>>(), false))
                .Returns(new List<Author>());

            Mocker.GetMock<IAddBookService>()
                .Setup(x => x.AddBooks(It.IsAny<List<Book>>(), false))
                .Returns(new List<Book>());

            Subject.Execute(new ImportListSyncCommand(definition.Id));

            Mocker.GetMock<IAddBookService>()
                .Verify(x => x.AddBooks(It.Is<List<Book>>(books =>
                    books.Count == 1 &&
                    books[0].ForeignBookId == "OL123W" &&
                    books[0].Author.Value.Metadata.Value.ForeignAuthorId == "OL999A"), false), Times.Once);
        }
    }
}
