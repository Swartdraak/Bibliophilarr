using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
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
