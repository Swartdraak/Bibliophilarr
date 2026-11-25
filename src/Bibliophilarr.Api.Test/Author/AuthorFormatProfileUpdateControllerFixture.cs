using System.Collections.Generic;
using System.IO;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Http.REST;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.AuthorFormatProfiles
{
    [TestFixture]
    public class AuthorFormatProfileUpdateControllerFixture : TestBase<AuthorFormatProfileController>
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void should_preserve_the_stored_hidden_path_when_editing_format_settings(bool hasRouteId, bool omitTags)
        {
            var stored = GivenProfile();
            if (hasRouteId)
            {
                Subject.RouteData.Values["id"] = stored.Id;
            }

            var request = stored.ToResource();
            request.RootFolderPath = "/next-audio";
            request.QualityProfileId = 25;
            if (omitTags)
            {
                request.Tags = null;
            }

            AuthorFormatProfile saved = null;
            Mocker.GetMock<IAuthorFormatProfileService>().Setup(service => service.Update(It.IsAny<AuthorFormatProfile>()))
                .Returns((AuthorFormatProfile profile) =>
                {
                    saved = profile;
                    return profile;
                });

            Subject.Update(request);

            saved.Should().BeEquivalentTo(new AuthorFormatProfile
            {
                Id = stored.Id,
                AuthorId = stored.AuthorId,
                FormatType = stored.FormatType,
                Path = stored.Path,
                RootFolderPath = request.RootFolderPath,
                QualityProfileId = request.QualityProfileId,
                Tags = stored.Tags,
                Monitored = stored.Monitored,
                MonitorNewItems = stored.MonitorNewItems
            });
        }

        [TestCase("route")]
        [TestCase("author")]
        [TestCase("format")]
        [TestCase("none")]
        public void should_refuse_to_change_the_stored_profile_identity(string mismatch)
        {
            var stored = GivenProfile();
            var request = stored.ToResource();
            if (mismatch == "route")
            {
                Subject.RouteData.Values["id"] = stored.Id + 1;
            }
            else if (mismatch == "author")
            {
                request.AuthorId++;
            }
            else if (mismatch == "format")
            {
                request.FormatType = FormatType.Ebook;
            }
            else
            {
                request.Id = 0;
            }

            Assert.Throws<BadRequestException>(() => Subject.Update(request));

            Mocker.GetMock<IAuthorFormatProfileService>().Verify(service => service.Update(It.IsAny<AuthorFormatProfile>()), Times.Never());
        }

        [Test]
        public void bulk_root_changes_should_continue_to_calculate_the_format_path()
        {
            var stored = GivenProfile();
            var author = new NzbDrone.Core.Books.Author { Id = stored.AuthorId, CleanName = "fixture-author" };
            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.GetAuthors(It.IsAny<IEnumerable<int>>())).Returns(new List<NzbDrone.Core.Books.Author> { author });
            authors.Setup(service => service.UpdateAuthors(It.IsAny<List<NzbDrone.Core.Books.Author>>(), true)).Returns(new List<NzbDrone.Core.Books.Author> { author });
            var profiles = Mocker.GetMock<IAuthorFormatProfileService>();
            profiles.Setup(service => service.GetByAuthorId(author.Id)).Returns(new List<AuthorFormatProfile> { stored });
            profiles.Setup(service => service.GetByAuthorIdAndFormat(author.Id, FormatType.Audiobook)).Returns(stored);
            const string destination = "/new-audio";

            Mocker.Resolve<AuthorEditorController>().SaveAll(new AuthorEditorResource
            {
                AuthorIds = new List<int> { author.Id },
                AudiobookRootFolderPath = destination
            });

            var expectedPath = Path.Combine(destination, author.CleanName);
            profiles.Verify(service => service.Update(It.Is<AuthorFormatProfile>(profile =>
                profile.Id == stored.Id && profile.RootFolderPath == destination && profile.Path == expectedPath)), Times.Once());
        }

        private AuthorFormatProfile GivenProfile()
        {
            Subject.ControllerContext.RouteData = new Microsoft.AspNetCore.Routing.RouteData();
            var stored = new AuthorFormatProfile
            {
                Id = 12,
                AuthorId = 7,
                FormatType = FormatType.Audiobook,
                Path = "/audio/custom-author",
                RootFolderPath = "/audio",
                QualityProfileId = 24,
                Tags = new HashSet<int> { 5, 6 },
                Monitored = true,
                MonitorNewItems = NewItemMonitorTypes.None
            };
            Mocker.GetMock<IAuthorFormatProfileService>().Setup(service => service.Get(stored.Id)).Returns(stored);
            return stored;
        }
    }
}
