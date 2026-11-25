using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Dapper;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class CatalogueWorkReconciliationFixture : DbTest<CatalogueWorkReconciliationService, Book>
    {
        [Test]
        public void should_preserve_files_formats_monitoring_and_legacy_routes()
        {
            var author = AddAuthor(400);
            var target = AddWork(author.Id, 111, "OL411W");
            var source = AddWork(author.Id, 112, "OL411W");
            var contributor = AddAuthor(401);
            var repository = Mocker.Resolve<BookRepository>();
            repository.RecordHardcoverAuthorCredits(contributor.Id, 401, new List<string> { source.ForeignBookId });
            var related = AddWork(author.Id, 113, "OL413W");
            related.RelatedBooks = new List<int> { source.Id, target.Id };
            Db.Update(related);
            target.RelatedBooks = new List<int> { source.Id, related.Id };
            Db.Update(target);
            Db.Insert(new Blocklist
            {
                SourceTitle = "Previously blocklisted sample",
                BookIds = new List<int> { source.Id, target.Id },
                Quality = new QualityModel(Quality.EPUB),
                Date = DateTime.UtcNow
            });
            var ebook = Db.Insert(new Edition { BookId = target.Id, ForeignEditionId = "hardcover:edition:1011", Title = "Book", TitleSlug = "ebook", IsEbook = true, Monitored = true });
            var audio = Db.Insert(new Edition { BookId = source.Id, ForeignEditionId = "hardcover:edition:1012", Title = "Book", TitleSlug = "audio", Format = "Audiobook", Monitored = true });
            var epub = Db.Insert(new BookFile { Path = "/library/book.epub", EditionId = ebook.Id, Quality = new QualityModel(Quality.EPUB) });
            var m4b = Db.Insert(new BookFile { Path = "/library/book.m4b", EditionId = audio.Id, Quality = new QualityModel(Quality.M4B) });
            var series = Db.Insert(new Series { ForeignSeriesId = "hardcover:series:99", Title = "Saga" });
            Db.Insert(new SeriesBookLink { BookId = target.Id, SeriesId = series.Id, Position = "", IsPrimary = true });
            Db.Insert(new SeriesBookLink { BookId = source.Id, SeriesId = series.Id, Position = "3", SeriesPosition = 3 });

            var command = new ReconcileCatalogueWorksCommand { SourceBookId = source.Id, TargetBookId = target.Id };
            Subject.Execute(command);
            AllStoredModels.Should().HaveCount(3);

            command.DryRun = false;
            Subject.Execute(command);
            Subject.Execute(command);

            var retained = AllStoredModels.Single(book => book.Id == target.Id);
            retained.CanonicalId.Should().Be(target.CanonicalId);
            retained.IsFormatMonitored(FormatType.Ebook).Should().BeTrue();
            retained.IsFormatMonitored(FormatType.Audiobook).Should().BeTrue();
            retained.RelatedBooks.Should().ContainSingle().Which.Should().Be(related.Id);
            AllStoredModels.Single(book => book.Id == related.Id).RelatedBooks.Should().ContainSingle().Which.Should().Be(target.Id);
            Db.All<Blocklist>().Should().ContainSingle().Which.BookIds.Should().ContainSingle().Which.Should().Be(target.Id);
            Db.All<Edition>().Select(edition => edition.BookId).Should().Equal(target.Id, target.Id);
            Db.All<BookFile>().ToDictionary(file => file.Id, file => file.EditionId).Should()
                .Contain(epub.Id, ebook.Id).And.Contain(m4b.Id, audio.Id);
            Db.All<SeriesBookLink>().Should().ContainSingle().Which.Position.Should().Be("3");

            var identity = Mocker.Resolve<CatalogueIdentityRepository>();
            identity.FindByRoute(CatalogueEntityKind.Work, source.CanonicalId).Should().Be(target.Id);
            identity.FindByRoute(CatalogueEntityKind.Work, source.TitleSlug).Should().Be(target.Id);
            identity.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "112").Should().Be(target.Id);
            repository.GetSharedBooksByAuthorMetadataId(contributor.Id).Should().ContainSingle().Which.Id.Should().Be(target.Id);
        }

        [Test]
        public void should_leave_same_title_with_conflicting_work_identity_unchanged()
        {
            var author = AddAuthor(500);
            var target = AddWork(author.Id, 211, "OL411W");
            var source = AddWork(author.Id, 212, "OL412W");

            var action = () => Subject.Execute(new ReconcileCatalogueWorksCommand { SourceBookId = source.Id, TargetBookId = target.Id, DryRun = false });
            action.Should().Throw<System.InvalidOperationException>();
            AllStoredModels.Should().HaveCount(2);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_hold_shared_isbn_when_stored_open_library_claims_disagree(bool dryRun)
        {
            var author = AddAuthor(600);
            var target = AddWork(author.Id, 311, null);
            var source = AddWork(author.Id, 312, null);
            var identity = Mocker.Resolve<CatalogueIdentityRepository>();
            identity.TryAddSource(CatalogueEntityKind.Work, target.Id, "OpenLibrary", "OL611W", "primary-provider-id").Should().BeTrue();
            identity.TryAddSource(CatalogueEntityKind.Work, source.Id, "OpenLibrary", "OL612W", "primary-provider-id").Should().BeTrue();
            var targetEdition = Db.Insert(new Edition { BookId = target.Id, ForeignEditionId = "OL611M", TitleSlug = "ol611m", Title = "Book", Isbn13 = "9781442482890" });
            var sourceEdition = Db.Insert(new Edition { BookId = source.Id, ForeignEditionId = "OL612M", TitleSlug = "ol612m", Title = "Book", Isbn13 = "9781442482890" });

            var command = new ReconcileCatalogueWorksCommand { SourceBookId = source.Id, TargetBookId = target.Id, DryRun = dryRun };
            var action = () => Subject.Execute(command);

            action.Should().Throw<InvalidOperationException>().WithMessage("*conflicting Open Library work IDs*");
            AllStoredModels.Select(book => book.Id).Should().BeEquivalentTo(new[] { target.Id, source.Id });
            Db.All<Edition>().ToDictionary(edition => edition.Id, edition => edition.BookId)
                .Should().Contain(targetEdition.Id, target.Id).And.Contain(sourceEdition.Id, source.Id);
            identity.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL611W").Should().Be(target.Id);
            identity.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL612W").Should().Be(source.Id);
            identity.FindByRoute(CatalogueEntityKind.Work, target.CanonicalId).Should().Be(target.Id);
            identity.FindByRoute(CatalogueEntityKind.Work, source.CanonicalId).Should().Be(source.Id);
        }

        [TestCase("preview")]
        [TestCase("commit")]
        [TestCase("different local author")]
        [TestCase("missing authority")]
        [TestCase("competing legacy source")]
        [TestCase("different work from ISBN")]
        [TestCase("wrong author role")]
        [TestCase("stale ISBN")]
        [TestCase("rate limited")]
        [TestCase("verification omitted")]
        public void should_require_fresh_server_verified_identity_for_a_split_format_pair(string scenario)
        {
            const string isbn = "9781442482890";
            const string requestedIsbnPath = "/isbn/9781442482890.json";
            const string editionPath = "/books/OL901M.json";
            const string workPath = "/works/OL902W.json";
            var identity = Mocker.Resolve<CatalogueIdentityRepository>();
            var author = Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = "openlibrary:author:OL903A",
                TitleSlug = "openlibrary-author-ol903a",
                Name = "Bridge Author",
                SortName = "bridge author",
                NameLastFirst = "Author, Bridge",
                SortNameLastFirst = "author, bridge"
            });
            if (scenario != "missing authority")
            {
                identity.TryAddSource(CatalogueEntityKind.Author, author.Id, "OpenLibrary", "OL903A", CatalogueIdentityRepository.PrimaryProviderEvidence)
                    .Should().BeTrue();
                identity.TryAddSource(CatalogueEntityKind.Author, author.Id, "Hardcover", "904", AuthorCanonicalizationService.BibliographyEvidence)
                    .Should().BeTrue();
            }

            var source = AddWork(author.Id, 905, null);
            source.EbookMonitoringOverride = true;
            Storage.Update(source);
            var target = Storage.Insert(new Book
            {
                AuthorMetadataId = scenario == "different local author" ? AddAuthor(906).Id : author.Id,
                ForeignBookId = "openlibrary:work:OL902W",
                OpenLibraryWorkId = "OL902W",
                Title = source.Title,
                CleanTitle = source.CleanTitle,
                TitleSlug = "openlibrary-work-ol902w",
                Monitored = true,
                AudiobookMonitoringOverride = true
            });
            if (scenario == "competing legacy source")
            {
                var legacy = AddWork(author.Id, 999, null);
                using var connection = Db.OpenConnection();
                connection.Execute(
                    """UPDATE "Books" SET "ForeignBookId" = @LegacyId WHERE "Id" = @Id""",
                    new { LegacyId = "hardcover:work:000905", legacy.Id });
            }

            var ebook = Db.Insert(new Edition
            {
                BookId = source.Id,
                ForeignEditionId = "hardcover:edition:905",
                Title = "Book",
                TitleSlug = "fixture-ebook",
                Format = "Ebook",
                IsEbook = true,
                Isbn13 = "1-4424-8289-3"
            });
            var audio = Db.Insert(new Edition
            {
                BookId = target.Id,
                ForeignEditionId = "openlibrary:edition:OL907M",
                Title = "Book",
                TitleSlug = "fixture-audiobook",
                Format = "Audiobook"
            });
            var ebookFile = Db.Insert(new BookFile { Path = "/fixture/a.epub", EditionId = ebook.Id, Quality = new QualityModel(Quality.EPUB), Size = 71 });
            var audioFile = Db.Insert(new BookFile { Path = "/fixture/b.m4b", EditionId = audio.Id, Quality = new QualityModel(Quality.M4B), Size = 173, Part = 1 });
            var carriedCanonicalId = source.CanonicalId;
            var expectedCanonicalId = target.CanonicalId;

            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(settings => settings.EnableOpenLibraryProvider).Returns(true);
            config.SetupGet(settings => settings.OpenLibraryIsbnRetryBudget).Returns(2);
            config.SetupGet(settings => settings.OpenLibraryWorkRetryBudget).Returns(2);
            Mocker.SetConstant<IOpenLibraryClient>(Mocker.Resolve<OpenLibraryClient>());
            var library = Mocker.Resolve<OpenLibraryProvider>();
            var registry = Mocker.GetMock<IMetadataProviderRegistry>();
            registry.Setup(item => item.GetProviders()).Returns(new IMetadataProvider[] { library });
            registry.Setup(item => item.GetEnabledProviders()).Returns(new List<IMetadataProvider> { library });
            registry.Setup(item => item.GetProvider("OpenLibrary")).Returns(library);
            registry.Setup(item => item.GetProvidersHealthStatus())
                .Returns(new Dictionary<string, ProviderHealthStatus> { ["OpenLibrary"] = new ProviderHealthStatus() });
            Mocker.SetConstant<IMetadataProviderOrchestrator>(Mocker.Resolve<MetadataProviderOrchestrator>());

            var arrivals = new List<string>();
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request =>
            {
                arrivals.Add(request.Url.Path);
                request.Url.Host.Should().Be("openlibrary.org");
                request.Url.Scheme.Should().Be("https");
                request.AllowAutoRedirect.Should().BeFalse();
                if (request.Url.Path == requestedIsbnPath)
                {
                    return scenario == "rate limited"
                        ? new HttpResponse(request, new HttpHeader { ["Retry-After"] = "0" }, "{}", HttpStatusCode.TooManyRequests)
                        : new HttpResponse(request, new HttpHeader { ["Location"] = editionPath }, "{}", HttpStatusCode.Found);
                }

                if (request.Url.Path == editionPath)
                {
                    var returnedWork = scenario == "different work from ISBN" ? "/works/OL908W" : "/works/OL902W";
                    return new HttpResponse(request, new HttpHeader(), $$"""
                        {"key":"/books/OL901M","title":"Book","isbn_13":["{{isbn}}"],"works":[{"key":"{{returnedWork}}"}]}
                        """);
                }

                request.Url.Path.Should().Be(workPath);
                if (scenario == "stale ISBN")
                {
                    // A separate database operation while the verifier is suspended
                    // proves that provider I/O does not hold the reconciliation write lock.
                    var changed = Db.All<Edition>().Single(item => item.Id == ebook.Id);
                    changed.Isbn13 = "9780141439518";
                    Db.Update(changed);
                }

                var role = scenario == "wrong author role" ? "/type/editor_role" : "/type/author_role";
                return new HttpResponse(request, new HttpHeader(), $$"""
                    {"key":"/works/OL902W","type":{"key":"/type/work"},"title":"Book",
                      "authors":[{"author":{"key":"/authors/OL903A"},"type":{"key":"{{role}}"} }]}
                    """);
            });

            // Unknown fields are ignored by the pre-fix command schema, so this
            // fixture reaches the unchanged guard and fails for missing proof logic.
            var verify = scenario != "verification omitted";
            var dryRun = scenario == "preview";
            var command = STJson.Deserialize<ReconcileCatalogueWorksCommand>($$"""
                {"sourceBookId":{{source.Id}},"targetBookId":{{target.Id}},
                 "verifyOpenLibraryIsbnBridge":{{(verify ? "true" : "false")}},
                 "dryRun":{{(dryRun ? "true" : "false")}}}
                """);
            command.Trigger = CommandTrigger.Manual;

            var execute = () => Subject.Execute(command);
            if (scenario is "preview" or "commit")
            {
                execute.Should().NotThrow();
            }
            else
            {
                execute.Should().Throw<InvalidOperationException>();
            }

            if (scenario == "commit")
            {
                var retained = AllStoredModels.Should().ContainSingle().Which;
                retained.Id.Should().Be(target.Id);
                retained.CanonicalId.Should().Be(expectedCanonicalId);
                retained.AuthorMetadataId.Should().Be(author.Id);
                retained.IsFormatMonitored(FormatType.Ebook).Should().BeTrue();
                retained.IsFormatMonitored(FormatType.Audiobook).Should().BeTrue();
                Db.All<Edition>().Should().OnlyContain(item => item.BookId == target.Id);
                identity.FindByRoute(CatalogueEntityKind.Work, carriedCanonicalId).Should().Be(target.Id);
                identity.FindByRoute(CatalogueEntityKind.Work, source.TitleSlug).Should().Be(target.Id);
                identity.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "905").Should().Be(target.Id);
                execute.Should().NotThrow();
            }
            else
            {
                AllStoredModels.Should().HaveCount(scenario == "competing legacy source" ? 3 : 2);
                Db.All<Edition>().ToDictionary(edition => edition.Id, edition => edition.BookId)
                    .Should().Contain(ebook.Id, source.Id).And.Contain(audio.Id, target.Id);
                identity.FindByRoute(CatalogueEntityKind.Work, carriedCanonicalId).Should().Be(source.Id);
                identity.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "905").Should().Be(source.Id);
            }

            identity.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL902W").Should().Be(target.Id);
            Db.All<BookFile>().Select(file => (file.Id, file.EditionId, file.Path, file.Size, file.Part, QualityId: file.Quality.Quality.Id))
                .Should().BeEquivalentTo(new[] { ebookFile, audioFile }.Select(file =>
                    (file.Id, file.EditionId, file.Path, file.Size, file.Part, QualityId: file.Quality.Quality.Id)));
            var storedEditions = Db.All<Edition>().ToDictionary(item => item.Id);
            storedEditions[ebook.Id].Format.Should().Be("Ebook");
            storedEditions[ebook.Id].IsEbook.Should().BeTrue();
            storedEditions[audio.Id].Format.Should().Be("Audiobook");
            storedEditions[audio.Id].IsEbook.Should().BeFalse();
            storedEditions[ebook.Id].Isbn13.Should().Be(scenario == "stale ISBN" ? "9780141439518" : ebook.Isbn13);
            arrivals.Should().Equal(scenario switch
            {
                "different local author" or "missing authority" or "competing legacy source" or "verification omitted" => Array.Empty<string>(),
                "rate limited" => new[] { requestedIsbnPath },
                "different work from ISBN" => new[] { requestedIsbnPath, editionPath },
                _ => new[] { requestedIsbnPath, editionPath, workPath }
            });
        }

        private Book AddWork(int authorId, int providerId, string openLibraryId)
        {
            return Storage.Insert(new Book
            {
                AuthorMetadataId = authorId,
                ForeignBookId = $"hardcover:work:{providerId}",
                Title = "Book",
                CleanTitle = "book",
                TitleSlug = $"hardcover-work-{providerId}",
                OpenLibraryWorkId = openLibraryId,
                Monitored = true
            });
        }

        private AuthorMetadata AddAuthor(int providerId)
        {
            return Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = $"hardcover:author:{providerId}",
                TitleSlug = $"hardcover-author-{providerId}",
                Name = "Test Author",
                SortName = "test author",
                NameLastFirst = "Author, Test",
                SortNameLastFirst = "author, test"
            });
        }
    }
}
