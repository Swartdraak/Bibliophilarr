using System;
using System.IO;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddAuthorPersistenceFixture : DbTest
    {
        [Test]
        public void should_persist_the_provider_name_and_reuse_the_author_on_a_second_add()
        {
            const string providerId = "OL12345A";
            const string canonicalId = "openlibrary:author:" + providerId;
            const string canonicalName = "Morgan Example";
            var service = CreateAddAuthorService();
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(provider => provider.GetAuthorInfo(providerId, false))
                .Returns(() => new Author
                {
                    Metadata = new AuthorMetadata
                    {
                        ForeignAuthorId = canonicalId,
                        Name = canonicalName,
                        SortName = "morgan example",
                        NameLastFirst = "Example, Morgan",
                        SortNameLastFirst = "example, morgan",
                        TitleSlug = "morgan-example"
                    }
                });

            var first = service.AddAuthorFromUnattendedLibraryImport(CreateRequest("First Placeholder"));
            var insertedId = first.Id;
            var insertedMetadataId = first.AuthorMetadataId;
            insertedId.Should().BePositive();
            first.Name.Should().Be(canonicalName);
            first.ForeignAuthorId.Should().Be(canonicalId);

            var repeated = service.AddAuthor(CreateRequest("Second Placeholder"), false);

            repeated.Id.Should().Be(insertedId);
            repeated.Name.Should().Be(canonicalName);
            repeated.ForeignAuthorId.Should().Be(canonicalId);
            var storedAuthor = Db.All<Author>().Should().ContainSingle().Which;
            storedAuthor.Id.Should().Be(insertedId);
            storedAuthor.AuthorMetadataId.Should().Be(insertedMetadataId);
            var storedMetadata = Db.All<AuthorMetadata>().Should().ContainSingle().Which;
            storedMetadata.Id.Should().Be(insertedMetadataId);
            storedMetadata.Name.Should().Be(canonicalName);
            storedMetadata.ForeignAuthorId.Should().Be(canonicalId);

            Author CreateRequest(string name) => new () { Name = name, ForeignAuthorId = canonicalId, Path = Path.Combine(TempFolder, name) };
        }

        [Test]
        public void should_not_create_storage_for_a_new_unattended_author_with_an_incomplete_authoritative_bibliography()
        {
            const string providerId = "OL12345A";
            const string sourceId = "openlibrary:author:" + providerId;
            var failure = new MetadataProviderUnavailableException(
                MetadataProviderOrchestrator.AuthorInfoOperation,
                sourceId,
                new InvalidOperationException("Fixture returned an empty page before its declared total."),
                authoritativeBibliographyEndedEarly: true,
                authorBibliographyProgress: (1, 1, 2));
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableDualFormatTracking).Returns(true);
            var provider = Mocker.GetMock<IMetadataProviderOrchestrator>();
            provider.Setup(service => service.GetAuthorInfo(providerId, false)).Throws(failure);
            var addition = CreateAddAuthorService();
            var requested = new Author { Name = "Incomplete Fixture", ForeignAuthorId = sourceId, Path = Path.Combine(TempFolder, "author") };

            Assert.Throws<MetadataProviderUnavailableException>(() => addition.AddAuthorFromUnattendedLibraryImport(requested))
                .Should().BeSameAs(failure);

            Db.All<AuthorMetadata>().Should().BeEmpty();
            Db.All<Author>().Should().BeEmpty();
            Db.All<AuthorFormatProfile>().Should().BeEmpty();
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<AuthorAddedEvent>()), Times.Never());
            provider.Verify(service => service.GetAuthorInfo(providerId, false), Times.Once());
            provider.VerifyNoOtherCalls();
        }

        private AddAuthorService CreateAddAuthorService()
        {
            Mocker.SetConstant<IAuthorRepository>(Mocker.Resolve<AuthorRepository>());
            Mocker.SetConstant<IAuthorMetadataRepository>(Mocker.Resolve<AuthorMetadataRepository>());
            Mocker.SetConstant<IAuthorService>(Mocker.Resolve<AuthorService>());
            Mocker.SetConstant<IAuthorMetadataService>(Mocker.Resolve<AuthorMetadataService>());
            Mocker.SetConstant<IAuthorCanonicalizationService>(Mocker.Resolve<AuthorCanonicalizationService>());
            Mocker.GetMock<IAddAuthorValidator>()
                .Setup(validator => validator.Validate(It.IsAny<Author>()))
                .Returns(new ValidationResult());
            return Mocker.Resolve<AddAuthorService>();
        }
    }
}
