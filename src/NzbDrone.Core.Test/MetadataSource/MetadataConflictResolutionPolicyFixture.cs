using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataConflictResolutionPolicyFixture : CoreTest<MetadataConflictResolutionPolicy>
    {
        [Test]
        public void should_select_highest_quality_candidate()
        {
            var decision = Subject.ResolveBookConflict(new List<MetadataProviderBookCandidate>
            {
                BuildCandidate("Inventaire", 74, true),
                BuildCandidate("GoogleBooks", 88, false)
            });

            decision.SelectedProvider.Should().Be("GoogleBooks");
            decision.ResolutionReason.Should().Be("quality-score");
            decision.TieBreakReason.Should().BeNull();
            decision.ProviderScores["GoogleBooks"].Should().Be(88);
        }

        [Test]
        public void should_prefer_cover_then_provider_precedence_when_scores_tie()
        {
            var decision = Subject.ResolveBookConflict(new List<MetadataProviderBookCandidate>
            {
                BuildCandidate("GoogleBooks", 90, true),
                BuildCandidate("Inventaire", 90, true)
            });

            decision.SelectedProvider.Should().Be("Inventaire");
            decision.ResolutionReason.Should().Be("tie-break");
            decision.TieBreakReason.Should().Be("cover-availability-then-provider-precedence");
            decision.UsedProviderPrecedence.Should().BeTrue();
            decision.SelectedHasCover.Should().BeTrue();
        }

        [Test]
        public void should_honor_preferred_provider_when_available()
        {
            var decision = Subject.ResolveBookConflict(new List<MetadataProviderBookCandidate>
            {
                BuildCandidate("Inventaire", 70, true),
                BuildCandidate("GoogleBooks", 95, true)
            }, preferredProvider: "Inventaire");

            decision.SelectedProvider.Should().Be("Inventaire");
            decision.ResolutionReason.Should().Be("preferred-provider");
        }

        [Test]
        public void should_expose_observability_fields_for_empty_candidates()
        {
            var decision = Subject.ResolveBookConflict(new List<MetadataProviderBookCandidate>());

            decision.SelectedBook.Should().BeNull();
            decision.SelectedProvider.Should().BeNull();
            decision.ResolutionReason.Should().Be("no-candidates");
            decision.EvaluatedProviders.Should().BeEmpty();
            decision.ProviderScores.Should().BeEmpty();
        }

        [Test]
        public void should_use_experimental_tie_break_only_when_feature_flag_is_enabled()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableMetadataConflictStrategyVariants)
                .Returns(true);

            var decision = Subject.ResolveBookConflict(new List<MetadataProviderBookCandidate>
            {
                BuildCandidate("GoogleBooks", 90, true),
                BuildCandidate("Inventaire", 90, false)
            });

            decision.SelectedProvider.Should().Be("Inventaire");
            decision.ResolutionReason.Should().Be("tie-break");
            decision.TieBreakReason.Should().Be("experimental-provider-precedence-only");
            decision.UsedProviderPrecedence.Should().BeTrue();
        }

        [Test]
        public void should_apply_field_precedence_matrix_for_overlapping_provider_fields()
        {
            var inventaire = BuildCandidate("Inventaire", 90, true);
            inventaire.Book.Title = "Dune (Inventaire)";
            inventaire.Book.AuthorMetadata.Value.ForeignAuthorId = "inv:author:1";
            inventaire.Book.ReleaseDate = null;
            inventaire.Book.Editions.Value[0].Language = "fr";
            inventaire.Book.Editions.Value[0].Disambiguation = string.Empty;
            inventaire.Book.Editions.Value[0].Isbn13 = string.Empty;

            var openLibrary = BuildCandidate("OpenLibrary", 90, true);
            openLibrary.Book.Title = "Dune (OpenLibrary)";
            openLibrary.Book.AuthorMetadata.Value.ForeignAuthorId = "ol:author:1";
            openLibrary.Book.ReleaseDate = new System.DateTime(1965, 8, 1);
            openLibrary.Book.Editions.Value[0].Language = "en";
            openLibrary.Book.Editions.Value[0].Disambiguation = "Ace edition";
            openLibrary.Book.Editions.Value[0].Isbn13 = "9780441172719";

            var decision = Subject.ResolveBookConflict(new List<MetadataProviderBookCandidate>
            {
                inventaire,
                openLibrary
            });

            decision.FieldSelections["title"].Should().Be("Inventaire");
            decision.FieldSelections["subtitle"].Should().Be("OpenLibrary");
            decision.FieldSelections["author-identity"].Should().Be("Inventaire");
            decision.FieldSelections["identifiers"].Should().Be("OpenLibrary");
            decision.FieldSelections["publication-date"].Should().Be("OpenLibrary");
            decision.FieldSelections["language"].Should().Be("OpenLibrary");
            decision.FieldSelections["cover-links"].Should().Be("Inventaire");
        }

        [Test]
        public void should_fallback_to_selected_provider_when_identifier_values_are_missing()
        {
            var inventaire = BuildCandidate("Inventaire", 90, true);
            var openLibrary = BuildCandidate("OpenLibrary", 90, true);

            inventaire.Book.ForeignBookId = null;
            inventaire.Book.Editions.Value[0].Isbn13 = null;
            inventaire.Book.Editions.Value[0].Asin = null;

            openLibrary.Book.ForeignBookId = null;
            openLibrary.Book.Editions.Value[0].Isbn13 = null;
            openLibrary.Book.Editions.Value[0].Asin = null;

            var decision = Subject.ResolveBookConflict(new List<MetadataProviderBookCandidate>
            {
                inventaire,
                openLibrary
            });

            decision.SelectedProvider.Should().Be("OpenLibrary");
            decision.FieldSelections["identifiers"].Should().Be("OpenLibrary");
        }

        private static MetadataProviderBookCandidate BuildCandidate(string provider, int score, bool withCover)
        {
            var book = new Book
            {
                ForeignBookId = $"{provider}:book",
                Title = $"{provider} Book",
                CleanTitle = $"{provider} Book",
                AuthorMetadata = new AuthorMetadata
                {
                    ForeignAuthorId = $"{provider}:author",
                    Name = "Author",
                    SortName = "Author",
                    NameLastFirst = "Author",
                    SortNameLastFirst = "Author"
                },
                Ratings = new Ratings()
            };

            var edition = new Edition
            {
                ForeignEditionId = $"{provider}:edition",
                Title = "Edition",
                Ratings = new Ratings(),
                Book = book
            };

            if (withCover)
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = "https://cover.example/test.jpg",
                    CoverType = MediaCoverTypes.Cover
                });
            }

            book.Editions = new List<Edition> { edition };

            return new MetadataProviderBookCandidate
            {
                ProviderName = provider,
                Book = book,
                QualityScore = score
            };
        }
    }
}
