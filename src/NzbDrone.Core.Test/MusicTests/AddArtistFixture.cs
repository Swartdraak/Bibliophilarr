using System;
using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddAuthorFixture : CoreTest<AddAuthorService>
    {
        private Author _fakeAuthor;

        [SetUp]
        public void Setup()
        {
            _fakeAuthor = Builder<Author>
                .CreateNew()
                .With(s => s.Path = null)
                .Build();
            _fakeAuthor.Books = new List<Book>();

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()))
                .Returns<Author, bool>((author, _) => author);
        }

        private void GivenValidAuthor(string bibliophilarrId)
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(s => s.GetAuthorInfo(bibliophilarrId, false))
                .Returns(_fakeAuthor);
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null))
                  .Returns<Author, NamingConfig>((c, n) => c.Name);

            Mocker.GetMock<IAddAuthorValidator>()
                  .Setup(s => s.Validate(It.IsAny<Author>()))
                  .Returns(new ValidationResult());
        }

        [Test]
        public void should_be_able_to_add_a_author_without_passing_in_name()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = @"C:\Test\Music"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            var author = Subject.AddAuthor(newAuthor);

            author.Name.Should().Be(_fakeAuthor.Name);
        }

        [Test]
        public void should_keep_the_fetched_bibliography_for_author_reuse_when_the_request_has_no_books()
        {
            const string incomingId = "hardcover:author:11";
            var requested = new Author { ForeignAuthorId = incomingId, RootFolderPath = @"C:\Test\Books" };
            var providerBooks = new List<Book> { new Book { ForeignBookId = "hardcover:work:21" } };
            _fakeAuthor.ForeignAuthorId = incomingId;
            _fakeAuthor.Books = providerBooks;
            var incumbent = new Author { Id = 30, ForeignAuthorId = "openlibrary:author:OL10A" };
            GivenValidAuthor(incomingId);
            GivenValidPath();
            Mocker.GetMock<IAuthorCanonicalizationService>()
                .Setup(service => service.TryReuseAuthorOnAdd(_fakeAuthor, providerBooks, It.IsAny<IReadOnlyList<(Author, IReadOnlyList<Book>)>>()))
                .Returns(incumbent);

            var result = Subject.AddAuthor(requested);

            result.Should().BeSameAs(incumbent);
            Mocker.GetMock<IAuthorService>().Verify(service => service.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IAuthorMetadataService>().Verify(service => service.Upsert(It.IsAny<AuthorMetadata>()), Times.Never());
        }

        [Test]
        public void should_save_the_work_providers_author_without_refetching_the_bibliography()
        {
            var requested = new Author { RootFolderPath = @"C:\Test\Books", MetadataProfileId = 3 };
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = "OL12345A",
                Name = "Provider Author",
                Overview = "Provider biography"
            };
            GivenValidPath();

            var author = Subject.AddAuthorWithMetadata(requested, metadata, false);

            author.ForeignAuthorId.Should().Be("openlibrary:author:OL12345A");
            author.Name.Should().Be("Provider Author");
            author.Metadata.Value.Overview.Should().Be("Provider biography");
            author.MetadataProfileId.Should().Be(3);
            author.Path.Should().Be(Path.Combine(requested.RootFolderPath, metadata.Name));
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(provider => provider.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IAuthorService>()
                .Verify(service => service.AddAuthor(author, false), Times.Once());
        }

        [Test]
        public void should_resolve_incomplete_author_details_by_the_verified_author_id()
        {
            var requested = new Author { RootFolderPath = @"C:\Test\Books" };
            var metadata = new AuthorMetadata { ForeignAuthorId = "OL12345A" };
            GivenValidAuthor("OL12345A");
            GivenValidPath();

            var author = Subject.AddAuthorWithMetadata(requested, metadata, false);

            author.Name.Should().Be(_fakeAuthor.Name);
            Mocker.GetMock<IMetadataProviderOrchestrator>().Verify(provider => provider.GetAuthorInfo("OL12345A", false), Times.Once());
        }

        [Test]
        public void should_have_proper_path()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = @"C:\Test\Music"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            var author = Subject.AddAuthor(newAuthor);

            author.Path.Should().Be(Path.Combine(newAuthor.RootFolderPath, _fakeAuthor.Name));
        }

        [Test]
        public void should_throw_if_author_validation_fails()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);

            Mocker.GetMock<IAddAuthorValidator>()
                  .Setup(s => s.Validate(It.IsAny<Author>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddAuthor(newAuthor));
        }

        [Test]
        public void should_throw_if_author_cannot_be_found()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "OL12345A",
                Path = @"C:\Test\Music\Name1"
            };

            // After ID normalization the orchestrator receives the bare OL token
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                  .Setup(s => s.GetAuthorInfo("OL12345A", false))
                  .Throws(new AuthorNotFoundException("OL12345A"));

            Mocker.GetMock<IAddAuthorValidator>()
                  .Setup(s => s.Validate(It.IsAny<Author>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddAuthor(newAuthor));

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_fallback_to_request_payload_when_metadata_provider_temporarily_fails()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "OL999999A",
                Path = @"C:\Test\Music\Fallback",
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = "OL999999A",
                    Name = "Fallback Author"
                }
            };

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(s => s.GetAuthorInfo(newAuthor.ForeignAuthorId, false))
                .Throws(new System.Exception("upstream unavailable"));

            Mocker.GetMock<IAddAuthorValidator>()
                .Setup(s => s.Validate(It.IsAny<Author>()))
                .Returns(new ValidationResult());

            var author = Subject.AddAuthor(newAuthor, false);

            author.Should().NotBeNull();
            author.ForeignAuthorId.Should().Be("openlibrary:author:OL999999A");
            author.Name.Should().Be("Fallback Author");
            author.Path.Should().Be(@"C:\Test\Music\Fallback");

            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void should_keep_manual_and_transport_fallback(bool unattended, bool authoritative)
        {
            var request = IncompleteFixtureAuthor();
            var provider = Mocker.GetMock<IMetadataProviderOrchestrator>();
            provider.Setup(service => service.GetAuthorInfo("OL12345A", false)).Throws(IncompleteBibliography(authoritative));
            GivenValidPath();

            var author = unattended
                ? Subject.AddAuthorFromUnattendedLibraryImport(request)
                : Subject.AddAuthor(request, false);

            author.Name.Should().Be("Incomplete Fixture");
            Mocker.GetMock<IAuthorMetadataService>().Verify(service => service.Upsert(author.Metadata.Value), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(service => service.AddAuthor(author, false), Times.Once());
            provider.Verify(service => service.GetAuthorInfo("OL12345A", false), Times.Once());
            provider.VerifyNoOtherCalls();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_allow_unattended_reuse_of_a_validated_owner_after_incomplete_bibliography()
        {
            var request = IncompleteFixtureAuthor();
            var incumbent = new Author { Id = 7, AuthorMetadataId = 8, ForeignAuthorId = "hardcover:author:42" };
            var provider = Mocker.GetMock<IMetadataProviderOrchestrator>();
            provider.Setup(service => service.GetAuthorInfo("OL12345A", false)).Throws(IncompleteBibliography(authoritative: true));
            var canonical = Mocker.GetMock<IAuthorCanonicalizationService>();
            canonical.Setup(service => service.TryReuseAuthorOnAdd(
                    It.Is<Author>(author => author.ForeignAuthorId == request.ForeignAuthorId && author.Path == request.Path),
                    null,
                    It.Is<IReadOnlyList<(Author, IReadOnlyList<Book>)>>(pending => pending.Count == 0)))
                .Returns(incumbent);
            GivenValidPath();
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableDualFormatTracking).Returns(true);

            Subject.AddAuthorFromUnattendedLibraryImport(request).Should().BeSameAs(incumbent);

            Mocker.GetMock<IAddAuthorValidator>().Verify(validator => validator.Validate(It.IsAny<Author>()), Times.Once());
            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorService>().Verify(service => service.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IAuthorFormatProfileService>().VerifyNoOtherCalls();
            provider.Verify(service => service.GetAuthorInfo("OL12345A", false), Times.Once());
            provider.VerifyNoOtherCalls();
        }

        private static Author IncompleteFixtureAuthor()
        {
            return new Author { Name = "Incomplete Fixture", ForeignAuthorId = "openlibrary:author:OL12345A", Path = @"C:\Test\Books\Incomplete Fixture" };
        }

        private static MetadataProviderUnavailableException IncompleteBibliography(bool authoritative)
        {
            return new MetadataProviderUnavailableException(
                MetadataProviderOrchestrator.AuthorInfoOperation,
                "openlibrary:author:OL12345A",
                new InvalidOperationException("Fixture author response unavailable."),
                authoritative,
                authoritative ? (1, 1, 2) : null);
        }

        [Test]
        public void should_disambiguate_if_author_folder_exists()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1",
            };

            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew().With(x => x.Disambiguation = "Disambiguation").Build();

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path))
                .Returns(true);

            var author = Subject.AddAuthor(newAuthor);
            author.Path.Should().Be(newAuthor.Path + " (Disambiguation)");
        }

        [Test]
        public void should_disambiguate_with_numbers_if_author_folder_still_exists()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1",
            };

            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew().With(x => x.Disambiguation = "Disambiguation").Build();

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (Disambiguation)"))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (Disambiguation) (1)"))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (Disambiguation) (2)"))
                .Returns(true);

            var author = Subject.AddAuthor(newAuthor);
            author.Path.Should().Be(newAuthor.Path + " (Disambiguation) (3)");
        }

        [Test]
        public void should_disambiguate_with_numbers_if_author_folder_exists_and_no_disambiguation()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1",
            };

            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew().With(x => x.Disambiguation = string.Empty).Build();

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (1)"))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (2)"))
                .Returns(true);

            var author = Subject.AddAuthor(newAuthor);
            author.Path.Should().Be(newAuthor.Path + " (3)");
        }
    }
}
