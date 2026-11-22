using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Moq;
using Moq.Protected;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshBookDeletionGuardFixture : CoreTest<RefreshBookService>
    {
        private Book _book;

        [SetUp]
        public void SetUp()
        {
            _book = new Book
            {
                Id = 7,
                ForeignBookId = "openlibrary:work:OL7W",
                Title = "Guarded Delete",
                AddOptions = new AddBookOptions { AddType = BookAddType.Automatic },
                AuthorMetadata = new AuthorMetadata { ForeignAuthorId = "openlibrary:author:OL7A", Name = "Author" },
                Editions = new List<Edition>()
            };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(_book.Id))
                .Returns(new List<BookFile>());

            Mocker.GetMock<ICheckIfBookShouldBeRefreshed>()
                .Setup(x => x.ShouldRefresh(It.IsAny<Book>()))
                .Returns(true);

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.GetBookInfo(It.IsAny<string>()))
                .Throws(new BookNotFoundException(_book.ForeignBookId));
        }

        [TestCase("hardcover:source-edition:80", "hardcover:source-edition:81")]
        [TestCase("openlibrary:edition:OL80M", "openlibrary:edition:OL81M")]
        public void should_keep_legacy_and_file_linked_source_editions_when_a_refresh_changes_representatives(string attachedEditionId, string unlinkedEditionId)
        {
            _book.ForeignBookId = "hardcover:work:7";
            _book.AuthorMetadata.Value.ForeignAuthorId = "hardcover:author:123";
            var author = new Author { Metadata = _book.AuthorMetadata };
            _book.Author = author;
            var legacy = new Edition { Id = 10, BookId = 7, ForeignEditionId = "hardcover:edition:7", Title = _book.Title, Monitored = true };
            var priorAudio = new Edition { Id = 12, BookId = 7, ForeignEditionId = attachedEditionId, Format = "Audio CD" };
            var emptyPriorVariant = new Edition { Id = 14, BookId = 7, ForeignEditionId = unlinkedEditionId, Format = "Audio CD" };
            var newAudio = new Edition { ForeignEditionId = "hardcover:source-edition:82", Format = "Audiobook" };
            var remote = new Book
            {
                ForeignBookId = _book.ForeignBookId,
                Title = _book.Title,
                AuthorMetadata = _book.AuthorMetadata,
                Editions = new List<Edition> { new Edition { ForeignEditionId = legacy.ForeignEditionId, Title = _book.Title }, newAudio }
            };

            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(author.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsForRefresh(_book.Id, It.IsAny<List<string>>()))
                .Returns(new List<Edition> { legacy, priorAudio, emptyPriorVariant });
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByEdition(12))
                .Returns(new List<BookFile> { new BookFile { Id = 99, EditionId = 12 } });
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByEdition(14))
                .Returns(new List<BookFile>());

            Subject.RefreshBookInfo(_book, new List<Book> { remote }, author, false);

            Mocker.GetMock<IEditionService>().Verify(service => service.InsertMany(
                It.Is<List<Edition>>(editions => editions.Single().ForeignEditionId == newAudio.ForeignEditionId)), Times.Once);
            Mocker.GetMock<IRefreshEditionService>().Verify(service => service.RefreshEditionInfo(
                It.IsAny<List<Edition>>(),
                It.Is<List<Edition>>(editions => editions.All(edition => edition.Id != priorAudio.Id)),
                It.IsAny<List<Tuple<Edition, Edition>>>(),
                It.Is<List<Edition>>(editions => editions.Single().Id == emptyPriorVariant.Id),
                It.Is<List<Edition>>(editions => editions.Any(edition => edition.Id == priorAudio.Id)),
                It.IsAny<List<Edition>>(),
                It.IsAny<bool>()), Times.Once);
            Mocker.GetMock<IMapCoversToLocal>().Verify(service => service.EnsureBookCovers(It.IsAny<Book>()), Times.Never);
        }

        [Test]
        public void should_leave_an_edition_with_its_work_when_it_is_claimed_after_supplemental_bibliography_selection()
        {
            _book.ForeignBookId = "hardcover:work:7";
            _book.IsSupplementalBibliographyWork = true;
            var author = new Author { Metadata = _book.AuthorMetadata };
            _book.Author = author;
            var occupied = new Edition
            {
                Id = 101,
                BookId = 88,
                ForeignEditionId = "hardcover:source-edition:101",
                Title = "Existing edition",
                Format = "Ebook",
                IsEbook = true,
                Monitored = true
            };
            var candidate = new Edition
            {
                ForeignEditionId = occupied.ForeignEditionId,
                Title = "Alternate edition",
                Format = occupied.Format,
                IsEbook = true
            };
            var novel = new Edition { ForeignEditionId = "hardcover:source-edition:102", Format = occupied.Format, IsEbook = true };
            var remote = new Book
            {
                ForeignBookId = _book.ForeignBookId,
                Title = _book.Title,
                AuthorMetadata = _book.AuthorMetadata,
                Editions = new List<Edition> { candidate, novel }
            };

            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(author.ForeignAuthorId)).Returns(author);

            // This query occurs during the child refresh, after the supplemental work and its proposed editions were selected.
            var editions = Mocker.GetMock<IEditionService>();
            editions.Setup(service => service.GetEditionsForRefresh(_book.Id, It.Is<List<string>>(ids => ids.Contains(occupied.ForeignEditionId))))
                .Returns(new List<Edition> { occupied });
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByEdition(occupied.Id))
                .Returns(new List<BookFile> { new BookFile { Id = 501, EditionId = occupied.Id } });
            Mocker.SetConstant<IRefreshEditionService>(Mocker.Resolve<RefreshEditionService>());

            Subject.RefreshBookInfo(_book, new List<Book> { remote }, author, false);

            occupied.BookId.Should().Be(88);
            occupied.Title.Should().Be("Existing edition");
            occupied.Monitored.Should().BeTrue();
            editions.Verify(service => service.InsertMany(It.Is<List<Edition>>(items => items.Count == 1 && items[0] == novel)), Times.Once());
            editions.Verify(service => service.UpdateMany(It.Is<List<Edition>>(items => items.Any(edition => edition.Id == occupied.Id))), Times.Never());
            editions.Verify(service => service.DeleteMany(It.Is<List<Edition>>(items => items.Any(edition => edition.Id == occupied.Id))), Times.Never());
        }

        [Test]
        public void should_refresh_a_work_with_its_verified_author_metadata_without_loading_a_bibliography()
        {
            var metadata = _book.AuthorMetadata.Value;
            var author = new Author { Metadata = metadata };
            _book.Author = author;
            var remote = new Book { ForeignBookId = _book.ForeignBookId, Title = "Updated title", Editions = new List<Edition>() };
            var providers = Mocker.GetMock<IMetadataProviderOrchestrator>();
            providers.Setup(service => service.GetBookInfo(_book.ForeignBookId))
                .Returns(Tuple.Create(metadata.ForeignAuthorId, remote, new List<AuthorMetadata> { metadata }));
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(metadata.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsForRefresh(_book.Id, It.IsAny<List<string>>())).Returns(new List<Edition>());

            Subject.RefreshBookInfo(_book);

            _book.Title.Should().Be("Updated title");
            providers.Verify(service => service.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_cache_artwork_after_persisting_a_standalone_work_refresh()
        {
            var metadata = _book.AuthorMetadata.Value;
            var author = new Author { Metadata = metadata };
            _book.Author = author;
            var edition = new Edition
            {
                ForeignEditionId = "openlibrary:edition:OL7M",
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://covers.example.invalid/7.jpg") }
            };
            var remote = new Book { ForeignBookId = _book.ForeignBookId, Title = _book.Title, Editions = new List<Edition> { edition } };
            var stored = new Book { Id = _book.Id, ForeignBookId = _book.ForeignBookId, Editions = new List<Edition> { edition } };
            Mocker.GetMock<IMetadataProviderOrchestrator>().Setup(service => service.GetBookInfo(_book.ForeignBookId))
                .Returns(Tuple.Create(metadata.ForeignAuthorId, remote, new List<AuthorMetadata> { metadata }));
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(metadata.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsForRefresh(_book.Id, It.IsAny<List<string>>())).Returns(new List<Edition>());
            var editionSaved = false;
            Mocker.GetMock<IEditionService>().Setup(service => service.InsertMany(It.Is<List<Edition>>(editions => editions.Single() == edition)))
                .Callback(() => editionSaved = true);
            var workSaved = false;
            var books = Mocker.GetMock<IBookService>();
            books.Setup(service => service.UpdateMany(It.Is<List<Book>>(works => works.Single() == _book)))
                .Callback(() => workSaved = true);
            books.Setup(service => service.FindById(_book.ForeignBookId)).Returns(() =>
            {
                (editionSaved && workSaved).Should().BeTrue();
                return stored;
            });

            Subject.RefreshBookInfo(_book);

            Mocker.GetMock<IMapCoversToLocal>().Verify(service => service.EnsureBookCovers(stored), Times.Once);
        }

        [Test]
        public void should_continue_a_bulk_book_refresh_when_one_provider_lookup_is_incomplete()
        {
            var next = new Book { Id = 8, ForeignBookId = "openlibrary:work:OL8W", Title = "Next work" };
            Mocker.GetMock<IBookService>().Setup(service => service.GetBooks(It.IsAny<List<int>>())).Returns(new List<Book> { _book, next });
            var providers = Mocker.GetMock<IMetadataProviderOrchestrator>();
            foreach (var book in new[] { _book, next })
            {
                providers.Setup(service => service.GetBookInfo(book.ForeignBookId)).Throws(
                    new MetadataProviderUnavailableException("get-book-info", book.ForeignBookId, new InvalidOperationException("Partial response")));
            }

            Subject.Execute(new BulkRefreshBookCommand(new List<int> { _book.Id, next.Id }));

            providers.Verify(service => service.GetBookInfo(next.ForeignBookId), Times.Once());
            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Exactly(2));
            Mocker.GetMock<IMapCoversToLocal>().Verify(service => service.EnsureBookCovers(It.IsAny<Book>()), Times.Never);
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void should_require_repeat_miss_before_hard_delete()
        {
            var remoteData = new Author { Metadata = _book.AuthorMetadata };

            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), remoteData, true, false, DateTime.UtcNow);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(_book.Id, false, false), Times.Never());

            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), remoteData, true, false, DateTime.UtcNow);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(_book.Id, false, false), Times.Once());

            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }

        [TestCase(true, 2)]
        [TestCase(false, 1)]
        public void should_preserve_a_monitored_edition_for_each_format_only_when_dual_tracking_is_enabled(bool dualFormat, int expectedCount)
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableDualFormatTracking).Returns(dualFormat);

            var author = new Author { Metadata = _book.AuthorMetadata };
            _book.Author = author;
            var ebook = new Edition { Id = 10, ForeignEditionId = "provider:edition:1", Format = "Ebook", IsEbook = true, Monitored = true };
            var audio = new Edition { Id = 11, ForeignEditionId = "provider:edition:2", Format = "Audiobook", Monitored = true };
            var editions = new List<Edition> { ebook, audio };
            var remote = new Book
            {
                ForeignBookId = _book.ForeignBookId,
                Title = _book.Title,
                AuthorMetadata = _book.AuthorMetadata,
                Editions = editions.Select(edition => new Edition
                {
                    ForeignEditionId = edition.ForeignEditionId,
                    Format = edition.Format,
                    IsEbook = edition.IsEbook
                }).ToList()
            };

            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(author.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsForRefresh(_book.Id, It.IsAny<List<string>>())).Returns(editions);
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByEdition(It.IsAny<int>())).Returns(new List<BookFile>());

            Subject.RefreshBookInfo(_book, new List<Book> { remote }, author, false);

            editions.Count(edition => edition.Monitored).Should().Be(expectedCount);
        }

        [Test]
        public void should_schedule_reparenting_when_an_identical_edition_has_a_different_persisted_owner()
        {
            var author = new Author { Metadata = _book.AuthorMetadata };
            _book.Author = author;
            var existing = new Edition { Id = 10, BookId = 88, ForeignEditionId = "provider:edition:1", Monitored = true };
            var remoteEdition = new Edition { ForeignEditionId = existing.ForeignEditionId };
            var remote = new Book
            {
                ForeignBookId = _book.ForeignBookId,
                Title = _book.Title,
                AuthorMetadata = _book.AuthorMetadata,
                Editions = new List<Edition> { remoteEdition }
            };
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(author.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsForRefresh(_book.Id, It.IsAny<List<string>>()))
                .Returns(new List<Edition> { existing });

            Subject.RefreshBookInfo(_book, new List<Book> { remote }, author, false);

            existing.BookId.Should().Be(88);
            remoteEdition.BookId.Should().Be(_book.Id);
            Mocker.GetMock<IRefreshEditionService>().Verify(service => service.RefreshEditionInfo(
                It.IsAny<List<Edition>>(),
                It.Is<List<Edition>>(editions => editions.Count == 1 && editions[0].Id == existing.Id),
                It.IsAny<List<Tuple<Edition, Edition>>>(),
                It.IsAny<List<Edition>>(),
                It.IsAny<List<Edition>>(),
                It.IsAny<List<Edition>>(),
                It.IsAny<bool>()), Times.Once);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_only_merge_file_backed_editions_when_all_target_editions_are_identified(bool sharedAudioIdentity)
        {
            var target = new Book
            {
                Id = 8,
                Editions = new List<Edition>
                {
                    new Edition { Id = 20, ForeignEditionId = "provider:edition:20", Format = "Ebook", IsEbook = true, Monitored = true, Asin = "B000000001" },
                    new Edition { Id = 21, ForeignEditionId = "provider:edition:21", Format = "Audiobook", Monitored = true, Asin = "B000000002" }
                }
            };
            var ebook = new BookFile
            {
                Id = 99,
                Path = "book.epub",
                EditionId = 10,
                Edition = new Edition { Id = 10, Format = "Ebook", IsEbook = true, Asin = "B000000001" }
            };
            var audio = new BookFile
            {
                Id = 100,
                Path = "book.m4b",
                EditionId = 11,
                Edition = new Edition { Id = 11, Format = "Audiobook", Asin = sharedAudioIdentity ? "B000000002" : "B000000003" }
            };
            var files = new List<BookFile> { ebook, audio };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByBook(_book.Id)).Returns(files);
            Mocker.GetMock<IHistoryService>().Setup(service => service.GetByBook(_book.Id, null)).Returns(new List<EntityHistory>());

            // The provider's current exact work lookup doesn't emit alias redirects.
            var merge = typeof(RefreshBookService).GetMethod("MergeEntity", BindingFlags.Instance | BindingFlags.NonPublic);
            merge.Invoke(Subject, new object[] { _book, target, target });

            ebook.EditionId.Should().Be(sharedAudioIdentity ? 20 : 10);
            audio.EditionId.Should().Be(sharedAudioIdentity ? 21 : 11);
            var writes = sharedAudioIdentity ? Times.Once() : Times.Never();
            Mocker.GetMock<IMediaFileService>().Verify(service => service.Update(files), writes);
            Mocker.GetMock<IBookService>().Verify(service => service.DeleteMany(It.IsAny<List<Book>>()), writes);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_skip_target_book_and_edition_updates_when_merge_is_deferred()
        {
            var author = new Author { Metadata = _book.AuthorMetadata };
            var target = new Book
            {
                Id = 8,
                ForeignBookId = "openlibrary:work:OL8W",
                AuthorMetadata = _book.AuthorMetadata,
                Editions = new List<Edition>()
            };
            var file = new BookFile { Id = 99, Path = "book.epub", EditionId = 10 };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByBook(_book.Id))
                .Returns(new List<BookFile> { file });
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(author.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IBookService>().Setup(service => service.FindById(target.ForeignBookId)).Returns(target);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsForRefresh(target.Id, It.IsAny<List<string>>()))
                .Returns(new List<Edition>());

            var refresh = new Mock<RefreshBookService>(
                Mocker.GetMock<IBookService>().Object,
                Mocker.GetMock<IAuthorService>().Object,
                Mocker.GetMock<IRootFolderService>().Object,
                Mocker.GetMock<IAddAuthorService>().Object,
                Mocker.GetMock<IEditionService>().Object,
                Mocker.GetMock<IAuthorMetadataService>().Object,
                Mocker.GetMock<IMetadataProviderOrchestrator>().Object,
                Mocker.GetMock<IRefreshEditionService>().Object,
                Mocker.GetMock<IMediaFileService>().Object,
                Mocker.GetMock<IHistoryService>().Object,
                Mocker.GetMock<IEventAggregator>().Object,
                Mocker.GetMock<ICheckIfBookShouldBeRefreshed>().Object,
                Mocker.GetMock<IMapCoversToLocal>().Object,
                Mocker.GetMock<IConfigService>().Object,
                Mocker.GetMock<ICommandResultReporter>().Object,
                LogManager.GetCurrentClassLogger())
            {
                CallBase = true
            };
            refresh.Protected().Setup<RefreshEntityServiceBase<Book, Edition>.RemoteData>(
                "GetRemoteData",
                ItExpr.IsAny<Book>(),
                ItExpr.IsAny<List<Book>>(),
                ItExpr.IsAny<Author>()).Returns(new RefreshEntityServiceBase<Book, Edition>.RemoteData { Entity = target });

            refresh.Object.RefreshBookInfo(_book, new List<Book> { target }, author, false).Should().BeFalse();

            Mocker.GetMock<IBookService>().Verify(service => service.UpdateMany(It.IsAny<List<Book>>()), Times.Never);
            Mocker.GetMock<IEditionService>().Verify(service => service.GetEditionsForRefresh(It.IsAny<int>(), It.IsAny<List<string>>()), Times.Never);
            file.EditionId.Should().Be(10);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_suppress_hard_delete_on_degraded_provider_window()
        {
            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), null, true, false, DateTime.UtcNow);
            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), null, true, false, DateTime.UtcNow);

            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(_book.Id, false, false), Times.Never());

            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }
    }
}
