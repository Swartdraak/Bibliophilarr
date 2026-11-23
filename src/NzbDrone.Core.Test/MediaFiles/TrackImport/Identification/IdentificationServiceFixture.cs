using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class IdentificationServiceFixture : CoreTest<IdentificationService>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.IdentificationWorkerCount).Returns(1);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.EnableDualFormatTracking).Returns(true);
            Mocker.GetMock<IMediaFileService>().Setup(x => x.GetFileWithPath(It.IsAny<List<string>>())).Returns(new List<BookFile>());
            Mocker.GetMock<IEditionService>().Setup(x => x.GetCandidates(It.IsAny<int>(), It.IsAny<string>())).Returns(new List<Edition>());

            // Only the catalogue and provider boundaries are synthetic. Grouping,
            // candidate selection, filename augmentation and distance are real.
            var provider = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            provider.Setup(x => x.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new List<Book>());
            provider.Setup(x => x.SearchByIsbn(It.IsAny<string>())).Returns(new List<Book>());
            Mocker.SetConstant(provider.Object);
            Mocker.SetConstant<IEnumerable<IBookSearchFallbackProvider>>(new List<IBookSearchFallbackProvider>());
            Mocker.SetConstant<IMetadataQueryNormalizationService>(Mocker.Resolve<MetadataQueryNormalizationService>());

            Mocker.SetConstant<ITrackGroupingService>(Mocker.Resolve<TrackGroupingService>());
            Mocker.SetConstant<ICandidateService>(Mocker.Resolve<CandidateService>());
            Mocker.SetConstant<IEnumerable<IAggregate<LocalEdition>>>(new[] { Mocker.Resolve<AggregateFilenameInfo>() });
            Mocker.SetConstant<IAugmentingService>(Mocker.Resolve<AugmentingService>());
        }

        [Test]
        public void should_match_each_same_title_file_to_the_correct_author_and_edition()
        {
            var firstEdition = new Edition { Id = 101, ForeignEditionId = "hardcover:edition:101", Format = "Audiobook" };
            var secondEdition = new Edition { Id = 202, ForeignEditionId = "hardcover:edition:202", Format = "Audiobook" };
            var firstWork = GivenBook(1, "Rowan Vale", "The Distant Light", firstEdition);
            var secondWork = GivenBook(2, "Miriam North", "The Distant Light", secondEdition);
            var firstPart = TaggedFile("/library/Rowan Vale - The Distant Light - Part 01.m4b", firstWork, Quality.M4B, 1);
            var secondPart = TaggedFile("/library/Rowan Vale - The Distant Light - Part 02.m4b", firstWork, Quality.M4B, 2);
            var thirdPart = TaggedFile("/library/Miriam North - The Distant Light - Part 01.m4b", secondWork, Quality.M4B, 1);
            var fourthPart = TaggedFile("/library/Miriam North - The Distant Light - Part 02.m4b", secondWork, Quality.M4B, 2);

            var results = Identify(firstPart, secondPart, thirdPart, fourthPart);

            AssertAssignment(results, firstWork, firstEdition, firstPart, secondPart);
            AssertAssignment(results, secondWork, secondEdition, thirdPart, fourthPart);
        }

        [Test]
        public void should_match_exact_ebook_isbn_and_keep_the_audiobook_edition_separate()
        {
            var otherEbook = new Edition { Id = 101, ForeignEditionId = "hardcover:edition:101", Format = "EPUB", IsEbook = true, Isbn13 = "9780140328721" };
            var audiobook = new Edition { Id = 102, ForeignEditionId = "hardcover:edition:102", Format = "Audiobook" };
            var ebook = new Edition { Id = 103, ForeignEditionId = "hardcover:edition:103", Format = "EPUB", IsEbook = true, Isbn13 = "9780306406157" };
            var book = GivenBook(1, "Rowan Vale", "The Distant Light", otherEbook, audiobook, ebook);
            var ebookFile = TaggedFile("/library/Rowan Vale - The Distant Light.epub", book, Quality.EPUB);
            ebookFile.FileTrackInfo.Isbn = ebook.Isbn13;
            ebookFile.FileTrackInfo.IsbnConfidence = 1;
            var audioFile = TaggedFile("/library/Rowan Vale - The Distant Light.m4b", book, Quality.M4B);

            var results = Identify(ebookFile, audioFile);

            AssertAssignment(results, book, ebook, ebookFile);
            AssertAssignment(results, book, audiobook, audioFile);
        }

        [Test]
        public void should_leave_an_audiobook_unassigned_when_only_an_ebook_edition_exists()
        {
            var ebook = new Edition { Id = 101, ForeignEditionId = "hardcover:edition:101", Format = "EPUB", IsEbook = true };
            var book = GivenBook(1, "Rowan Vale", "The Distant Light", ebook);
            var audioFile = TaggedFile("/library/Rowan Vale - The Distant Light.m4b", book, Quality.M4B);

            var result = Identify(audioFile).Should().ContainSingle().Which;

            result.LocalBooks.Should().ContainSingle().Which.Should().BeSameAs(audioFile);
            result.Edition.Should().BeNull();
            audioFile.Edition.Should().BeNull();
            audioFile.Book.Should().BeNull();
            audioFile.Author.Should().BeNull();
        }

        private Book GivenBook(int id, string authorName, string title, params Edition[] editions)
        {
            var metadata = new AuthorMetadata { Id = id, Name = authorName, ForeignAuthorId = $"hardcover:author:{id}" };
            var author = new Author { Id = id, AuthorMetadataId = metadata.Id, Metadata = metadata };
            var book = new Book
            {
                Id = id,
                Title = title,
                ForeignBookId = $"hardcover:work:{id}",
                AuthorMetadataId = metadata.Id,
                AuthorMetadata = metadata,
                Author = author,
                Editions = editions.ToList()
            };

            foreach (var edition in editions)
            {
                edition.BookId = book.Id;
                edition.Book = book;
                edition.Title = title;
                edition.Monitored = true;
                edition.BookFiles = new List<BookFile>();
            }

            Mocker.GetMock<IAuthorService>().Setup(x => x.GetCandidates(authorName)).Returns(new List<Author> { author });
            Mocker.GetMock<IBookService>().Setup(x => x.GetCandidates(metadata.Id, title)).Returns(new List<Book> { book });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(book.Id)).Returns(editions.ToList());

            return book;
        }

        private static LocalBook TaggedFile(string path, Book book, Quality quality, int part = 1)
        {
            return new LocalBook
            {
                Path = path,
                Part = part,
                Quality = new QualityModel(quality),
                FileTrackInfo = new ParsedTrackInfo
                {
                    BookTitle = book.Title,
                    TrackNumbers = new[] { part },
                    Authors = new List<string> { book.AuthorMetadata.Value.Name },
                    BookTitleConfidence = 1,
                    AuthorConfidence = 1
                }
            };
        }

        private List<LocalEdition> Identify(params LocalBook[] files)
        {
            return Subject.Identify(files.ToList(), new IdentificationOverrides(), new ImportDecisionMakerConfig
            {
                KeepAllEditions = true
            });
        }

        private static void AssertAssignment(List<LocalEdition> results, Book book, Edition edition, params LocalBook[] files)
        {
            var group = results.Single(result => result.LocalBooks.Contains(files[0]));
            group.LocalBooks.Select(file => file.Path).Should().BeEquivalentTo(files.Select(file => file.Path));
            group.Edition.Should().BeSameAs(edition);

            foreach (var file in files)
            {
                group.LocalBooks.Should().Contain(file);
                file.Edition.Should().BeSameAs(edition);
                file.Book.Should().BeSameAs(book);
                file.Author.Should().BeSameAs(book.Author.Value);
            }
        }
    }
}
