using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class MonitorNewBookServiceFixture : CoreTest<MonitorNewBookService>
    {
        private List<Book> _books;

        [SetUp]
        public void Setup()
        {
            _books = Builder<Book>.CreateListOfSize(4)
                .All()
                .With(e => e.Monitored = true)
                .With(e => e.ReleaseDate = DateTime.UtcNow.AddDays(-7))

                //Future
                .TheFirst(1)
                .With(e => e.ReleaseDate = DateTime.UtcNow.AddDays(7))

                //Future/TBA
                .TheNext(1)
                .With(e => e.ReleaseDate = null)
                .Build()
                .ToList();
        }

        [Test]
        public void should_monitor_with_all()
        {
            foreach (var book in _books)
            {
                Subject.ShouldMonitorNewBook(book, _books, NewItemMonitorTypes.All).Should().BeTrue();
            }
        }

        [Test]
        public void should_not_monitor_with_none()
        {
            foreach (var book in _books)
            {
                Subject.ShouldMonitorNewBook(book, _books, NewItemMonitorTypes.None).Should().BeFalse();
            }
        }

        [Test]
        public void should_only_monitor_new_with_new()
        {
            Subject.ShouldMonitorNewBook(_books[0], _books, NewItemMonitorTypes.New).Should().BeTrue();

            foreach (var book in _books.Skip(1))
            {
                Subject.ShouldMonitorNewBook(book, _books, NewItemMonitorTypes.New).Should().BeFalse();
            }
        }

        [Test]
        public void should_not_throw_and_should_default_to_no_monitor_when_monitor_type_is_out_of_range()
        {
            // Regression (issue #256): an out-of-range NewItemMonitorTypes value (e.g. 3, a
            // MonitorTypes value mistakenly stored in a NewItemMonitorTypes column) must NOT
            // throw NotImplementedException — that exception was caught upstream and only
            // logged, silently dropping every new book on import/refresh. It must log a
            // warning and fall back to the safe "do not auto-monitor" default.
            var outOfRange = (NewItemMonitorTypes)3;

            var result = Subject.ShouldMonitorNewBook(_books[0], _books, outOfRange);

            result.Should().BeFalse("out-of-range values must fall back to the safe no-monitor default");
        }

        [Test]
        public void should_not_throw_and_should_default_to_no_monitor_when_monitor_type_is_negative()
        {
            var negative = (NewItemMonitorTypes)(-1);

            Subject.ShouldMonitorNewBook(_books[0], _books, negative).Should().BeFalse();
        }

        [Test]
        public void should_not_throw_when_monitor_type_exceeds_defined_range()
        {
            // A value above the highest defined member (New=2) is also out of range.
            var tooLarge = (NewItemMonitorTypes)99;

            Subject.ShouldMonitorNewBook(_books[0], _books, tooLarge).Should().BeFalse();
        }
    }
}
