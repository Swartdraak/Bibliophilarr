using System;
using System.Collections.Generic;
using System.Text.Json;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Http;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.AuthorMetadataRefresh
{
    [TestFixture]
    public class AuthorMetadataRefreshFixture : TestBase<AuthorController>
    {
        private const string RefreshProperty = "hasMetadataRefresh";

        [TestCase(false)]
        [TestCase(true)]
        public void should_return_recorded_refresh_state_for_stored_authors(bool refreshed)
        {
            var author = new NzbDrone.Core.Books.Author { Id = 12, LastInfoSync = refreshed ? DateTime.UtcNow : null };
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthor(author.Id)).Returns(author);
            GivenAuthorDetails(author.Id);

            var result = Subject.GetResourceByIdWithErrorHandler(author.Id).Value;
            ToJson(result).GetProperty(RefreshProperty).GetBoolean().Should().Be(refreshed);
        }

        [Test]
        public void should_include_recorded_refresh_state_in_the_stored_author_list()
        {
            var author = new NzbDrone.Core.Books.Author { Id = 12 };
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAllAuthors()).Returns(new List<NzbDrone.Core.Books.Author> { author });
            Mocker.GetMock<IAuthorStatisticsService>().Setup(service => service.AuthorStatistics()).Returns(new List<AuthorStatistics>());
            GivenAuthorDetails(author.Id);

            var resource = Subject.AllAuthors().Should().ContainSingle().Which;

            ToJson(resource).GetProperty(RefreshProperty).GetBoolean().Should().BeFalse();
        }

        [TestCase(0)]
        [TestCase(12)]
        public void should_not_expose_recorded_refresh_state_through_the_shared_lookup_mapping(int id)
        {
            var author = new NzbDrone.Core.Books.Author { Id = id, LastInfoSync = DateTime.UtcNow };

            ToJson(author.ToResource()).TryGetProperty(RefreshProperty, out _).Should().BeFalse();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_broadcast_stored_refresh_state_when_the_edit_body_disagrees(bool refreshed)
        {
            var author = new NzbDrone.Core.Books.Author { Id = 12, LastInfoSync = refreshed ? DateTime.UtcNow : null };
            var refreshTimestamp = author.LastInfoSync;
            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.GetAuthor(author.Id)).Returns(author);
            authors.Setup(service => service.UpdateAuthor(author)).Returns(author);
            GivenAuthorDetails(author.Id);
            var messages = CaptureBroadcasts();
            var supplied = JsonSerializer.Serialize(new { id = author.Id, hasMetadataRefresh = !refreshed });
            var request = STJson.Deserialize<AuthorResource>(supplied);
            ToJson(request).TryGetProperty(RefreshProperty, out _).Should().BeFalse();

            Subject.UpdateAuthor(request);

            author.LastInfoSync.Should().Be(refreshTimestamp);
            ToJson(messages.Should().ContainSingle().Which).GetProperty(RefreshProperty).GetBoolean().Should().Be(refreshed);
        }

        [Test]
        public void should_broadcast_first_recorded_refresh_through_the_existing_saved_author_event()
        {
            var stored = new NzbDrone.Core.Books.Author { Id = 12 };
            var updated = new NzbDrone.Core.Books.Author { Id = stored.Id, LastInfoSync = DateTime.UtcNow };
            GivenAuthorDetails(stored.Id);
            var messages = CaptureBroadcasts();

            Subject.Handle(new AuthorEditedEvent(updated, stored));

            ToJson(messages.Should().ContainSingle().Which).GetProperty(RefreshProperty).GetBoolean().Should().BeTrue();
        }

        private static JsonElement ToJson(AuthorResource resource)
        {
            return JsonSerializer.SerializeToElement(resource, STJson.GetSerializerSettings());
        }

        private void GivenAuthorDetails(int id)
        {
            var books = Mocker.GetMock<IBookService>();
            books.Setup(service => service.GetNextBooksByAuthorMetadataId(It.IsAny<IEnumerable<int>>())).Returns(new List<Book>());
            books.Setup(service => service.GetLastBooksByAuthorMetadataId(It.IsAny<IEnumerable<int>>())).Returns(new List<Book>());
            Mocker.GetMock<IAuthorFormatProfileService>().Setup(service => service.GetByAuthorId(id)).Returns(new List<AuthorFormatProfile>());
        }

        private List<AuthorResource> CaptureBroadcasts()
        {
            var resources = new List<AuthorResource>();
            var broadcaster = Mocker.GetMock<IBroadcastSignalRMessage>();
            broadcaster.SetupGet(service => service.IsConnected).Returns(true);
            broadcaster.Setup(service => service.BroadcastMessage(It.IsAny<SignalRMessage>()))
                .Callback<SignalRMessage>(message => resources.Add(((ResourceChangeMessage<AuthorResource>)message.Body).Resource));
            return resources;
        }
    }
}
