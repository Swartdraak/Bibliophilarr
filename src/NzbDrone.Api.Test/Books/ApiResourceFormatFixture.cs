using System.Collections.Generic;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Api.V1.Books;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class AuthorFormatProfileResourceMapperFixture
    {
        [Test]
        public void should_map_model_to_resource()
        {
            var model = new AuthorFormatProfile
            {
                Id = 1,
                AuthorId = 42,
                FormatType = FormatType.Audiobook,
                QualityProfileId = 3,
                RootFolderPath = "/audiobooks",
                Tags = new HashSet<int> { 1, 2 },
                Monitored = true,
                MonitorNewItems = NewItemMonitorTypes.New
            };

            var resource = model.ToResource();

            resource.Id.Should().Be(1);
            resource.AuthorId.Should().Be(42);
            resource.FormatType.Should().Be(FormatType.Audiobook);
            resource.QualityProfileId.Should().Be(3);
            resource.RootFolderPath.Should().Be("/audiobooks");
            resource.Tags.Should().Contain(new[] { 1, 2 });
            resource.Monitored.Should().BeTrue();
            resource.MonitorNewItems.Should().Be(NewItemMonitorTypes.New);
        }

        [Test]
        public void should_map_resource_to_model()
        {
            var resource = new AuthorFormatProfileResource
            {
                Id = 1,
                AuthorId = 42,
                FormatType = FormatType.Ebook,
                QualityProfileId = 2,
                RootFolderPath = "/ebooks",
                Tags = new HashSet<int> { 3 },
                Monitored = false,
                MonitorNewItems = NewItemMonitorTypes.None
            };

            var model = resource.ToModel();

            model.Id.Should().Be(1);
            model.AuthorId.Should().Be(42);
            model.FormatType.Should().Be(FormatType.Ebook);
            model.QualityProfileId.Should().Be(2);
            model.RootFolderPath.Should().Be("/ebooks");
            model.Tags.Should().Contain(3);
            model.Monitored.Should().BeFalse();
            model.MonitorNewItems.Should().Be(NewItemMonitorTypes.None);
        }

        [Test]
        public void should_return_null_when_model_is_null()
        {
            AuthorFormatProfile model = null;
            model.ToResource().Should().BeNull();
        }

        [Test]
        public void should_return_null_when_resource_is_null()
        {
            AuthorFormatProfileResource resource = null;
            resource.ToModel().Should().BeNull();
        }

        [Test]
        public void should_map_collection()
        {
            var models = new List<AuthorFormatProfile>
            {
                new AuthorFormatProfile { Id = 1, AuthorId = 42, FormatType = FormatType.Ebook },
                new AuthorFormatProfile { Id = 2, AuthorId = 42, FormatType = FormatType.Audiobook }
            };

            var resources = models.ToResource();

            resources.Should().HaveCount(2);
            resources[0].FormatType.Should().Be(FormatType.Ebook);
            resources[1].FormatType.Should().Be(FormatType.Audiobook);
        }
    }

    [TestFixture]
    public class BookFormatStatusResourceFixture
    {
        [TestCase(0, 0)]
        [TestCase(1, 100)]
        [TestCase(2, 100)]
        [TestCase(5, 100)]
        public void should_count_available_works_instead_of_files(int fileCount, int percent)
        {
            var result = new BookStatistics
            {
                BookCount = 1,
                AvailableBookCount = fileCount > 0 ? 1 : 0,
                BookFileCount = fileCount
            }.ToResource();

            result.BookFileCount.Should().Be(fileCount);
            result.PercentOfBooks.Should().Be(percent);
        }

        [TestCase("openlibrary:work:OL123W", null, "OL123W", "Open Library")]
        [TestCase("hardcover:work:432", "OL456W", "OL456W", "Hardcover")]
        [TestCase("googlebooks:work:volume", null, null, "Google Books")]
        [TestCase("inventaire:work:uuid", "OL456M", null, "Inventaire")]
        [TestCase("local:work:88", null, null, null)]
        public void should_expose_known_book_provenance(string foreignId, string storedWorkId, string workId, string source)
        {
            var result = new Book { ForeignBookId = foreignId, OpenLibraryWorkId = storedWorkId }.ToResource();

            result.OpenLibraryWorkId.Should().Be(workId);
            result.MetadataSourceLabel.Should().Be(source);
        }

        [Test]
        public void should_expose_another_same_work_cover_without_changing_the_preferred_edition()
        {
            var cover = new MediaCover(MediaCoverTypes.Cover, "https://covers.example/edition.jpg");
            var book = new Book
            {
                Title = "Fixture Work",
                Editions = new List<Edition>
                {
                    new Edition { ForeignEditionId = "preferred", Monitored = true },
                    new Edition { ForeignEditionId = "alternate", Images = new List<MediaCover> { cover } }
                }
            };

            var resource = book.ToResource();

            resource.ForeignEditionId.Should().Be("preferred");
            resource.Images.Should().ContainSingle().Which.Url.Should().Be(cover.Url);
            resource.Images[0].Should().NotBeSameAs(cover);
        }

        [Test]
        public void should_include_ebook_format_status_when_ebook_edition_exists()
        {
            var book = new Book
            {
                Id = 1,
                ForeignBookId = "test-1",
                Title = "Test Book",
                Monitored = true,
                Genres = new List<string>(),
                Links = new List<Links>(),
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = "e-1",
                        IsEbook = true,
                        Monitored = true,
                        Images = new List<MediaCover>(),
                        Links = new List<Links>(),
                        Ratings = new Ratings()
                    }
                }
            };

            var resource = book.ToResource();

            resource.FormatStatuses.Should().HaveCount(1);
            resource.FormatStatuses[0].FormatType.Should().Be(FormatType.Ebook);
            resource.FormatStatuses[0].Monitored.Should().BeTrue();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_not_infer_audiobook_availability_or_monitoring_from_a_print_edition(bool includeAudio)
        {
            var edits = new List<Edition> { new Edition { Format = "Print", Monitored = true } };
            if (includeAudio)
            {
                edits.Add(new Edition { Format = "Audio CD", Monitored = false });
            }

            var book = new Book { Id = 1, Title = "Test Book", Monitored = true, Editions = edits };

            var status = book.ToResource().FormatStatuses;

            if (includeAudio)
            {
                status.Should().ContainSingle().Which.FormatType.Should().Be(FormatType.Audiobook);
                status[0].Monitored.Should().BeFalse();
            }
            else
            {
                status.Should().BeEmpty();
            }
        }

        [Test]
        public void should_include_both_format_statuses_for_dual_format_book()
        {
            var book = new Book
            {
                Id = 1,
                ForeignBookId = "test-1",
                Title = "Test Book",
                Monitored = true,
                Genres = new List<string>(),
                Links = new List<Links>(),
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = "e-1",
                        IsEbook = true,
                        Monitored = true,
                        Images = new List<MediaCover>(),
                        Links = new List<Links>(),
                        Ratings = new Ratings()
                    },
                    new Edition
                    {
                        ForeignEditionId = "a-1",
                        IsEbook = false,
                        Monitored = true,
                        Images = new List<MediaCover>(),
                        Links = new List<Links>(),
                        Ratings = new Ratings()
                    }
                }
            };

            var resource = book.ToResource();

            resource.FormatStatuses.Should().HaveCount(2);
            resource.FormatStatuses.Should().Contain(s => s.FormatType == FormatType.Ebook && s.Monitored);
            resource.FormatStatuses.Should().Contain(s => s.FormatType == FormatType.Audiobook && s.Monitored);
        }

        [Test]
        public void should_show_unmonitored_when_no_monitored_edition_for_format()
        {
            var book = new Book
            {
                Id = 1,
                ForeignBookId = "test-1",
                Title = "Test Book",
                Monitored = true,
                Genres = new List<string>(),
                Links = new List<Links>(),
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = "e-1",
                        IsEbook = true,
                        Monitored = false,
                        Images = new List<MediaCover>(),
                        Links = new List<Links>(),
                        Ratings = new Ratings()
                    }
                }
            };

            var resource = book.ToResource();

            resource.FormatStatuses.Should().HaveCount(1);
            resource.FormatStatuses[0].FormatType.Should().Be(FormatType.Ebook);
            resource.FormatStatuses[0].Monitored.Should().BeFalse();
        }

        [Test]
        public void should_not_crash_with_dual_monitored_editions()
        {
            // Dual-format: both ebook and audiobook editions monitored
            // should not throw (previously used SingleOrDefault which would crash)
            var book = new Book
            {
                Id = 1,
                ForeignBookId = "test-1",
                Title = "Test Book",
                Monitored = true,
                Genres = new List<string>(),
                Links = new List<Links>(),
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = "e-1",
                        IsEbook = true,
                        Monitored = true,
                        Images = new List<MediaCover>(),
                        Links = new List<Links>(),
                        Ratings = new Ratings()
                    },
                    new Edition
                    {
                        ForeignEditionId = "a-1",
                        IsEbook = false,
                        Monitored = true,
                        Images = new List<MediaCover>(),
                        Links = new List<Links>(),
                        Ratings = new Ratings()
                    }
                }
            };

            // Should not throw InvalidOperationException
            var resource = book.ToResource();

            resource.Should().NotBeNull();
            resource.ForeignEditionId.Should().NotBeNull();
        }
    }
}
