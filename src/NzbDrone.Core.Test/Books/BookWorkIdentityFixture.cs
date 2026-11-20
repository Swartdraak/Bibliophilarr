using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class BookWorkIdentityFixture
    {
        [TestCase("Footwizard: Spellmonger, Book 13", "13", true)]
        [TestCase("Footwizard: The Spellmonger Series, Book 13", "13", true)]
        [TestCase("Footwizard: Spellmonger, Volume 13", "13", true)]
        [TestCase("Footwizard: A Spellmonger Anthology", "13", false)]
        [TestCase("Footwizard: Spellmonger, Book 13", "14", false)]
        [TestCase("Footwizard: Another Saga, Book 13", "13", false)]
        public void distinguishes_a_verified_series_suffix_from_a_title_subtitle(string title, string position, bool equivalent)
        {
            var left = BuildWork("Footwizard", "13", "hardcover:work:638246");
            var right = BuildWork(title, position, "hardcover:work:2939037");

            BookWorkIdentity.AreCorroborated(left, right).Should().Be(equivalent);
        }

        [TestCase("hardcover-original-book-mapping", true)]
        [TestCase("unverified-hint", false)]
        public void requires_provider_declared_evidence_to_match_an_original_hardcover_id(string evidence, bool equivalent)
        {
            var canonical = new Book { AuthorMetadataId = 77, Title = "Victory Soup", ForeignBookId = "hardcover:work:2483337" };
            var retired = new Book { AuthorMetadataId = 77, Title = "Victory Soup", ForeignBookId = "hardcover:work:1379870" };
            canonical.PendingSourceAssociations.Add(new CatalogueSourceAssociation
            {
                Provider = "Hardcover",
                ProviderId = "1379870",
                Evidence = evidence
            });

            BookWorkIdentity.AreCorroborated(canonical, retired).Should().Be(equivalent);
            retired.AuthorMetadataId = 88;
            BookWorkIdentity.AreCorroborated(canonical, retired).Should().BeFalse();
        }

        [TestCase("9780141439518", "0141439513", true)]
        [TestCase("9780141439510", "9780141439510", false)]
        public void requires_a_valid_edition_isbn_to_match_works(string leftIsbn, string rightIsbn, bool equivalent)
        {
            var left = new Book
            {
                AuthorMetadataId = 77,
                Title = "Same title",
                ForeignBookId = "hardcover:work:1",
                Editions = new List<Edition> { new Edition { Isbn13 = leftIsbn } }
            };
            var right = new Book
            {
                AuthorMetadataId = 77,
                Title = "Same title",
                ForeignBookId = "openlibrary:work:OL1W",
                Editions = new List<Edition> { new Edition { Isbn13 = rightIsbn } }
            };

            BookWorkIdentity.AreCorroborated(left, right).Should().Be(equivalent);
        }

        private static Book BuildWork(string title, string position, string providerId)
        {
            return new Book
            {
                AuthorMetadataId = 77,
                Title = title,
                ForeignBookId = providerId,
                SeriesLinks = new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        IsPrimary = true,
                        Position = position,
                        Series = new Series { ForeignSeriesId = "hardcover:series:950", Title = "The Spellmonger" }
                    }
                }
            };
        }
    }
}
