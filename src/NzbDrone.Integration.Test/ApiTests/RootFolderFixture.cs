using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bibliophilarr.Api.V1.RootFolders;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using NUnit.Framework;
using NzbDrone.SignalR;

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

            var assertionsPassed = false;
            try
            {
                created.Id.Should().BePositive();
                var listed = RootFolders.All().Single(folder => folder.Id == created.Id);
                listed.Path.Should().Be(path);
                listed.Name.Should().Be(created.Name);
                assertionsPassed = true;
            }
            finally
            {
                if (created.Id > 0)
                {
                    try
                    {
                        RootFolders.Delete(created.Id);
                    }
                    catch (Exception cleanupError) when (!assertionsPassed)
                    {
                        TestContext.Error.WriteLine($"Root folder cleanup also failed: {cleanupError.Message}");
                    }
                }
            }
        }

        [Test]
        public async Task should_publish_the_created_root_folder_over_authenticated_signalr()
        {
            var path = GetTempDirectory("signalr-library");
            var hubUrl = new Uri(new Uri(RootUrl), "signalr/messages");
            await using var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.Headers["X-Api-Key"] = ApiKey;
                    options.Transports = HttpTransportType.WebSockets;
                })
                .Build();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var broadcast = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var subscription = connection.On<SignalRMessage>("receiveMessage", message =>
            {
                if (message.Name == "version")
                {
                    ready.TrySetResult();
                }

                if (message.Name == "rootfolder" && message.Body is JsonElement body &&
                    body.GetProperty("action").GetString() == "created" &&
                    body.GetProperty("resource").GetProperty("path").GetString() == path)
                {
                    broadcast.TrySetResult(body.GetProperty("resource").GetProperty("id").GetInt32());
                }
            });

            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                await connection.StartAsync(timeout.Token);
                await ready.Task.WaitAsync(timeout.Token);
            }

            var created = RootFolders.Post(new RootFolderResource
            {
                Name = "SignalR Test Library",
                Path = path,
                DefaultMetadataProfileId = 1,
                DefaultQualityProfileId = 1,
                OutputProfile = "default"
            });

            try
            {
                (await broadcast.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(created.Id);
            }
            finally
            {
                RootFolders.Delete(created.Id);
            }
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
