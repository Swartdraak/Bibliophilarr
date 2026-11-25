using System.Collections.Generic;
using System.Threading;
using Bibliophilarr.Api.V1.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Inventaire;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Api.Test.Search
{
    [TestFixture]
    public class InventaireSeriesPreviewFixture
    {
        private const string SeriesId = "inventaire:series:wd:Q41";
        private const string Term = "Fixture series";

        [Test]
        public void should_return_read_only_member_data_and_never_call_the_hardcover_member_lookup()
        {
            var original = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            var inventaire = new Mock<IInventaireSeriesDiscovery>(MockBehavior.Strict);
            var row = new InventaireSeriesMember
            {
                ForeignWorkId = "inventaire:work:wd:Q42",
                Title = "Fixture member",
                SourceUrl = "https://inventaire.io/entity/wd:Q42"
            };
            inventaire.Setup(provider => provider.GetMembers(SeriesId, CancellationToken.None)).Returns(new List<InventaireSeriesMember> { row });

            var result = Controller(original, inventaire).Search(SeriesId, "series-books");

            var preview = result.Should().BeOfType<List<SearchResource>>().Which.Should().ContainSingle().Which;
            preview.SeriesMemberPreview.Should().BeSameAs(row);
            preview.Book.Should().BeNull();
            preview.Author.Should().BeNull();
            preview.CatalogueOwnerId.Should().BeNull();
            original.VerifyNoOtherCalls();
        }

        [Test]
        public void should_use_inventaire_only_when_hardcover_is_unavailable_and_no_saved_match_was_found()
        {
            var original = new Mock<IMetadataProviderOrchestrator>();
            var inventaire = new Mock<IInventaireSeriesDiscovery>();
            inventaire.Setup(provider => provider.Search(Term, CancellationToken.None)).Returns(new List<SeriesSearchSummary>
            {
                new SeriesSearchSummary { ForeignSeriesId = SeriesId, Title = Term, SourceProvider = "Inventaire" }
            });
            var controller = Controller(original, inventaire);

            original.Setup(provider => provider.SearchForNewSeries(Term)).Returns(new List<SeriesSearchSummary>());
            controller.Search(Term, "series").Should().BeOfType<List<SearchResource>>().Which.Should().BeEmpty();
            inventaire.VerifyNoOtherCalls();

            original.Setup(provider => provider.SearchForNewSeries(Term)).Returns((List<SeriesSearchSummary>)null);
            var result = controller.Search(Term, "series").Should().BeOfType<List<SearchResource>>().Which.Should().ContainSingle().Which;
            result.Series.ForeignSeriesId.Should().Be(SeriesId);
            result.Series.SourceProvider.Should().Be("Inventaire");
            inventaire.Verify(provider => provider.Search(Term, CancellationToken.None), Times.Once);
        }

        [Test]
        public void should_refuse_an_invalid_provider_scoped_series_before_transport()
        {
            var original = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            var inventaire = new Mock<IInventaireSeriesDiscovery>(MockBehavior.Strict);

            Controller(original, inventaire).Search("inventaire:series:wd:Q0", "series-books").Should().BeOfType<BadRequestObjectResult>();
            original.VerifyNoOtherCalls();
            inventaire.VerifyNoOtherCalls();
        }

        private static SearchController Controller(Mock<IMetadataProviderOrchestrator> original, Mock<IInventaireSeriesDiscovery> inventaire)
        {
            return new SearchController(
                original.Object,
                Mock.Of<IBuildFileNames>(),
                Mock.Of<IMapCoversToLocal>(),
                Mock.Of<ISearchTelemetryService>(),
                Mock.Of<ICatalogueIdentityRepository>(),
                Mock.Of<ISeriesRepository>(),
                Mock.Of<IAuthorRepository>(),
                Mock.Of<IBookRepository>(),
                inventaire.Object);
        }
    }
}
