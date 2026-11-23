using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_catalogue_identityFixture : MigrationTest<add_catalogue_identity>
    {
        [Test]
        public void should_backfill_sources_and_routes_without_rekeying_catalogue()
        {
            var db = WithMigrationTestDb(migration =>
            {
                migration.Execute.Sql("""
                    INSERT INTO "AuthorMetadata" ("Id", "ForeignAuthorId", "Name", "TitleSlug", "Status", "Images", "Aliases", "OpenLibraryAuthorId", "SortName", "NameLastFirst", "SortNameLastFirst")
                    VALUES (21, 'hardcover:author:507', 'Test Author', 'hardcover-author-507', 0, '[]', '[]', 'ol710a', 'test author', 'Author, Test', 'author test');
                    INSERT INTO "Books" ("Id", "AuthorMetadataId", "ForeignBookId", "TitleSlug", "Title", "CleanTitle", "Monitored", "AnyEditionOk", "OpenLibraryWorkId")
                    VALUES (25, 21, 'hardcover:work:905', 'hardcover-work-905', 'Test Work', 'testwork', TRUE, TRUE, 'ol720w'),
                           (26, 21, 'hardcover:work:906', 'secondary-copy', 'Another Work', 'anotherwork', FALSE, TRUE, 'ol721w'),
                           (27, 21, 'openlibrary:work:OL721W', 'primary-copy', 'Another Work', 'anotherwork', TRUE, TRUE, NULL),
                           (28, 21, 'openlibrary:edition:OL333M', 'edition-fallback', 'Edition Fallback', 'editionfallback', FALSE, TRUE, NULL);
                    INSERT INTO "Series" ("Id", "ForeignSeriesId", "Title", "Numbered", "WorkCount", "PrimaryWorkCount")
                    VALUES (31, 'hardcover:series:3001', 'Test Series', TRUE, 1, 1);
                    INSERT INTO "SeriesBookLink" ("SeriesId", "BookId", "Position", "IsPrimary")
                    VALUES (31, 25, '1', TRUE);
                    """);
            });

            var identity = Mocker.Resolve<CatalogueIdentityRepository>();
            identity.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL710A").Should().Be(21);
            identity.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "hardcover:work:905").Should().Be(25);
            identity.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL720W").Should().Be(25);
            identity.FindByProviderId(CatalogueEntityKind.Series, "Hardcover", "3001").Should().Be(31);
            identity.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL721W").Should().BeNull();
            identity.GetSources(CatalogueEntityKind.Work, 28).Should().BeEmpty();

            var bookIds = db.Query<Book>("""
                SELECT "Id", "CanonicalId" FROM "Books" ORDER BY "Id"
                """);
            bookIds.Select(book => book.Id).Should().Equal(25, 26, 27, 28);
            bookIds.Select(book => book.CanonicalId).Should().OnlyHaveUniqueItems()
                .And.OnlyContain(value => value != null && value.Length == 32);
            identity.FindByRoute(CatalogueEntityKind.Work, bookIds[0].CanonicalId).Should().Be(25);
            identity.FindByRoute(CatalogueEntityKind.Work, "hardcover-work-905").Should().Be(25);
            identity.FindByRoute(CatalogueEntityKind.Work, "hardcover:work:905").Should().Be(25);
            identity.FindByLegacyProviderId(CatalogueEntityKind.Work, "ol720w").Should().Be(25);
            identity.FindByRoute(CatalogueEntityKind.Work, "openlibrary:work:OL721W").Should().BeNull();
            identity.FindByRoute(CatalogueEntityKind.Author, "hardcover-author-507").Should().Be(21);
            db.Query<SeriesBookLink>("""
                SELECT "SeriesId", "BookId", "Position" FROM "SeriesBookLink"
                """).Should().ContainSingle(link => link.SeriesId == 31 && link.BookId == 25 && link.Position == "1");
        }

        [TestCase("Hardcover", "905", CatalogueIdentityRepository.HardcoverWorkMappingEvidence, true, false, false, TestName = "should_reconcile_a_single_legacy_work_into_the_verified_owner")]
        [TestCase("Hardcover", "905", CatalogueIdentityRepository.HardcoverWorkMappingEvidence, true, true, false, TestName = "should_refuse_reversing_a_verified_remap")]
        [TestCase("Hardcover", "905", CatalogueIdentityRepository.HardcoverWorkMappingEvidence, false, false, false, TestName = "should_refuse_a_remap_with_two_suppressed_legacy_owners")]
        [TestCase("Hardcover", "906", CatalogueIdentityRepository.HardcoverWorkMappingEvidence, true, false, false, TestName = "should_refuse_a_verified_remap_for_a_different_primary")]
        [TestCase("GoogleBooks", "905", CatalogueIdentityRepository.HardcoverWorkMappingEvidence, true, false, false, TestName = "should_refuse_a_remap_from_a_different_provider")]
        [TestCase("Hardcover", "905", "corroborated-work-match", true, false, false, TestName = "should_refuse_an_unverified_remap_claim")]
        [TestCase("Hardcover", "905", CatalogueIdentityRepository.HardcoverWorkMappingEvidence, true, false, true, TestName = "should_refuse_conflicting_open_library_works_with_a_shared_untyped_isbn")]
        public void should_require_directional_and_unambiguous_persisted_remap(
            string provider,
            string providerId,
            string evidence,
            bool removeOtherLegacyRow,
            bool reverse,
            bool conflictingOpenLibrary)
        {
            var database = WithTestDb(new MigrationContext(MigrationType)
            {
                BeforeMigration = migration =>
                {
                    if (migration is add_catalogue_identity)
                    {
                        migration.Execute.Sql("""
                            INSERT INTO "AuthorMetadata" ("Id", "ForeignAuthorId", "Name", "TitleSlug", "Status", "Images", "Aliases", "SortName", "NameLastFirst", "SortNameLastFirst")
                            VALUES (21, 'hardcover:author:507', 'Test Author', 'hardcover-author-507', 0, '[]', '[]', 'test author', 'Author, Test', 'author test');
                            INSERT INTO "Books" ("Id", "AuthorMetadataId", "ForeignBookId", "TitleSlug", "Title", "CleanTitle", "Monitored", "AnyEditionOk")
                            VALUES (25, 21, 'hardcover:work:905', 'original-one', 'Test Work', 'testwork', TRUE, TRUE),
                                   (26, 21, 'HardCover:work:00905', 'original-two', 'Test Work', 'testwork', TRUE, TRUE),
                                   (27, 21, 'hardcover:work:987', 'canonical', 'Test Work', 'testwork', TRUE, TRUE);
                            """);
                    }
                }
            });

            var identity = Mocker.Resolve<CatalogueIdentityRepository>();
            identity.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "905").Should().BeNull();
            var books = database.All<Book>().ToDictionary(book => book.Id);
            var canonical = books[27];
            var original = books[25];
            if (removeOtherLegacyRow)
            {
                database.Delete(books[26]);
            }

            identity.TryAddSource(CatalogueEntityKind.Work, canonical.Id, provider, providerId, evidence).Should().BeTrue();
            identity.FindByProviderId(CatalogueEntityKind.Work, provider, providerId).Should().Be(canonical.Id);

            var ebook = database.Insert(new Edition { BookId = canonical.Id, ForeignEditionId = "hardcover:edition:981", Title = "Book", TitleSlug = "ebook", IsEbook = true });
            var audio = database.Insert(new Edition { BookId = original.Id, ForeignEditionId = "hardcover:edition:951", Title = "Book", TitleSlug = "audio", Format = "Audiobook" });
            var ebookFile = database.Insert(new BookFile { Path = "/synthetic-book/a.epub", EditionId = ebook.Id, Quality = new QualityModel(Quality.EPUB) });
            var audioFile = database.Insert(new BookFile { Path = "/synthetic-book/b.m4b", EditionId = audio.Id, Quality = new QualityModel(Quality.M4B) });
            if (conflictingOpenLibrary)
            {
                canonical.OpenLibraryWorkId = "OL501W";
                original.OpenLibraryWorkId = "OL502W";
                database.Update(canonical);
                database.Update(original);
                database.Insert(new Edition { BookId = canonical.Id, ForeignEditionId = "hardcover:edition:982", Title = "Book", TitleSlug = "reference-a", Isbn13 = "9780141439518" });
                database.Insert(new Edition { BookId = original.Id, ForeignEditionId = "hardcover:edition:952", Title = "Book", TitleSlug = "reference-b", Isbn13 = "9780141439518" });
            }

            var service = Mocker.Resolve<CatalogueWorkReconciliationService>();
            var command = new ReconcileCatalogueWorksCommand
            {
                SourceBookId = reverse ? canonical.Id : original.Id,
                TargetBookId = reverse ? original.Id : canonical.Id
            };
            var allowed = provider == "Hardcover" && providerId == "905" &&
                          evidence == CatalogueIdentityRepository.HardcoverWorkMappingEvidence &&
                          removeOtherLegacyRow && !reverse && !conflictingOpenLibrary;
            var action = () => service.Execute(command);

            if (allowed)
            {
                action.Should().NotThrow();
                database.All<Book>().Should().HaveCount(2);
                command.DryRun = false;
                action.Should().NotThrow();

                database.All<Book>().Should().ContainSingle().Which.CanonicalId.Should().Be(canonical.CanonicalId);
                database.All<Edition>().Should().OnlyContain(edition => edition.BookId == canonical.Id);
                identity.FindByRoute(CatalogueEntityKind.Work, original.CanonicalId).Should().Be(canonical.Id);
            }
            else
            {
                action.Should().Throw<InvalidOperationException>();
                command.DryRun = false;
                action.Should().Throw<InvalidOperationException>();

                database.All<Book>().Should().HaveCount(removeOtherLegacyRow ? 2 : 3);
                database.All<Edition>().Single(edition => edition.Id == audio.Id).BookId.Should().Be(original.Id);
                identity.FindByRoute(CatalogueEntityKind.Work, original.CanonicalId).Should().Be(original.Id);
            }

            identity.FindByProviderId(CatalogueEntityKind.Work, provider, providerId).Should().Be(canonical.Id);
            database.All<BookFile>().ToDictionary(file => file.Id, file => file.EditionId).Should()
                .Contain(ebookFile.Id, ebook.Id).And.Contain(audioFile.Id, audio.Id);
        }

        [Test]
        public void should_keep_public_identity_and_bookmarks_when_a_provider_changes()
        {
            WithMigrationTestDb();
            var repository = Mocker.Resolve<CatalogueIdentityRepository>();
            var database = Mocker.Resolve<IMainDatabase>();
            var books = new BasicRepository<Book>(database, Mocker.Resolve<IEventAggregator>());
            var book = new Book
            {
                ForeignBookId = "hardcover:work:42",
                Title = "Test Work",
                CleanTitle = "testwork",
                TitleSlug = "original-provider-slug",
                CanonicalId = "12345678123456781234567812345678"
            };
            book.PendingSourceAssociations.Add(new CatalogueSourceAssociation
            {
                Provider = "OpenLibrary",
                ProviderId = "ol5w",
                Evidence = "exact-isbn-and-title-and-author"
            });
            books.Insert(book);

            var original = book.CanonicalId;
            original.Should().NotBe("12345678123456781234567812345678");
            repository.GetSources(CatalogueEntityKind.Work, book.Id).Should().HaveCount(2);
            repository.TryAddSource(CatalogueEntityKind.Work, book.Id, "OpenLibrary", "OL5W", "exact-isbn-and-title-and-author").Should().BeTrue();

            book.ForeignBookId = "openlibrary:work:OL6W";
            book.TitleSlug = "new-provider-slug";
            book.CanonicalId = "12345678123456781234567812345678";
            books.Update(book);
            books.Get(book.Id).CanonicalId.Should().Be(original);
            book.CanonicalId.Should().Be(original);
            repository.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL6W").Should().Be(book.Id);
            repository.FindByRoute(CatalogueEntityKind.Work, "original-provider-slug").Should().Be(book.Id);
            repository.FindByRoute(CatalogueEntityKind.Work, "new-provider-slug").Should().Be(book.Id);
            repository.FindByRoute(CatalogueEntityKind.Work, original.ToUpperInvariant()).Should().Be(book.Id);
            repository.TryAddRouteAlias(CatalogueEntityKind.Work, book.Id, "9999999999999999999999999999999a").Should().BeTrue();
            repository.FindByRoute(CatalogueEntityKind.Work, "9999999999999999999999999999999A").Should().Be(book.Id);
            repository.GetSources(CatalogueEntityKind.Work, book.Id).Should().HaveCount(3);
        }

        [Test]
        public void should_roll_back_new_row_when_a_corroborated_source_belongs_to_another_work()
        {
            WithMigrationTestDb();
            var repository = Mocker.Resolve<CatalogueIdentityRepository>();
            var books = new BasicRepository<Book>(Mocker.Resolve<IMainDatabase>(), Mocker.Resolve<IEventAggregator>());
            var owner = books.Insert(new Book { ForeignBookId = "openlibrary:work:OL5W", Title = "Owner", CleanTitle = "owner", TitleSlug = "owner" });
            var conflicting = new Book { ForeignBookId = "hardcover:work:31", Title = "Conflict", CleanTitle = "conflict", TitleSlug = "conflict" };
            conflicting.PendingSourceAssociations.Add(new CatalogueSourceAssociation
            {
                Provider = "OpenLibrary",
                ProviderId = "OL5W",
                Evidence = "corroborated-id"
            });

            var insert = () => books.Insert(conflicting);
            insert.Should().Throw<InvalidOperationException>();
            books.Count().Should().Be(1);
            conflicting.Id.Should().Be(0);
            conflicting.CanonicalId.Should().BeNull();
            repository.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL5W").Should().Be(owner.Id);
            repository.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "31").Should().BeNull();
            repository.FindByRoute(CatalogueEntityKind.Work, "conflict").Should().BeNull();
        }

        [Test]
        public void should_preserve_another_works_source_without_discarding_normal_refresh()
        {
            WithMigrationTestDb();
            var repository = Mocker.Resolve<CatalogueIdentityRepository>();
            var books = new BasicRepository<Book>(Mocker.Resolve<IMainDatabase>(), Mocker.Resolve<IEventAggregator>());
            var owner = books.Insert(new Book { ForeignBookId = "openlibrary:work:OL5W", Title = "Owner", CleanTitle = "owner", TitleSlug = "owner" });
            var secondary = books.Insert(new Book { ForeignBookId = "hardcover:work:41", Title = "Secondary", CleanTitle = "secondary", TitleSlug = "secondary" });
            secondary.Title = "Updated Secondary";
            secondary.PendingSourceAssociations.Add(new CatalogueSourceAssociation
            {
                Provider = "OpenLibrary",
                ProviderId = "OL5W",
                Evidence = "corroborated-id"
            });
            books.Update(secondary);

            books.Get(secondary.Id).Title.Should().Be("Updated Secondary");
            repository.FindByLegacyProviderId(CatalogueEntityKind.Work, "OL5W").Should().Be(owner.Id);
            repository.GetSources(CatalogueEntityKind.Work, secondary.Id).Should().ContainSingle();

            secondary.ForeignBookId = "openlibrary:work:OL5W";
            var update = () => books.Update(secondary);
            update.Should().Throw<InvalidOperationException>();
            books.Get(secondary.Id).ForeignBookId.Should().Be("hardcover:work:41");
        }
    }
}
