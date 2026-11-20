using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
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
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.IsbnContextFallbackLimit)
                .Returns(3);

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

        [TestCase("Ebook", "River Chronicles: Book 7", true, true)]
        [TestCase("Audiobook", "River Chronicles: Book 3", true, true)]
        [TestCase("Audiobook", "River Chronicles: Book 7", true, false)]
        [TestCase("Audiobook", "River Chronicles: Book 3", false, false)]
        public void should_check_the_next_provider_for_an_eligible_author_and_title_match_in_library_scans(
            string firstFormat, string firstTitle, bool forLibraryScan, bool expectSecond)
        {
            const string title = "River Chronicles: Book 7";
            const string author = "Casey Rivers";
            var first = CreateSearchProvider("Hardcover", "hardcover:edition:1", firstTitle, firstFormat, out var firstSearch);
            var second = CreateSearchProvider("OpenLibrary", "openlibrary:edition:OL2M", title, "Audiobook", out var secondSearch);
            var third = CreateSearchProvider("GoogleBooks", "googlebooks:edition:3", title, "Audiobook", out var thirdSearch);
            var registry = new Mock<IMetadataProviderRegistry>();
            registry.Setup(r => r.GetProviders()).Returns(new[] { first, second, third });
            Mocker.SetConstant<IMetadataProviderOrchestrator>(new MetadataProviderOrchestrator(
                registry.Object, new MetadataProviderTelemetryService(), LogManager.GetCurrentClassLogger()));
            Mocker.GetMock<IConfigService>().SetupGet(config => config.RemoteCandidateSearchWorkerCount).Returns(1);

            var local = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        Path = "/library/Casey Rivers/River Chronicles 7.m4b",
                        FileTrackInfo = new ParsedTrackInfo { Authors = new List<string> { author }, BookTitle = title }
                    }
                }
            };

            var candidates = Subject.GetRemoteCandidates(local, null, edition => edition.Title == title, forLibraryScan).ToList();

            if (expectSecond || firstTitle == title)
            {
                candidates.Should().ContainSingle().Which.Edition.ForeignEditionId.Should().Be(
                    expectSecond ? "openlibrary:edition:OL2M" : "hardcover:edition:1");
            }
            else
            {
                candidates.Should().BeEmpty();
            }

            firstSearch.Verify(search => search.SearchForNewBook(title, author, true), Times.Once());
            secondSearch.Verify(search => search.SearchForNewBook(title, author, true), expectSecond ? Times.Once() : Times.Never());
            thirdSearch.Verify(search => search.SearchForNewBook(title, author, true), Times.Never());

            IMetadataProvider CreateSearchProvider(string name, string editionId, string resultTitle, string format, out Mock<ISearchForNewBook> search)
            {
                var provider = new Mock<IMetadataProvider>();
                provider.SetupGet(item => item.ProviderName).Returns(name);
                provider.SetupGet(item => item.SupportsBookSearch).Returns(true);
                search = provider.As<ISearchForNewBook>();
                search.Setup(item => item.SearchForNewBook(title, author, true)).Returns(new List<Book>
                {
                    new Book
                    {
                        Title = resultTitle,
                        Editions = new List<Edition> { new Edition { ForeignEditionId = editionId, Title = resultTitle, Format = format } }
                    }
                });
                return provider.Object;
            }
        }

        [Test]
        public void should_not_throw_on_openlibrary_exception()
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
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
            Mocker.GetMock<IMetadataProviderOrchestrator>()
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

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(s => s.SearchForNewBook("Book Without Author", null, true), Times.Once());
        }

        [Test]
        public void should_search_by_author_when_title_is_missing()
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
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

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(s => s.SearchForNewBook("Fallback Author", null, true), Times.Once());
        }

        [Test]
        public void should_use_normalized_title_variants_when_searching()
        {
            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(s => s.BuildTitleVariants("Spellmonger: Book 1"))
                .Returns(new List<string> { "Spellmonger: Book 1", "Spellmonger" });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
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

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(s => s.SearchForNewBook("Spellmonger", "Terry Mancour", true), Times.Once());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_try_swapped_author_title_search_when_primary_search_has_no_compatible_edition(bool incompatibleFirstResult)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = "openlibrary:author:OL1A",
                Name = "Frank Herbert"
            };

            var matchedBook = new Book
            {
                ForeignBookId = "openlibrary:work:OL1W",
                Title = "Dune",
                AuthorMetadata = metadata,
                Author = new Author
                {
                    Metadata = metadata,
                    AuthorMetadataId = metadata.Id
                }
            };

            matchedBook.Editions = new List<Edition>
            {
                new Edition
                {
                    ForeignEditionId = "openlibrary:edition:OL1M",
                    Title = "Dune",
                    Book = matchedBook
                }
            };

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns((string title, string author, bool _) =>
                {
                    if (title == "Frank Herbert" && author == "Dune")
                    {
                        var incompatibleBook = new Book
                        {
                            ForeignBookId = "openlibrary:work:OL2W",
                            Editions = new List<Edition>
                            {
                                new Edition
                                {
                                    ForeignEditionId = "openlibrary:edition:OL2M",
                                    Title = "Frank Herbert",
                                    Format = ".EPUB"
                                }
                            }
                        };

                        return incompatibleFirstResult ? new List<Book> { incompatibleBook } : new List<Book>();
                    }

                    if (title == "Dune" && author == "Frank Herbert")
                    {
                        return new List<Book> { matchedBook };
                    }

                    return new List<Book>();
                });

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        Path = "/media/Dune.m4b",
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Dune" },
                            BookTitle = "Frank Herbert"
                        }
                    }
                }
            };

            var candidates = Subject.GetRemoteCandidates(edition, null).ToList();

            candidates.Should().ContainSingle().Which.Edition.Should().BeSameAs(matchedBook.Editions.Value.Single());
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.SearchForNewBook("Frank Herbert", "Dune", true), Times.Once());
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.SearchForNewBook("Dune", "Frank Herbert", true), Times.Once());
        }

        [Test]
        public void should_use_tertiary_fallback_provider_when_primary_returns_no_candidates()
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
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

        [Test]
        public void should_try_limited_title_author_fallback_when_isbn_lookup_misses()
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchByIsbn("9780261103573"))
                .Returns(new List<Book>());

            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(s => s.BuildTitleVariants("The Lord of the Rings"))
                .Returns(new List<string> { "The Lord of the Rings" });

            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(s => s.ExpandAuthorAliases(It.IsAny<IEnumerable<string>>()))
                .Returns(new List<string> { "J.R.R. Tolkien" });

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = "openlibrary:author:OL26320A",
                Name = "J.R.R. Tolkien"
            };

            var matchedBook = new Book
            {
                ForeignBookId = "openlibrary:work:OL27448W",
                Title = "The Lord of the Rings",
                AuthorMetadata = metadata,
                Author = new Author
                {
                    Metadata = metadata,
                    AuthorMetadataId = metadata.Id
                }
            };

            var matchedEdition = new Edition
            {
                ForeignEditionId = "openlibrary:edition:OL7353617M",
                Title = "The Lord of the Rings",
                Book = matchedBook
            };

            matchedBook.Editions = new List<Edition> { matchedEdition };

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchForNewBook("The Lord of the Rings", "J.R.R. Tolkien", true))
                .Returns(new List<Book> { matchedBook });

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Isbn = "9780261103573",
                            Authors = new List<string> { "J.R.R. Tolkien" },
                            BookTitle = "The Lord of the Rings"
                        }
                    }
                }
            };

            var candidates = Subject.GetRemoteCandidates(edition, null).ToList();

            candidates.Should().NotBeEmpty();

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.SearchForNewBook("The Lord of the Rings", "J.R.R. Tolkien", true), Times.Once());
        }
    }
}
