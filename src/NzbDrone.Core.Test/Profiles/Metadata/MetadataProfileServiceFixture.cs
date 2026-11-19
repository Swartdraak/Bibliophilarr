using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles.Metadata
{
    [TestFixture]

    public class MetadataProfileServiceFixture : CoreTest<MetadataProfileService>
    {
        [Test]
        public void complete_bibliography_should_keep_both_format_variants_through_series_filtering()
        {
            var book = new Book
            {
                ForeignBookId = "hardcover:work:543",
                Title = "Print, ebook, and audio",
                Editions = new List<Edition>
                {
                    new Edition { ForeignEditionId = "hardcover:edition:543", Format = "Print" },
                    new Edition { ForeignEditionId = "hardcover:source-edition:60", Format = "Ebook", IsEbook = true },
                    new Edition { ForeignEditionId = "hardcover:source-edition:80", Format = "Audio CD" }
                }
            };
            var series = new Series { ForeignSeriesId = "hardcover:series:789", Title = "Variant Series" };
            series.LinkItems = new List<SeriesBookLink> { new SeriesBookLink { Book = book, Series = series, IsPrimary = true } };
            var author = new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = "hardcover:author:123" },
                Books = new List<Book> { book },
                Series = new List<Series> { series }
            };
            Mocker.GetMock<IMetadataProfileRepository>()
                .Setup(repository => repository.Get(3))
                .Returns(new MetadataProfile { Id = 3, Name = "Complete Bibliography" });
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByAuthor(0)).Returns(new List<BookFile>());

            var filtered = Subject.FilterBooks(author, 3).Single();

            filtered.Editions.Value.Should().HaveCount(3);
            filtered.GetPreferredEdition(FormatType.Ebook).ForeignEditionId.Should().Be("hardcover:source-edition:60");
            filtered.GetPreferredEdition(FormatType.Audiobook).ForeignEditionId.Should().Be("hardcover:source-edition:80");
        }

        [Test]
        public void init_should_add_default_profiles()
        {
            Subject.Handle(new ApplicationStartedEvent());

            var profiles = Mocker.GetMock<IMetadataProfileRepository>();
            profiles.Verify(v => v.Insert(It.IsAny<MetadataProfile>()), Times.Exactly(3));
            profiles.Verify(v => v.Insert(It.Is<MetadataProfile>(p => p.Name == "Complete Bibliography" &&
                p.MinPopularity == 0 && p.MinPages == 0 && !p.SkipMissingDate && !p.SkipMissingIsbn &&
                !p.SkipPartsAndSets && !p.SkipSeriesSecondary && string.IsNullOrWhiteSpace(p.AllowedLanguages) &&
                p.Ignored.Count == 0)), Times.Once());
        }

        [Test]

        //This confirms that new profiles are added only if no other profiles exists.
        //We don't want to keep adding them back if a user deleted them on purpose.
        public void Init_should_skip_if_any_profiles_already_exist()
        {
            var profiles = Builder<MetadataProfile>.CreateListOfSize(2).Build().ToList();
            profiles[0].Name = "Standard";
            profiles[1].Name = "Complete Bibliography";

            Mocker.GetMock<IMetadataProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(profiles);

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMetadataProfileRepository>()
                  .Verify(v => v.Insert(It.Is<MetadataProfile>(x => x.Name == "Standard" ||
                      x.Name == "Complete Bibliography")), Times.Never());
        }

        [Test]
        public void init_should_add_none_profile_if_it_doesnt_exist()
        {
            Mocker.GetMock<IMetadataProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(Builder<MetadataProfile>.CreateListOfSize(2).Build().ToList());

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMetadataProfileRepository>()
                  .Verify(v => v.Insert(It.Is<MetadataProfile>(x => x.Name == "None")), Times.Once());
        }

        [Test]
        public void init_should_remove_openlibrary_profile_if_it_exists()
        {
            var profiles = Builder<MetadataProfile>.CreateListOfSize(3).Build().ToList();
            profiles[0].Name = MetadataProfileService.OPEN_LIBRARY_PROFILE_NAME;
            profiles[1].Name = "Standard";

            Mocker.GetMock<IMetadataProfileRepository>()
                .Setup(s => s.All())
                .Returns(profiles);

            Mocker.GetMock<IMetadataProfileRepository>()
                .Setup(s => s.Get(profiles[0].Id))
                .Returns(profiles[0]);

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAuthorsByMetadataProfile(profiles[0].Id))
                .Returns(new List<Author>());

            Mocker.GetMock<IImportListFactory>()
                .Setup(s => s.All())
                .Returns(new List<ImportListDefinition>());

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.All())
                .Returns(new List<RootFolder>());

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMetadataProfileRepository>()
                .Verify(v => v.Delete(profiles[0].Id), Times.Once());
        }

        [Test]
        public void init_should_not_add_openlibrary_profile()
        {
            Mocker.GetMock<IMetadataProfileRepository>()
                .Setup(s => s.All())
                .Returns(Builder<MetadataProfile>.CreateListOfSize(2).Build().ToList());

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMetadataProfileRepository>()
                .Verify(v => v.Insert(It.Is<MetadataProfile>(x => x.Name == MetadataProfileService.OPEN_LIBRARY_PROFILE_NAME)), Times.Never());
        }

        [TestCase("Other", "None.1")]
        [TestCase("none.1", "None.2")]
        public void init_should_move_existing_none_profile(string existingName, string renamed)
        {
            var profiles = Builder<MetadataProfile>.CreateListOfSize(2)
                .TheFirst(1)
                .With(x => x.Name = MetadataProfileService.NONE_PROFILE_NAME)
                .BuildList();
            profiles[1].Name = existingName;

            Mocker.GetMock<IMetadataProfileRepository>()
                .Setup(s => s.All())
                .Returns(profiles);

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMetadataProfileRepository>()
                .Verify(v => v.Update(It.Is<MetadataProfile>(x => x.Id == profiles.First().Id && x.Name == renamed)), Times.Once());

            Mocker.GetMock<IMetadataProfileRepository>()
                  .Verify(v => v.Insert(It.Is<MetadataProfile>(x => x.Name == "None")), Times.Once());
        }

        [Test]
        public void should_not_be_able_to_edit_none_profile()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Name = MetadataProfileService.NONE_PROFILE_NAME)
                .Build();

            Assert.Throws<InvalidOperationException>(() => Subject.Update(profile));
        }

        [Test]
        public void should_not_be_able_to_delete_none_profile()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Name = MetadataProfileService.NONE_PROFILE_NAME)
                .Build();

            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<MetadataProfileInUseException>(() => Subject.Delete(profile.Id));
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_author()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.MetadataProfileId = 1)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultMetadataProfileId = 1)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.AuthorExistsWithMetadataProfile(profile.Id)).Returns(true);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<MetadataProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IMetadataProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_import_list()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .Random(1)
                .With(c => c.MetadataProfileId = profile.Id)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultMetadataProfileId = 1)
                .BuildList();

            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<MetadataProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IMetadataProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_root_folder()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.MetadataProfileId = 1)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .Random(1)
                .With(f => f.DefaultMetadataProfileId = profile.Id)
                .BuildList();

            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<MetadataProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IMetadataProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_delete_profile_if_not_assigned_to_author_import_list_or_root_folder()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Id = 1)
                .Build();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.MetadataProfileId = 2)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultMetadataProfileId = 2)
                .BuildList();

            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Subject.Delete(1);

            Mocker.GetMock<IMetadataProfileRepository>().Verify(c => c.Delete(1), Times.Once());
        }
    }
}
