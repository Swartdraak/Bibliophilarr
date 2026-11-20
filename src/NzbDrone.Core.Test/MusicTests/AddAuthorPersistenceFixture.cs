using System.IO;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
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
            Mocker.SetConstant<IAuthorRepository>(Mocker.Resolve<AuthorRepository>());
            Mocker.SetConstant<IAuthorMetadataRepository>(Mocker.Resolve<AuthorMetadataRepository>());
            Mocker.SetConstant<IAuthorService>(Mocker.Resolve<AuthorService>());
            Mocker.SetConstant<IAuthorMetadataService>(Mocker.Resolve<AuthorMetadataService>());
            Mocker.SetConstant<IAuthorCanonicalizationService>(Mocker.Resolve<AuthorCanonicalizationService>());
            Mocker.GetMock<IAddAuthorValidator>()
                .Setup(validator => validator.Validate(It.IsAny<Author>()))
                .Returns(new ValidationResult());
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

            var service = Mocker.Resolve<AddAuthorService>();
            var first = service.AddAuthor(CreateRequest("First Placeholder"), false);
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
    }
}
