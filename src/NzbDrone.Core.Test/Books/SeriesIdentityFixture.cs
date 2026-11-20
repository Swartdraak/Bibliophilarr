using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class SeriesIdentityFixture
    {
        [TestCase("openlibrary:series:ol5a:saga", "2", true, true)]
        [TestCase("openlibrary:series:ol5a:saga", "3", true, false)]
        [TestCase("openlibrary:series:ol5a:saga", "2", false, false)]
        [TestCase("hardcover:series:22", "2", true, false)]
        public void requires_independently_matching_member_positions(string providerId, string secondPosition, bool secondPrimary, bool matches)
        {
            var existing = new Series
            {
                Title = "The Saga",
                ForeignSeriesId = "hardcover:series:11",
                LinkItems = new List<SeriesBookLink>
                {
                    new SeriesBookLink { BookId = 1, Position = "1", IsPrimary = true },
                    new SeriesBookLink { BookId = 2, Position = "2", IsPrimary = true }
                }
            };
            var incoming = new Series
            {
                Title = "The Saga",
                ForeignSeriesId = providerId,
                LinkItems = new List<SeriesBookLink>
                {
                    new SeriesBookLink { Book = new Book { Id = 1 }, Position = "1", IsPrimary = true },
                    new SeriesBookLink { Book = new Book { Id = 2 }, Position = secondPosition, IsPrimary = secondPrimary }
                }
            };

            RefreshSeriesService.SeriesIdentityMatches(existing, incoming).Should().Be(matches);
        }
    }
}
