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

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchByExternalId("openlibrary", "123"))
                .Returns(new List<Book> { mappedBook });

            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(x => x.BuildTitleVariants(It.IsAny<string>()))
                .Returns((string title) => new List<string> { title });

            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(x => x.ExpandAuthorAliases(It.IsAny<IEnumerable<string>>()))
                .Returns((IEnumerable<string> names) => new List<string>(names));

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

        [Test]
        public void should_map_non_numeric_openlibrary_edition_token_and_preserve_monitored_edition()
        {
            var definition = new ImportListDefinition
            {
                Id = 12,
                Name = "OpenLibrary token import",
                EnableAutomaticAdd = true,
                ShouldMonitor = ImportListMonitorType.SpecificBook,
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
                Author = "J. R. R. Tolkien",
                Book = "The Lord of the Rings",
                EditionOpenLibraryId = "OL7353617M"
            };

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = "openlibrary:author:OL26320A",
                Name = "J. R. R. Tolkien"
            };

            var mappedBook = new Book
            {
                ForeignBookId = "openlibrary:work:OL45883W",
                ForeignEditionId = "openlibrary:edition:OL7353617M",
                Title = "The Lord of the Rings",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(metadata)
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
                .Setup(x => x.GetEditionByForeignEditionId("OL7353617M"))
                .Returns((Edition)null);

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchByExternalId("openlibrary", "OL7353617M"))
                .Returns(new List<Book> { mappedBook });

            Mocker.GetMock<IBookService>()
                .Setup(x => x.FindById("openlibrary:work:OL45883W"))
                .Returns((Book)null);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.FindById("openlibrary:author:OL26320A"))
                .Returns((Author)null);

            Mocker.GetMock<IAddAuthorService>()
                .Setup(x => x.AddAuthors(It.IsAny<List<Author>>(), false))
                .Returns(new List<Author>());

            Mocker.GetMock<IAddBookService>()
                .Setup(x => x.AddBooks(It.IsAny<List<Book>>(), false))
                .Returns(new List<Book>());

            Subject.Execute(new ImportListSyncCommand(definition.Id));

            Mocker.GetMock<IAddBookService>().Verify(x => x.AddBooks(It.Is<List<Book>>(books =>
                    books.Count == 1 &&
                    books[0].Editions.Value.Count == 1 &&
                    books[0].Editions.Value[0].ForeignEditionId == "OL7353617M" &&
                    books[0].Editions.Value[0].Monitored), false), Times.Once);
        }

        [Test]
        public void should_use_normalized_query_variants_when_mapping_book_without_ids()
        {
            var definition = new ImportListDefinition
            {
                Id = 11,
                Name = "Normalized import",
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
                Book = "Spellmonger: Book 1",
                Author = "Terry Mancour"
            };

            var mappedMetadata = new AuthorMetadata
            {
                ForeignAuthorId = "OL123A",
                Name = "Terry Mancour"
            };

            var mappedBook = new Book
            {
                ForeignBookId = "OL456W",
                ForeignEditionId = "OL456M",
                Title = "Spellmonger",
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

            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(x => x.BuildTitleVariants("Spellmonger: Book 1"))
                .Returns(new List<string> { "Spellmonger: Book 1", "Spellmonger" });

            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(x => x.ExpandAuthorAliases(It.IsAny<IEnumerable<string>>()))
                .Returns(new List<string> { "Terry Mancour" });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchForNewBook("Spellmonger: Book 1", "Terry Mancour", false))
                .Returns(new List<Book>());

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchForNewBook("Spellmonger", "Terry Mancour", false))
                .Returns(new List<Book> { mappedBook });

            Mocker.GetMock<IBookService>()
                .Setup(x => x.FindById("OL456W"))
                .Returns((Book)null);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.FindById("OL123A"))
                .Returns((Author)null);

            Mocker.GetMock<IAddAuthorService>()
                .Setup(x => x.AddAuthors(It.IsAny<List<Author>>(), false))
                .Returns(new List<Author>());

            Mocker.GetMock<IAddBookService>()
                .Setup(x => x.AddBooks(It.IsAny<List<Book>>(), false))
                .Returns(new List<Book>());

            Subject.Execute(new ImportListSyncCommand(definition.Id));

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.SearchForNewBook("Spellmonger", "Terry Mancour", false), Times.Once);
        }
    }
}
