using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class CandidateServiceFixture : CoreTest<CandidateService>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(s => s.ExpandAuthorAliases(It.IsAny<IEnumerable<string>>()))
                .Returns((IEnumerable<string> authors) => (authors ?? new List<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).ToList());

            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(s => s.BuildTitleVariants(It.IsAny<string>()))
                .Returns((string title) => string.IsNullOrWhiteSpace(title) ? new List<string>() : new List<string> { title });

            Mocker.SetConstant<IEnumerable<IBookSearchFallbackProvider>>(new List<IBookSearchFallbackProvider>());
            Mocker.GetMock<IBookSearchFallbackExecutionService>()
                .Setup(x => x.Search(It.IsAny<IBookSearchFallbackProvider>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((IBookSearchFallbackProvider provider, string title, string author) => provider.Search(title, author));
        }

        [Test]
        public void should_not_throw_on_openlibrary_exception()
        {
            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Throws(new OpenLibraryException("Bad search"));

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Author" },
                            BookTitle = "Book"
                        }
                    }
                }
            };

            Subject.GetRemoteCandidates(edition, null).Should().BeEmpty();
        }

        [Test]
        public void should_search_by_title_when_author_is_missing()
        {
            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(new List<Book>());

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string>(),
                            BookTitle = "Book Without Author"
                        }
                    }
                }
            };

            Subject.GetRemoteCandidates(edition, null).Should().BeEmpty();

            Mocker.GetMock<ISearchForNewBook>()
                .Verify(s => s.SearchForNewBook("Book Without Author", null, true), Times.Once());
        }

        [Test]
        public void should_search_by_author_when_title_is_missing()
        {
            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(new List<Book>());

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Fallback Author" },
                            BookTitle = null
                        }
                    }
                }
            };

            Subject.GetRemoteCandidates(edition, null).Should().BeEmpty();

            Mocker.GetMock<ISearchForNewBook>()
                .Verify(s => s.SearchForNewBook("Fallback Author", null, true), Times.Once());
        }

        [Test]
        public void should_use_normalized_title_variants_when_searching()
        {
            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(s => s.BuildTitleVariants("Spellmonger: Book 1"))
                .Returns(new List<string> { "Spellmonger: Book 1", "Spellmonger" });

            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(new List<Book>());

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Terry Mancour" },
                            BookTitle = "Spellmonger: Book 1"
                        }
                    }
                }
            };

            Subject.GetRemoteCandidates(edition, null).Should().BeEmpty();

            Mocker.GetMock<ISearchForNewBook>()
                .Verify(s => s.SearchForNewBook("Spellmonger", "Terry Mancour", true), Times.Once());
        }

        [Test]
        public void should_use_tertiary_fallback_provider_when_primary_returns_no_candidates()
        {
            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(new List<Book>());

            var fallback = new Mock<IBookSearchFallbackProvider>();
            fallback.SetupGet(x => x.ProviderName).Returns("GoogleBooks");
            fallback.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());
            fallback.Setup(x => x.Search("Fallback Title", "Fallback Author")).Returns(new List<Book>());
            fallback.Setup(x => x.Search("Fallback Title", null)).Returns(new List<Book>());
            fallback.Setup(x => x.Search(null, "Fallback Author")).Returns(new List<Book>());

            Mocker.SetConstant<IEnumerable<IBookSearchFallbackProvider>>(new[] { fallback.Object });

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Fallback Author" },
                            BookTitle = "Fallback Title"
                        }
                    }
                }
            };

            Subject.GetRemoteCandidates(edition, null).Should().BeEmpty();

            fallback.Verify(x => x.Search("Fallback Title", "Fallback Author"), Times.Once());
        }
    }
}
