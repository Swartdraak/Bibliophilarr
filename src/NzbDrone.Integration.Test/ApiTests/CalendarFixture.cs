using System;
using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Books;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class CalendarFixture : CatalogueIntegrationTest
    {
        public ClientBase<BookResource> Calendar;

        protected override void InitRestClients()
        {
            base.InitRestClients();

            Calendar = new ClientBase<BookResource>(RestClient, ApiKey, "calendar");
        }

        [Test]
        public void should_be_able_to_get_books()
        {
            var book = SeedCatalogueBook(true);

            var request = Calendar.BuildRequest();
            request.AddParameter("start", new DateTime(2020, 02, 01).ToString("s") + "Z");
            request.AddParameter("end", new DateTime(2020, 02, 28).ToString("s") + "Z");
            var items = Calendar.Get<List<BookResource>>(request);

            items = items.Where(v => v.AuthorId == book.AuthorId).ToList();

            items.Should().HaveCount(1);
            items.First().Title.Should().Be(CatalogueTitle);
        }

        [Test]
        public void should_not_be_able_to_get_unmonitored_books()
        {
            var book = SeedCatalogueBook(false);

            var request = Calendar.BuildRequest();
            request.AddParameter("start", new DateTime(2020, 02, 01).ToString("s") + "Z");
            request.AddParameter("end", new DateTime(2020, 02, 28).ToString("s") + "Z");
            request.AddParameter("unmonitored", "false");
            var items = Calendar.Get<List<BookResource>>(request);

            items = items.Where(v => v.AuthorId == book.AuthorId).ToList();

            items.Should().BeEmpty();
        }

        [Test]
        public void should_be_able_to_get_unmonitored_books()
        {
            var book = SeedCatalogueBook(false);

            var request = Calendar.BuildRequest();
            request.AddParameter("start", new DateTime(2020, 02, 01).ToString("s") + "Z");
            request.AddParameter("end", new DateTime(2020, 02, 28).ToString("s") + "Z");
            request.AddParameter("unmonitored", "true");
            var items = Calendar.Get<List<BookResource>>(request);

            items = items.Where(v => v.AuthorId == book.AuthorId).ToList();

            items.Should().HaveCount(1);
            items.First().Title.Should().Be(CatalogueTitle);
        }
    }
}
