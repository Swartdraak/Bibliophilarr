using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
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

        [TestCase("OL12345A", "openlibrary:author:OL12345A", true)]
        [TestCase("hardcover:author:123", "hardcover:author:123", false)]
        public void should_not_create_storage_for_a_new_unattended_author_with_an_incomplete_authoritative_bibliography(
            string providerId, string sourceId, bool endedEarly)
        {
            var failure = new MetadataProviderUnavailableException(
                MetadataProviderOrchestrator.AuthorInfoOperation,
                sourceId,
                new InvalidOperationException("Fixture returned an incomplete bibliography."),
                authoritativeBibliographyEndedEarly: endedEarly,
                authorBibliographyProgress: endedEarly ? (1, 1, 2) : null,
                authoritativeBibliographyMissingWorkTitle: !endedEarly);
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

        [TestCase(FormatType.Ebook)]
        [TestCase(FormatType.Audiobook)]
        public void should_bind_the_generated_opposite_format_before_scanning_the_new_root(FormatType firstFormat)
        {
            var secondFormat = firstFormat == FormatType.Ebook ? FormatType.Audiobook : FormatType.Ebook;
            var first = CreateTypedRoot("first", firstFormat);
            var second = CreateTypedRoot("second", secondFormat);
            var roots = ConfigureFormatDefaults();
            roots.Add(first);
            var author = AddFormatFixtureAuthor(first);
            var original = FindFormat(author.Id, secondFormat);
            original.RootFolderPath.Should().Be(first.Path);
            original.QualityProfileId.Should().Be(first.DefaultQualityProfileId);
            original.PendingTypedDefault.Should().BeTrue();
            var originalFirst = FindFormat(author.Id, firstFormat);
            var observed = false;
            Mocker.GetMock<IManageCommandQueue>()
                .Setup(queue => queue.Push(It.Is<RescanFoldersCommand>(command => command.Folders.Contains(second.Path)), CommandPriority.Normal, CommandTrigger.Unspecified))
                .Callback(() =>
                {
                    var updated = FindFormat(author.Id, secondFormat);
                    updated.Id.Should().Be(original.Id);
                    updated.RootFolderPath.Should().Be(second.Path);
                    updated.QualityProfileId.Should().Be(second.DefaultQualityProfileId);
                    updated.PendingTypedDefault.Should().BeFalse();
                    updated.Path.Should().Be(original.Path);
                    updated.Tags.Should().BeEquivalentTo(original.Tags);
                    updated.Monitored.Should().Be(original.Monitored);
                    updated.MonitorNewItems.Should().Be(original.MonitorNewItems);
                    FindFormat(author.Id, firstFormat).Should().BeEquivalentTo(originalFirst);
                    observed = true;
                });

            roots.Add(second);

            observed.Should().BeTrue();
            FindFormat(author.Id, secondFormat).RootFolderPath.Should().Be(second.Path);
        }

        [TestCase("manual")]
        [TestCase("explicit root")]
        [TestCase("explicit quality")]
        [TestCase("mixed")]
        [TestCase("Calibre")]
        public void should_keep_new_profiles_without_an_unattended_opposite_format_default(string scenario)
        {
            var first = scenario == "mixed" ? CreateRoot("first", Quality.EPUB, Quality.M4B) : CreateRoot("first", Quality.EPUB);
            first.IsCalibreLibrary = scenario == "Calibre";
            var roots = ConfigureFormatDefaults();
            roots.Add(first);
            var options = new AddAuthorOptions();
            if (scenario == "explicit root")
            {
                options.AudiobookRootFolderPath = first.Path;
            }

            if (scenario == "explicit quality")
            {
                options.AudiobookQualityProfileId = first.DefaultQualityProfileId;
            }

            var author = AddFormatFixtureAuthor(first, scenario != "manual", options);
            var original = FindFormat(author.Id, FormatType.Audiobook);
            original.PendingTypedDefault.Should().BeFalse();

            roots.Add(CreateTypedRoot("second", FormatType.Audiobook));

            FindFormat(author.Id, FormatType.Audiobook).Should().BeEquivalentTo(original);
        }

        [Test]
        public void should_keep_a_pending_default_when_two_compatible_roots_are_already_visible()
        {
            var first = CreateTypedRoot("first", FormatType.Ebook);
            var roots = ConfigureFormatDefaults();
            roots.Add(first);
            var author = AddFormatFixtureAuthor(first);
            var original = FindFormat(author.Id, FormatType.Audiobook);
            original.PendingTypedDefault.Should().BeTrue();

            Db.Insert(CreateTypedRoot("second", FormatType.Audiobook));
            roots.Add(CreateTypedRoot("third", FormatType.Audiobook));

            FindFormat(author.Id, FormatType.Audiobook).Should().BeEquivalentTo(original);
        }

        [Test]
        public void should_resolve_the_pending_default_after_a_root_arrives_during_profile_creation()
        {
            var first = CreateTypedRoot("first", FormatType.Ebook);
            var second = CreateTypedRoot("second", FormatType.Audiobook);
            var roots = ConfigureFormatDefaults();
            roots.Add(first);
            Mocker.GetMock<IQualityProfileService>().Setup(service => service.All()).Returns(() =>
            {
                // BuildFormatProfiles has already read the first root snapshot.
                roots.Add(second);
                return Db.All<QualityProfile>().ToList();
            });

            var author = AddFormatFixtureAuthor(first);
            var resolved = FindFormat(author.Id, FormatType.Audiobook);

            resolved.PendingTypedDefault.Should().BeFalse();
            resolved.RootFolderPath.Should().Be(second.Path);
            resolved.QualityProfileId.Should().Be(second.DefaultQualityProfileId);
        }

        [Test]
        public void should_no_longer_resolve_a_default_after_an_explicit_save_of_the_same_values()
        {
            var first = CreateTypedRoot("first", FormatType.Ebook);
            var roots = ConfigureFormatDefaults();
            roots.Add(first);
            var author = AddFormatFixtureAuthor(first);
            var stale = FindFormat(author.Id, FormatType.Audiobook);
            stale.PendingTypedDefault.Should().BeTrue();

            var explicitChoice = FindFormat(author.Id, FormatType.Audiobook);
            Mocker.Resolve<AuthorFormatProfileService>().Update(explicitChoice);

            var target = Db.Insert(CreateTypedRoot("second", FormatType.Audiobook));
            Mocker.Resolve<AuthorFormatProfileRepository>().TryResolvePendingDefault(stale, target).Should().BeFalse();
            var saved = FindFormat(author.Id, FormatType.Audiobook);
            saved.PendingTypedDefault.Should().BeFalse();
            saved.Should().BeEquivalentTo(explicitChoice);
        }

        private IRootFolderService ConfigureFormatDefaults()
        {
            Mocker.GetMock<IConfigService>().SetupGet(service => service.EnableDualFormatTracking).Returns(true);
            var folderRepository = Mocker.Resolve<RootFolderRepository>();
            Mocker.SetConstant<IRootFolderRepository>(folderRepository);
            Mocker.GetMock<IProfileRepository>().Setup(repository => repository.All()).Returns(() => Db.All<QualityProfile>().ToList());
            Mocker.GetMock<IQualityProfileService>().Setup(service => service.All()).Returns(() => Db.All<QualityProfile>().ToList());
            Mocker.SetConstant<IAuthorFormatProfileRepository>(Mocker.Resolve<AuthorFormatProfileRepository>());
            Mocker.SetConstant<IAuthorFormatDefaultService>(Mocker.Resolve<AuthorFormatDefaultService>());
            Mocker.GetMock<IDiskProvider>().Setup(service => service.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(service => service.FolderWritable(It.IsAny<string>())).Returns(true);
            var roots = Mocker.Resolve<RootFolderService>();
            Mocker.SetConstant<IRootFolderService>(roots);
            return roots;
        }

        private RootFolder CreateTypedRoot(string name, FormatType format)
        {
            return CreateRoot(name, format == FormatType.Ebook ? Quality.EPUB : Quality.M4B);
        }

        private RootFolder CreateRoot(string name, params Quality[] qualities)
        {
            var profile = Db.Insert(new QualityProfile
            {
                Name = name,
                Items = qualities.Select(quality => new QualityProfileQualityItem { Allowed = true, Quality = quality }).ToList()
            });
            return new RootFolder { Name = name, Path = Path.Combine(TempFolder, name), DefaultQualityProfileId = profile.Id };
        }

        private Author AddFormatFixtureAuthor(RootFolder root, bool unattended = true, AddAuthorOptions options = null)
        {
            const string source = "hardcover:author:702";
            Mocker.GetMock<IMetadataProviderOrchestrator>().Setup(service => service.GetAuthorInfo(source, false))
                .Returns(() => new Author
                {
                    Metadata = new AuthorMetadata
                    {
                        ForeignAuthorId = source,
                        Name = "Fixture Author",
                        SortName = "fixture author",
                        NameLastFirst = "Author, Fixture",
                        SortNameLastFirst = "author, fixture",
                        TitleSlug = "fixture-author"
                    }
                });
            Mocker.SetConstant<IAuthorFormatProfileService>(Mocker.Resolve<AuthorFormatProfileService>());
            var service = CreateAddAuthorService();
            var request = new Author
            {
                ForeignAuthorId = source,
                Name = "Fixture Author",
                RootFolderPath = root.Path,
                Path = Path.Combine(root.Path, "Author"),
                QualityProfileId = root.DefaultQualityProfileId,
                Tags = new HashSet<int> { 4 },
                Monitored = true,
                AddOptions = options
            };
            return unattended ? service.AddAuthorFromUnattendedLibraryImport(request) : service.AddAuthor(request, false);
        }

        private AuthorFormatProfile FindFormat(int authorId, FormatType format)
        {
            return Db.All<AuthorFormatProfile>().Single(profile => profile.AuthorId == authorId && profile.FormatType == format);
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
