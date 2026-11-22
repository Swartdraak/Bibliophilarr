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
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
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
                .With(a => a.RemoteSeriesComplete = null)
                .With(a => a.MetadataProfile = profile)
                .Build();

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                  .Setup(s => s.GetAuthors(new List<int> { _author.Id }))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IBookService>(MockBehavior.Strict)
                .Setup(s => s.InsertBibliographyWorks(It.IsAny<List<Book>>()))
                .Returns((List<Book> books) => books);

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
        public void should_record_a_verified_shared_credit_without_refreshing_another_authors_work()
        {
            _author.ForeignAuthorId = "hardcover:author:99";
            _book1.ForeignBookId = "hardcover:work:456";
            _book1.AuthorMetadataId = _author.AuthorMetadataId + 1;
            _book2.ForeignBookId = "hardcover:work:789";
            _book2.AuthorMetadataId = _author.AuthorMetadataId + 2;
            _remoteBooks[0].ForeignBookId = _book1.ForeignBookId;
            _remoteBooks[0].CreditedHardcoverAuthorId = 99;
            _remoteBooks[1].ForeignBookId = _book2.ForeignBookId;
            var metadata = _author.JsonClone();
            metadata.Metadata = _author.Metadata.Value.JsonClone();
            metadata.Books = _remoteBooks;
            GivenNewAuthorInfo(metadata);
            GivenBooksForRefresh(new List<Book> { _book1, _book2 });
            AllowAuthorUpdate();
            var books = Mocker.GetMock<IBookService>();
            books.Setup(service => service.RecordHardcoverAuthorCredits(_author.AuthorMetadataId, 99, It.IsAny<List<string>>()));

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            books.Verify(service => service.RecordHardcoverAuthorCredits(
                _author.AuthorMetadataId,
                99,
                It.Is<List<string>>(ids => ids.Count == 1 && ids[0] == "hardcover:work:456")), Times.Once());
            books.Verify(service => service.InsertBibliographyWorks(It.Is<List<Book>>(added => added.Count == 0)), Times.Once());
            Mocker.GetMock<IRefreshBookService>().Verify(service => service.RefreshBookInfo(
                It.Is<List<Book>>(list => list.Count == 0),
                It.IsAny<List<Book>>(),
                It.IsAny<Author>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<DateTime?>()), Times.Once());
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void should_refresh_an_existing_work_with_editions_from_provider_aliases()
        {
            _book1.Title = "Example Work";
            _book1.ReleaseDate = new DateTime(2022, 1, 25);
            _book1.OpenLibraryWorkId = "OL4242W";
            _book1.AuthorMetadataId = _author.AuthorMetadataId;
            var ebook = new Edition { ForeignEditionId = "hardcover:edition:123", Format = "Ebook" };
            var audiobook = new Edition { ForeignEditionId = "hardcover:edition:456", Format = "Audiobook" };
            _remoteBooks.Clear();
            _remoteBooks.Add(new Book
            {
                ForeignBookId = "hardcover:work:45",
                Title = _book1.Title,
                OpenLibraryWorkId = "OL4242W",
                ReleaseDate = _book1.ReleaseDate,
                Editions = new List<Edition> { ebook }
            });
            _remoteBooks.Add(new Book
            {
                ForeignBookId = "hardcover:work:54",
                Title = _book1.Title,
                OpenLibraryWorkId = "OL4242W",
                ReleaseDate = _book1.ReleaseDate,
                Editions = new List<Edition> { audiobook }
            });

            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Books = _remoteBooks;
            var primary = new SeriesBookLink { Book = _remoteBooks[1], IsPrimary = true, Position = "2" };
            var series = new Series
            {
                ForeignSeriesId = "hardcover:series:86",
                LinkItems = new List<SeriesBookLink> { new SeriesBookLink { Book = _remoteBooks[0] }, primary }
            };
            newAuthorInfo.Series = new List<Series> { series };
            GivenNewAuthorInfo(newAuthorInfo);
            GivenBooksForRefresh(new List<Book> { _book1 });
            AllowAuthorUpdate();

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IBookService>().Verify(s => s.InsertBibliographyWorks(It.Is<List<Book>>(books => books.Count == 0)), Times.Once());
            Mocker.GetMock<IRefreshBookService>().Verify(s => s.RefreshBookInfo(
                It.Is<List<Book>>(books => books.Count == 1 && books[0].Id == _book1.Id),
                It.Is<List<Book>>(books => books.Count == 1 && books[0].ForeignBookId == _book1.ForeignBookId &&
                    books[0].Editions.Value.Count == 2 && books[0].Editions.Value.Contains(ebook) &&
                    books[0].Editions.Value.Contains(audiobook)),
                It.IsAny<Author>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<DateTime?>()), Times.Once());
            Assert.That(series.LinkItems.Value, Is.EqualTo(new[] { primary }));
            Assert.That(primary.Book.Value.ForeignBookId, Is.EqualTo(_book1.ForeignBookId));
        }

        [Test]
        public void should_preserve_exact_stored_works_with_the_same_title_and_release_date()
        {
            foreach (var book in _books)
            {
                book.Title = "A shared title";
                book.ReleaseDate = new DateTime(2022, 1, 25);
                book.AuthorMetadataId = _author.AuthorMetadataId;
            }

            _remoteBooks.Clear();
            _remoteBooks.AddRange(_books.JsonClone());
            _remoteBooks.ForEach(book => book.Id = 0);
            _remoteBooks.Add(new Book
            {
                ForeignBookId = "hardcover:work:300",
                Title = _book1.Title,
                ReleaseDate = _book1.ReleaseDate
            });
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Books = _remoteBooks;
            GivenNewAuthorInfo(newAuthorInfo);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IRefreshBookService>().Verify(s => s.RefreshBookInfo(
                It.Is<List<Book>>(books => books.Count == 3 && books.Contains(_book1) && books.Contains(_book2)),
                It.Is<List<Book>>(books => books.Count == 3 && books.Exists(x => x.ForeignBookId == _book1.ForeignBookId) &&
                    books.Exists(x => x.ForeignBookId == _book2.ForeignBookId) && books.Exists(x => x.ForeignBookId == "hardcover:work:300")),
                It.IsAny<Author>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<DateTime?>()), Times.Once());
            Mocker.GetMock<IBookService>().Verify(s => s.DeleteBook(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_keep_a_work_omitted_by_the_provider_without_identity_evidence()
        {
            _remoteBooks.RemoveAt(1);
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Books = _remoteBooks;
            GivenNewAuthorInfo(newAuthorInfo);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByBook(_book2.Id)).Returns(new List<BookFile>());

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IBookService>().Verify(s => s.DeleteBook(_book2.Id, It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(
                aggregator => aggregator.PublishEvent(It.Is<BookInfoRefreshedEvent>(message => message.Removed.Count == 0)),
                Times.Once());
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
                .With(x => x.Title = "A distinct new work")
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
        public void should_keep_unlinked_local_author_without_provider_metadata()
        {
            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateAuthor(It.IsAny<Author>()), Times.Never());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ICommandResultReporter>().Verify(x => x.Report(It.IsAny<CommandResult>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_keep_local_author_with_files_without_provider_metadata()
        {
            GivenAuthorFiles();
            GivenBooksForRefresh(new List<Book>());

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateAuthor(It.IsAny<Author>()), Times.Never());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_delete_newly_created_local_author_when_foreign_id_unresolvable_and_no_files()
        {
            // Issue #272: a locally created author (synthetic/unmapped foreign id, e.g.
            // "recovery:author:RTA-001") is persisted by POST /api/v1/author and its
            // AuthorMetadataId is 0 (never provider-resolved). The post-create
            // RefreshAuthorCommand must not treat the expected not-found as a removal
            // signal and delete the row before the 201 response resolves.
            _author.AuthorMetadataId = 0;

            // Builder default foreign id ("ForeignAuthorId1") is a synthetic/unmapped id
            // (not provider-scoped), matching the locally created author this guards.
            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                  .Setup(s => s.GetAuthors(new List<int> { _author.Id }))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));

            Subject.Execute(new RefreshAuthorCommand(_author.Id, true));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_delete_local_author_on_bulk_refresh_when_foreign_id_unresolvable_and_no_files()
        {
            // Issue #272 companion: bulk refresh (queued via AuthorsImportedEvent) hits
            // the same ShouldDelete path — the local-author guard must hold there too.
            _author.AuthorMetadataId = 0;

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                  .Setup(s => s.GetAuthors(new List<int> { _author.Id }))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));

            Subject.Execute(new BulkRefreshAuthorCommand
            {
                AuthorIds = new List<int> { _author.Id },
                AreNewAuthors = true,
                Trigger = CommandTrigger.Manual
            });

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_keep_existing_provider_author_when_lookup_is_inconclusive(bool transportFailure)
        {
            _author.ForeignAuthorId = "hardcover:author:4821";
            _author.AuthorMetadataId = 1;
            Exception failure = transportFailure
                ? new MetadataProviderUnavailableException("get-author-info", _author.ForeignAuthorId, new System.Net.Http.HttpRequestException("provider unreachable"))
                : new AuthorNotFoundException(_author.ForeignAuthorId);

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.GetAuthorInfo(_author.ForeignAuthorId, true))
                .Throws(failure);

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IAuthorService>()
                .Verify(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(x => x.UpdateAuthor(It.IsAny<Author>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
            if (transportFailure)
            {
                ExceptionVerification.ExpectedErrors(1);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_report_unavailable_refresh_as_unsuccessful_and_continue(bool refreshAll)
        {
            _author.Metadata.Value.Name = "Alpha";
            var other = _author.JsonClone();
            other.Id++;
            other.Metadata = _author.Metadata.Value.JsonClone();
            other.Metadata.Value.Name = "Zulu";
            other.ForeignAuthorId = "openlibrary:author:OL9000A";
            other.AuthorMetadataId = other.Metadata.Value.Id;

            var ids = new List<int> { _author.Id, other.Id };
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAuthors(ids)).Returns(new List<Author> { _author, other });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author> { _author, other });
            Mocker.GetMock<IMetadataProviderOrchestrator>().Setup(x => x.GetAuthorInfo(_author.ForeignAuthorId, true))
                .Throws(new MetadataProviderUnavailableException("get-author-info", _author.ForeignAuthorId, new InvalidOperationException("Incomplete provider response")));
            var otherData = other.JsonClone();
            otherData.Metadata = other.Metadata.Value.JsonClone();
            otherData.Books = _remoteBooks;
            Mocker.GetMock<IMetadataProviderOrchestrator>().Setup(x => x.GetAuthorInfo(other.ForeignAuthorId, true)).Returns(otherData);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            if (refreshAll)
            {
                Subject.Execute(new RefreshAuthorCommand(null, true) { Trigger = CommandTrigger.Manual });
            }
            else
            {
                Subject.Execute(new BulkRefreshAuthorCommand(ids, true));
            }

            Mocker.GetMock<ICommandResultReporter>().Verify(x => x.Report(CommandResult.Unsuccessful), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(x => x.UpdateAuthor(It.Is<Author>(a => a.Id == other.Id)), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(x => x.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase(CatalogueIdentityRepository.LegacyLinkedEvidence)]
        [TestCase("exact-isbn-and-title-and-author")]
        public void should_keep_google_books_author_unchanged_when_outage_has_only_weaker_alternate_evidence(string evidence)
        {
            const string primaryId = "googlebooks:author:fixture-person";
            const string alternateId = "OL42A";
            _author.ForeignAuthorId = primaryId;
            var incumbent = _author.Metadata.Value;
            _author.AuthorMetadataId = incumbent.Id;
            incumbent.OpenLibraryAuthorId = alternateId;
            var previousSync = _author.LastInfoSync;
            var storedSeries = _author.Series.Value;
            var identities = Mocker.GetMock<ICatalogueIdentityRepository>();
            identities.Setup(repository => repository.GetSources(CatalogueEntityKind.Author, incumbent.Id))
                .Returns(new[] { new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = alternateId, Evidence = evidence } });
            identities.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", alternateId))
                .Returns(incumbent.Id);
            var provider = Mocker.GetMock<IMetadataProviderOrchestrator>();
            provider.Setup(orchestrator => orchestrator.GetAuthorInfo(primaryId, true))
                .Throws(new MetadataProviderUnavailableException("get-author-info", primaryId, new InvalidOperationException("Incomplete provider bibliography")));

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            provider.Verify(orchestrator => orchestrator.GetAuthorInfo(primaryId, true), Times.Once());
            provider.VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshBookService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshSeriesService>().VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorService>().Verify(service => service.UpdateAuthor(It.IsAny<Author>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(service => service.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once());
            Assert.That(_author.Metadata.Value, Is.SameAs(incumbent));
            Assert.That(_author.Series.Value, Is.SameAs(storedSeries));
            Assert.That(_author.LastInfoSync, Is.EqualTo(previousSync));
            ExceptionVerification.ExpectedWarns(1);
            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase("openlibrary:author:OL87654A", false)]
        [TestCase("OL87654A", true)]
        public void should_keep_monitored_provider_author_when_author_endpoint_returns_404(string foreignId, bool bulk)
        {
            _author.AuthorMetadataId = 1;
            _author.ForeignAuthorId = foreignId;
            _author.Monitored = true;

            Mocker.GetMock<IAuthorService>().Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.GetAuthorInfo(foreignId, true))
                .Throws(new AuthorNotFoundException(foreignId, isConfirmedMissing: true));

            if (bulk)
            {
                Subject.Execute(new BulkRefreshAuthorCommand(new List<int> { _author.Id }));
            }
            else
            {
                Subject.Execute(new RefreshAuthorCommand(_author.Id));
            }

            Mocker.GetMock<IAuthorService>()
                .Verify(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(x => x.UpdateAuthor(It.IsAny<Author>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_delete_local_author_with_positive_metadata_id_when_foreign_id_unresolvable()
        {
            // Issue #325 (DEFECT-RECOVERY-LOCAL-AUTHOR-DELETED-AFTER-CREATE): at the
            // exact head, AddAuthorService upserts an AuthorMetadata row for every
            // added author (including locally created ones with synthetic/unmapped
            // foreign ids) and assigns the resulting POSITIVE id to
            // Author.AuthorMetadataId. The old "AuthorMetadataId <= 0" guard
            // therefore no longer identifies locally created authors, and the
            // post-create RefreshAuthorCommand deleted the persisted row ~3s after
            // the 201 Created — destroying API-created data (R3
            // metadata/file-lifecycle invariant, regression of issue #272's intent).
            // The guard must key on the provider id scope, not on AuthorMetadataId:
            // a synthetic/unmapped id (e.g. "recovery:author:RTA-001") can never be
            // provider-resolvable, so its not-found is expected, never a removal
            // signal — even when the author DID get a metadata row.
            _author.AuthorMetadataId = 42;
            _author.ForeignAuthorId = "recovery:author:RTA-001";

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                  .Setup(s => s.GetAuthors(new List<int> { _author.Id }))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));

            Subject.Execute(new RefreshAuthorCommand(_author.Id, true));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_delete_local_author_with_positive_metadata_id_on_bulk_refresh()
        {
            // Issue #325 companion: the bulk-refresh path (queued via
            // AuthorsImportedEvent) hits the same ShouldDelete — the
            // provider-scope guard must hold there too for a locally created
            // author that DID get an AuthorMetadata row.
            _author.AuthorMetadataId = 42;
            _author.ForeignAuthorId = "recovery:author:RTA-001";

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                  .Setup(s => s.GetAuthors(new List<int> { _author.Id }))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));

            Subject.Execute(new BulkRefreshAuthorCommand
            {
                AuthorIds = new List<int> { _author.Id },
                AreNewAuthors = true,
                Trigger = CommandTrigger.Manual
            });

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("hardcover:author:recoveryseed")]
        [TestCase("hardcover:author:local-author-xyz")]
        [TestCase("hardcover:author:0")]
        [TestCase("hardcover:author:-7")]
        public void should_not_delete_local_author_with_non_numeric_hardcover_scoped_foreign_id_when_not_found_and_no_files(string foreignId)
        {
            // Issue #404 (DATA-LOSS regression): a locally-created author whose foreign
            // id is in the "hardcover:" scope but is NOT the resolvable numeric author
            // form (e.g. "hardcover:author:recoveryseed") can never be resolved by any
            // provider (the live app log shows "Hardcover author search returned no
            // results" -> AuthorNotFoundException). The previous gate treated ANY id
            // starting "hardcover:" as provider-owned, so the post-create refresh
            // interpreted the expected not-found as a provider-removal signal and
            // synchronously deleted the API-created Authors row (orphaning the
            // AuthorMetadata row). The gate now scopes the hardcover: prefix to the
            // numeric author form only, so a non-numeric hardcover:-scoped id is
            // locally-created and must NOT be deleted on a genuine not-found — even
            // though the author carries a positive AuthorMetadataId (AddAuthorService
            // upserts an AuthorMetadata row for every added author).
            //
            // Test case matrix:
            //   "hardcover:author:recoveryseed"  — the exact r4 reproduction id
            //   "hardcover:author:local-author-xyz" — a generic non-numeric synthetic id
            //   "hardcover:author:0"            — zero is not a valid positive provider id
            //   "hardcover:author:-7"           — negative is not a valid positive provider id
            // All four are non-resolvable synthetic ids; none may be treated as
            // provider-owned, so none may be deleted.
            _author.AuthorMetadataId = 42;
            _author.ForeignAuthorId = foreignId;

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                  .Setup(s => s.GetAuthors(new List<int> { _author.Id }))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));

            Subject.Execute(new RefreshAuthorCommand(_author.Id, true));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("hardcover:author:4821")]
        [TestCase("hardcover:author:1")]
        public void should_keep_numeric_hardcover_authors_when_lookup_returns_no_match(string foreignId)
        {
            // Hardcover can return empty results for a valid ID without confirming a removal.
            _author.AuthorMetadataId = 1;
            _author.ForeignAuthorId = foreignId;

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                  .Setup(s => s.GetAuthors(new List<int> { _author.Id }))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.GetAuthorInfo(_author.ForeignAuthorId, true))
                .Throws(new AuthorNotFoundException(_author.ForeignAuthorId));

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(_author.Id, false, false), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_delete_provider_sourced_author_when_remote_removed_but_files_exist()
        {
            _author.AuthorMetadataId = 1;
            _author.ForeignAuthorId = "openlibrary:author:OL87654A";
            GivenAuthorFiles();

            Mocker.GetMock<IAuthorService>(MockBehavior.Strict)
                  .Setup(s => s.GetAuthors(new List<int> { _author.Id }))
                  .Returns(new List<Author> { _author });

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()));

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.GetAuthorInfo(_author.ForeignAuthorId, true))
                .Throws(new AuthorNotFoundException(_author.ForeignAuthorId, isConfirmedMissing: true));

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_preserve_the_canonical_author_when_an_alternate_source_is_verified()
        {
            _author.ForeignAuthorId = "hardcover:author:42";
            _author.Metadata.Value.CanonicalId = Guid.NewGuid().ToString("N");
            _author.AuthorMetadataId = _author.Metadata.Value.Id;
            var incumbentKey = _author.Metadata.Value.CanonicalId;
            var alternate = _author.JsonClone();
            alternate.Metadata = _author.Metadata.Value.JsonClone();
            alternate.ForeignAuthorId = "openlibrary:author:OL42A";
            alternate.Metadata.Value.Id = 99;
            alternate.Metadata.Value.Overview = "A verified biography";
            alternate.Books = _remoteBooks;
            GivenNewAuthorInfo(alternate);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();
            Mocker.GetMock<ICatalogueIdentityRepository>()
                .Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL42A"))
                .Returns(_author.AuthorMetadataId);

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Assert.That(_author.Metadata.Value.CanonicalId, Is.EqualTo(incumbentKey));
            Assert.That(_author.ForeignAuthorId, Is.EqualTo("hardcover:author:42"));
            Assert.That(_author.Metadata.Value.PendingSourceAssociations, Has.Exactly(1).Matches<CatalogueSourceAssociation>(source => source.ProviderId == "OL42A"));
            Assert.That(_author.Metadata.Value.Overview, Is.EqualTo("A verified biography"));
        }

        [TestCase("OL42A", "OL42A", true, false, false, false, true)]
        [TestCase("OL42A", "OL42A", false, false, false, false, false)]
        [TestCase("OL84A", "OL42A", true, false, false, false, false)]
        [TestCase("OL42A", null, true, false, false, false, false)]
        [TestCase("OL42A", "OL42A", true, true, false, false, false)]
        [TestCase("OL42A", "OL42A", true, false, true, false, false)]
        [TestCase("OL42A", "OL42A", true, false, false, true, false)]
        public void should_add_only_an_unclaimed_work_from_a_verified_alternate_bibliography(
            string returnedAuthor, string creditedAuthor, bool ownsSource, bool existingWork, bool existingEdition, bool ownerChangesAfterLookup, bool shouldAdd)
        {
            _author.ForeignAuthorId = "hardcover:author:42";
            var metadata = _author.Metadata.Value;
            metadata.Overview = "Primary profile";
            metadata.OpenLibraryAuthorId = null;
            metadata.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, "https://example.net/portrait.jpg") };
            _author.AuthorMetadataId = metadata.Id;

            var primary = _author.JsonClone();
            primary.Metadata = metadata.JsonClone();
            primary.Books = _remoteBooks;
            GivenNewAuthorInfo(primary);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            const string alternateWorkId = "openlibrary:work:OL100W";
            const string alternateEditionId = "openlibrary:edition:OL101M";
            var alternate = new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = $"openlibrary:author:{returnedAuthor}", OpenLibraryAuthorId = returnedAuthor },
                Books = new List<Book>
                {
                    new Book
                    {
                        ForeignBookId = alternateWorkId,
                        Title = "A distinct work",
                        VerifiedOpenLibraryAuthorId = creditedAuthor,
                        Editions = new List<Edition> { new Edition { ForeignEditionId = alternateEditionId } }
                    }
                },
                Series = new List<Series>()
            };
            var currentOwner = ownsSource ? metadata.Id : metadata.Id + 1;
            var identities = Mocker.GetMock<ICatalogueIdentityRepository>();
            identities.Setup(repository => repository.GetSources(CatalogueEntityKind.Author, metadata.Id))
                .Returns(new[] { new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL42A", Evidence = AuthorCanonicalizationService.BibliographyEvidence } });
            identities.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL42A"))
                .Returns(() => currentOwner);
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(provider => provider.GetAuthorInfo("openlibrary:author:OL42A", It.IsAny<bool>()))
                .Returns(() =>
                {
                    if (ownerChangesAfterLookup)
                    {
                        currentOwner = metadata.Id + 1;
                    }

                    return alternate;
                });
            Mocker.GetMock<IMetadataProfileService>()
                .Setup(service => service.FilterBooks(alternate, _author.MetadataProfileId))
                .Returns(alternate.Books.Value);
            var service = Mocker.GetMock<IBookService>();
            service.Setup(books => books.GetBooksByAuthorMetadataId(metadata.Id)).Returns(_books);
            service.Setup(books => books.FindById(alternateWorkId))
                .Returns(existingWork ? new Book { ForeignBookId = alternateWorkId, AuthorMetadataId = metadata.Id + 1 } : null);
            Mocker.GetMock<IEditionService>()
                .Setup(editions => editions.GetEditionsForRefresh(0, It.Is<List<string>>(ids => ids.Contains(alternateEditionId))))
                .Returns(existingEdition ? new List<Edition> { new Edition { ForeignEditionId = alternateEditionId, BookId = 15 } } : new List<Edition>());

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(provider => provider.GetAuthorInfo("openlibrary:author:OL42A", It.IsAny<bool>()), ownsSource ? Times.Once() : Times.Never());
            service.Verify(books => books.InsertBibliographyWorks(It.Is<List<Book>>(items =>
                items.Exists(book => book.ForeignBookId == alternateWorkId && book.AuthorMetadataId == metadata.Id))),
                shouldAdd ? Times.Once() : Times.Never());
            Assert.That(_author.ForeignAuthorId, Is.EqualTo("hardcover:author:42"));
        }

        [TestCase(AuthorCanonicalizationService.BibliographyEvidence, null, "OL42A", true, true, true)]
        [TestCase(AuthorCanonicalizationService.BibliographyEvidence, "OL84A", "OL42A", false, false, true)]
        [TestCase(AuthorCanonicalizationService.BibliographyEvidence, null, "OL84A", true, false, true)]
        [TestCase(AuthorCanonicalizationService.BibliographyEvidence, null, "OL42A", true, false, false)]
        [TestCase(CatalogueIdentityRepository.LegacyLinkedEvidence, null, "OL42A", false, false, true)]
        public void should_fill_missing_author_profile_fields_only_from_one_corroborated_source(
            string evidence, string linkedId, string returnedId, bool readsProfile, bool acceptsProfile, bool stillOwnsSource)
        {
            _author.ForeignAuthorId = "hardcover:author:42";
            var incumbent = _author.Metadata.Value;
            incumbent.CanonicalId = Guid.NewGuid().ToString("N");
            incumbent.TitleSlug = "primary-author-slug";
            incumbent.OpenLibraryAuthorId = linkedId;
            incumbent.Overview = null;
            incumbent.Images = new List<MediaCover.MediaCover>();
            _author.AuthorMetadataId = incumbent.Id;
            var canonicalId = incumbent.CanonicalId;
            var authorName = incumbent.Name;

            var primary = _author.JsonClone();
            primary.Metadata = incumbent.JsonClone();
            primary.Books = _remoteBooks;
            GivenNewAuthorInfo(primary);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            var identities = Mocker.GetMock<ICatalogueIdentityRepository>();
            identities
                .Setup(repository => repository.GetSources(CatalogueEntityKind.Author, incumbent.Id))
                .Returns(new[] { new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL42A", Evidence = evidence } });
            identities.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL42A"))
                .Returns(stillOwnsSource ? incumbent.Id : incumbent.Id + 1);

            var portrait = new MediaCover.MediaCover(MediaCoverTypes.Poster, "https://covers.openlibrary.org/a/id/42-L.jpg")
            {
                SourceProvider = "OpenLibrary",
                SourceId = returnedId
            };
            var orchestrator = Mocker.GetMock<IMetadataProviderOrchestrator>();
            orchestrator.Setup(provider => provider.GetAuthorProfileMetadata("OpenLibrary", "OL42A"))
                .Returns(new AuthorMetadata
                {
                    Name = "Alternate provider spelling",
                    Overview = "The alternate author biography",
                    OpenLibraryAuthorId = returnedId,
                    Images = new List<MediaCover.MediaCover> { portrait }
                });

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            var refreshed = _author.Metadata.Value;
            Assert.That(refreshed.CanonicalId, Is.EqualTo(canonicalId));
            Assert.That(refreshed.ForeignAuthorId, Is.EqualTo("hardcover:author:42"));
            Assert.That(refreshed.TitleSlug, Is.EqualTo("primary-author-slug"));
            Assert.That(refreshed.Name, Is.EqualTo(authorName));
            Assert.That(refreshed.OpenLibraryAuthorId, Is.EqualTo(linkedId));
            if (acceptsProfile)
            {
                Assert.That(refreshed.Overview, Is.EqualTo("The alternate author biography"));
                Assert.That(refreshed.Images, Has.Exactly(1).Matches<MediaCover.MediaCover>(image =>
                    image.SourceProvider == "OpenLibrary" && image.SourceId == "OL42A"));
            }
            else
            {
                Assert.That(refreshed.Overview, Is.Null);
                Assert.That(refreshed.Images, Is.Empty);
            }

            orchestrator.Verify(provider => provider.GetAuthorProfileMetadata("OpenLibrary", "OL42A"), readsProfile ? Times.Once() : Times.Never());
            orchestrator.Verify(provider => provider.GetAuthorInfo("openlibrary:author:OL42A", It.IsAny<bool>()),
                readsProfile && stillOwnsSource ? Times.Once() : Times.Never());
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

        [Test]
        public void should_send_non_zero_series_payload_on_refresh_path_for_known_series_corpus()
        {
            var newAuthorInfo = _author.JsonClone();
            newAuthorInfo.Metadata = _author.Metadata.Value.JsonClone();
            newAuthorInfo.Series = new List<Series>
            {
                new Series
                {
                    ForeignSeriesId = "openlibrary:series:ol100a:earthsea",
                    Title = "Earthsea"
                }
            };

            var remoteBook = Builder<Book>.CreateNew()
                .With(x => x.ForeignBookId = "series-book-1")
                .With(x => x.SeriesLinks = new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "1",
                        SeriesPosition = 1,
                        Series = new Series
                        {
                            ForeignSeriesId = "openlibrary:series:ol100a:earthsea",
                            Title = "Earthsea"
                        }
                    }
                })
                .Build();

            newAuthorInfo.Books = new List<Book> { remoteBook };

            GivenNewAuthorInfo(newAuthorInfo);
            GivenBooksForRefresh(_books);
            AllowAuthorUpdate();

            Subject.Execute(new RefreshAuthorCommand(_author.Id));

            Mocker.GetMock<IRefreshSeriesService>()
                .Verify(x => x.RefreshSeriesInfo(
                    It.IsAny<int>(),
                    It.Is<List<Series>>(series => series != null && series.Count > 0 && series[0].Title == "Earthsea"),
                    It.IsAny<Author>(),
                    false,
                    false,
                    null), Times.Once);
        }
    }
}
