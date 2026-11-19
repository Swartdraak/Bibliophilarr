using System;
using System.Linq;
using Bibliophilarr.Api.V1.RootFolders;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class RootFolderFixture : IntegrationTest
    {
        [Test]
        public void should_have_no_root_folder_initially()
        {
            RootFolders.All().Should().BeEmpty();
        }

        [Test]
        public void should_list_the_created_root_folder_id_and_path()
        {
            var path = GetTempDirectory("library");
            var created = RootFolders.Post(new RootFolderResource
            {
                Name = "Test Library",
                Path = path,
                DefaultMetadataProfileId = 1,
                DefaultQualityProfileId = 1,
                OutputProfile = "default"
            });

            try
            {
                created.Id.Should().BePositive();
                var listed = RootFolders.All().Single(folder => folder.Id == created.Id);
                listed.Path.Should().Be(path);
                listed.Name.Should().Be(created.Name);
            }
            finally
            {
                RootFolders.Delete(created.Id);
            }
        }

        [Test]
        [Ignore("SignalR on CI seems unstable")]
        public void should_add_and_delete_root_folders()
        {
            ConnectSignalR().Wait();

            var rootFolder = new RootFolderResource
            {
                Path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            };

            var postResponse = RootFolders.Post(rootFolder);

            postResponse.Id.Should().NotBe(0);
            postResponse.FreeSpace.Should().NotBe(0);

            RootFolders.All().Should().OnlyContain(c => c.Id == postResponse.Id);

            RootFolders.Delete(postResponse.Id);

            RootFolders.All().Should().BeEmpty();

            SignalRMessages.Should().Contain(c => c.Name == "rootfolder");
        }

        [Test]
        public void invalid_path_should_return_bad_request()
        {
            var rootFolder = new RootFolderResource
            {
                Path = "invalid_path"
            };

            var postResponse = RootFolders.InvalidPost(rootFolder);
            postResponse.Should().NotBeNull();
        }
    }
}
