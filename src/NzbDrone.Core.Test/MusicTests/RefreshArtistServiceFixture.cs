using System;
using System.Collections.Generic;

using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshAuthorServiceFixture : CoreTest<RefreshAuthorService>
    {
        private Author _author;
        private Book _book1;
        private Book _book2;
        private List<Book> _books;
        private List<Book> _remoteBooks;

        [SetUp]
        public void Setup()
        {
            _book1 = Builder<Book>.CreateNew()
                .With(s => s.ForeignBookId = "1")
                .Build();

            _book2 = Builder<Book>.CreateNew()
                .With(s => s.ForeignBookId = "2")
                .Build();

            _books = new List<Book> { _book1, _book2 };

            _remoteBooks = _books.JsonClone();
            _remoteBooks.ForEach(x => x.Id = 0);

            var metadata = Builder<AuthorMetadata>.CreateNew().Build();
            var series = Builder<Series>.CreateListOfSize(1).BuildList();
            var profile = Builder<MetadataProfile>.CreateNew().Build();

            _author = Builder<Author>.CreateNew()
                .With(a => a.Metadata = metadata)
                .With(a => a.Series = series)
                .With(a => a.MetadataProfile = profile)
                .Build();

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                  .Setup(s => s.GetAuthors(new List<int> { _author.Id }))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IBookService>(MockBehavior.Strict)
                .Setup(s => s.InsertMany(It.IsAny<List<Book>>()));

            Mocker.GetMock<IMetadataProfileService>()
                .Setup(s => s.FilterBooks(It.IsAny<Author>(), It.IsAny<int>()))
                .Returns(_remoteBooks);

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                  .Setup(s => s.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>()))
                  .Callback(() => { throw new AuthorNotFoundException(_author.ForeignAuthorId); });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IHistoryService>()
                .Setup(x => x.GetByAuthor(It.IsAny<int>(), It.IsAny<EntityHistoryEventType?>()))
                .Returns(new List<EntityHistory>());

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(x => x.FindByForeignId(It.IsAny<List<string>>()))
                .Returns(new List<ImportListExclusion>());

            Mocker.GetMock<IRootFolderService>()
                .Setup(x => x.All())
                .Returns(new List<RootFolder>());

            Mocker.GetMock<IMonitorNewBookService>()
                .Setup(x => x.ShouldMonitorNewBook(It.IsAny<Book>(), It.IsAny<List<Book>>(), It.IsAny<NewItemMonitorTypes>()))
                .Returns(true);
        }

        private void GivenNewAuthorInfo(Author author)
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                  .Setup(s => s.GetAuthorInfo(_author.ForeignAuthorId, It.IsAny<bool>()))
                  .Returns(author);
        }

        private void GivenAuthorFiles()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(x => x.GetFilesByAuthor(It.IsAny<int>()))
                  .Returns(Builder<BookFile>.CreateListOfSize(1).BuildList());
        }

        private void GivenBooksForRefresh(List<Book> books)
        {
            Mocker.GetMock<IBookService>(MockBehavior.Strict)
                .Setup(s => s.GetBooksForRefresh(It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns(books);
        }

        private void AllowAuthorUpdate()
        {
            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                .Setup(x => x.UpdateAuthor(It.IsAny<Author>()))
                .Returns((Author a) => a);
        }

        [Test]
        public void should_not_publish_author_updated_event_if_metadata_not_updated()
        {
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Books = _remoteBooks;

            GivenNewAuthorInfo(newAuthorInfo);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            VerifyEventNotPublished<AuthorUpdatedEvent>();
            VerifyEventPublished<AuthorRefreshCompleteEvent>();
        }

        [Test]
        public void should_publish_author_updated_event_if_metadata_updated()
        {
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Metadata.Value.Images = new List<MediaCover.MediaCover>
            {
                new MediaCover.MediaCover(MediaCover.MediaCoverTypes.Logo, "dummy")
            };
            newAuthorInfo.Books = _remoteBooks;

            GivenNewAuthorInfo(newAuthorInfo);
            GivenBooksForRefresh(new List<Book>());
            AllowAuthorUpdate();

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            VerifyEventPublished<AuthorUpdatedEvent>();
            VerifyEventPublished<AuthorRefreshCompleteEvent>();
        }

        [Test]
        public void should_call_new_book_monitor_service_when_adding_book()
        {
            var newBook = Builder<Book>.CreateNew()
                .With(x => x.Id = 0)
                .With(x => x.ForeignBookId = "3")
                .Build();
            _remoteBooks.Add(newBook);

            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Books = _remoteBooks;

            GivenNewAuthorInfo(newAuthorInfo);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IMonitorNewBookService>()
                .Verify(x => x.ShouldMonitorNewBook(newBook, _books, _author.MonitorNewItems), Times.Once());
        }

        [Test]
        public void should_log_error_and_delete_if_musicbrainz_id_not_found_and_author_has_no_files()
        {
            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateAuthor(It.IsAny<Author>()), Times.Never());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once());

            ExceptionVerification.ExpectedErrors(1);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_log_error_but_not_delete_if_musicbrainz_id_not_found_and_author_has_files()
        {
            GivenAuthorFiles();
            GivenBooksForRefresh(new List<Book>());

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateAuthor(It.IsAny<Author>()), Times.Never());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedErrors(2);
        }

        [Test]
        public void should_update_if_musicbrainz_id_changed_and_no_clash()
        {
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Books = _remoteBooks;
            newAuthorInfo.ForeignAuthorId = _author.ForeignAuthorId + 1;
            newAuthorInfo.Metadata.Value.Id = 100;

            GivenNewAuthorInfo(newAuthorInfo);

            var seq = new MockSequence();

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                .Setup(x => x.FindById(newAuthorInfo.ForeignAuthorId))
                .Returns(default(Author));

            // Make sure that the author is updated before we refresh the books
            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                .InSequence(seq)
                .Setup(x => x.UpdateAuthor(It.IsAny<Author>()))
                .Returns((Author a) => a);

            Mocker.GetMock<IBookService>(MockBehavior.Strict)
                .InSequence(seq)
                .Setup(x => x.GetBooksForRefresh(It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns(new List<Book>());

            // Update called twice for a move/merge
            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                .InSequence(seq)
                .Setup(x => x.UpdateAuthor(It.IsAny<Author>()))
                .Returns((Author a) => a);

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateAuthor(It.Is<Author>(s => s.AuthorMetadataId == 100 && s.ForeignAuthorId == newAuthorInfo.ForeignAuthorId)),
                        Times.Exactly(2));
        }

        [Test]
        public void should_merge_if_musicbrainz_id_changed_and_new_id_already_exists()
        {
            var existing = _author;

            var clash = _author.JsonClone();
            clash.Id = 100;
            clash.Metadata = existing.Metadata.Value.JsonClone();
            clash.Metadata.Value.Id = 101;
            clash.Metadata.Value.ForeignAuthorId = clash.Metadata.Value.ForeignAuthorId + 1;

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                .Setup(x => x.FindById(clash.Metadata.Value.ForeignAuthorId))
                .Returns(clash);

            var newAuthorInfo = clash.JsonClone();
            newAuthorInfo.Metadata = clash.Metadata.Value.JsonClone();
            newAuthorInfo.Books = _remoteBooks;

            GivenNewAuthorInfo(newAuthorInfo);

            var seq = new MockSequence();

            // Make sure that the author is updated before we refresh the books
            Mocker.GetMock<IBookService>(MockBehavior.Strict)
                .InSequence(seq)
                .Setup(x => x.GetBooksByAuthor(existing.Id))
                .Returns(_books);

            Mocker.GetMock<IBookService>(MockBehavior.Strict)
                .InSequence(seq)
                .Setup(x => x.UpdateMany(It.IsAny<List<Book>>()));

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                .InSequence(seq)
                .Setup(x => x.DeleteAuthor(existing.Id, It.IsAny<bool>(), false));

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                .InSequence(seq)
                .Setup(x => x.UpdateAuthor(It.Is<Author>(a => a.Id == clash.Id)))
                .Returns((Author a) => a);

            Mocker.GetMock<IBookService>(MockBehavior.Strict)
                .InSequence(seq)
                .Setup(x => x.GetBooksForRefresh(clash.AuthorMetadataId, It.IsAny<List<string>>()))
                .Returns(_books);

            // Update called twice for a move/merge
            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                .InSequence(seq)
                .Setup(x => x.UpdateAuthor(It.IsAny<Author>()))
                .Returns((Author a) => a);

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            // the retained author gets updated
            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateAuthor(It.Is<Author>(s => s.Id == clash.Id)), Times.Exactly(2));

            // the old one gets removed
            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(existing.Id, false, false));

            Mocker.GetMock<IBookService>()
                .Verify(v => v.UpdateMany(It.Is<List<Book>>(x => x.Count == _books.Count)));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_use_orchestrator_for_author_info_not_direct_provider()
        {
            // Arrange: orchestrator returns valid author data (simulates secondary-provider fallback success)
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Books = _remoteBooks;

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                  .Setup(s => s.GetAuthorInfo(_author.ForeignAuthorId, It.IsAny<bool>()))
                  .Returns(newAuthorInfo);

            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            // Verify the orchestrator was called — not a direct IProvideAuthorInfo mock
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                  .Verify(s => s.GetAuthorInfo(_author.ForeignAuthorId, It.IsAny<bool>()), Times.Once);

            // Direct provider should never be called for author info during refresh
            Mocker.GetMock<IProvideAuthorInfo>()
                  .Verify(s => s.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);

            VerifyEventPublished<AuthorRefreshCompleteEvent>();
        }

        [Test]
        public void should_use_orchestrator_for_changed_author_lookup_not_direct_provider()
        {
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Books = _remoteBooks;

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAllAuthors())
                .Returns(new List<Author> { _author });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                  .Setup(s => s.GetChangedAuthors(It.IsAny<DateTime>()))
                  .Returns(new HashSet<string> { _author.ForeignAuthorId });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                  .Setup(s => s.GetAuthorInfo(_author.ForeignAuthorId, It.IsAny<bool>()))
                  .Returns(newAuthorInfo);

            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            Subject.Execute(new RefreshAuthorCommand
            {
                LastExecutionTime = DateTime.UtcNow,
                LastStartTime = DateTime.UtcNow.AddMinutes(-5)
            });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                  .Verify(s => s.GetChangedAuthors(It.IsAny<DateTime>()), Times.Once);

            Mocker.GetMock<IProvideAuthorInfo>()
                  .Verify(s => s.GetChangedAuthors(It.IsAny<DateTime>()), Times.Never);

            VerifyEventPublished<AuthorRefreshCompleteEvent>();
        }

        [Test]
        public void should_not_queue_duplicate_rescan_when_matching_rescan_is_already_queued()
        {
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Metadata.Value.Overview = "Updated overview";
            newAuthorInfo.Books = _remoteBooks;

            GivenNewAuthorInfo(newAuthorInfo);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            var folders = new List<RootFolder>
            {
                new RootFolder { Path = "/media/ebooks" }
            };

            Mocker.GetMock<IRootFolderService>()
                .Setup(x => x.All())
                .Returns(folders);

            var existingCommand = new CommandModel
            {
                Name = nameof(RescanFoldersCommand),
                Status = CommandStatus.Queued,
                Body = new RescanFoldersCommand(new List<string> { "/media/ebooks" }, FilterFilesType.Matched, false, new List<int> { _author.Id })
            };

            Mocker.GetMock<IManageCommandQueue>()
                .Setup(x => x.All())
                .Returns(new List<CommandModel> { existingCommand });

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never);
        }

        [Test]
        public void should_not_queue_duplicate_rescan_during_repeated_refresh_command_storm()
        {
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            var invocation = 0;
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(s => s.GetAuthorInfo(_author.ForeignAuthorId, It.IsAny<bool>()))
                .Returns(() =>
                {
                    var newAuthorInfo = _author.JsonClone();
                    newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
                    newAuthorInfo.Metadata.Value.Overview = $"Updated overview {invocation++}";
                    newAuthorInfo.Books = _remoteBooks;
                    return newAuthorInfo;
                });

            Mocker.GetMock<IRootFolderService>()
                .Setup(x => x.All())
                .Returns(new List<RootFolder> { new RootFolder { Path = "/media/ebooks" } });

            var existingCommand = new CommandModel
            {
                Name = nameof(RescanFoldersCommand),
                Status = CommandStatus.Started,
                Body = new RescanFoldersCommand(new List<string> { "/media/ebooks" }, FilterFilesType.Matched, false, new List<int> { _author.Id })
            };

            Mocker.GetMock<IManageCommandQueue>()
                .Setup(x => x.All())
                .Returns(new List<CommandModel> { existingCommand });

            for (var i = 0; i < 5; i++)
            {
                Subject.Execute(new RefreshAuthorCommand(_author.Id) { Trigger = CommandTrigger.Manual });
            }

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never);
        }

        [Test]
        public void should_refresh_author_overview_and_images_when_books_are_filtered_by_metadata_profile()
        {
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Metadata.Value.Overview = "Updated overview from provider";
            newAuthorInfo.Metadata.Value.Images = new List<MediaCover.MediaCover>
            {
                new MediaCover.MediaCover(MediaCover.MediaCoverTypes.Poster, "https://covers.example/author.jpg")
            };

            newAuthorInfo.Books = _remoteBooks;

            GivenNewAuthorInfo(newAuthorInfo);
            AllowAuthorUpdate();
            GivenBooksForRefresh(new List<Book>());

            Mocker.GetMock<IMetadataProfileService>()
                .Setup(s => s.FilterBooks(It.IsAny<Author>(), It.IsAny<int>()))
                .Returns(new List<Book>());

            Subject.Execute(new RefreshAuthorCommand(_author.Id) { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<IAuthorService>()
                .Verify(x => x.UpdateAuthor(It.Is<Author>(a =>
                    a.Metadata.Value.Overview == "Updated overview from provider" &&
                    a.Metadata.Value.Images.Count == 1 &&
                    a.Metadata.Value.Images[0].Url == "https://covers.example/author.jpg")), Times.AtLeastOnce);

            Mocker.GetMock<IMetadataProfileService>()
                .Verify(s => s.FilterBooks(It.IsAny<Author>(), _author.MetadataProfileId), Times.Once);
        }
    }
}
