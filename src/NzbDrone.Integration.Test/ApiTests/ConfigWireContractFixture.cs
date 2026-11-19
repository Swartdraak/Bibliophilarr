using System.Net;
using System.Net.Http;
using System.Text.Json;
using Bibliophilarr.Api.V1.Config;
using Bibliophilarr.Api.V1.ManualImport;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class ConfigWireContractFixture : IntegrationTest
    {
        [Test]
        public void metadata_config_should_return_accepted_and_the_saved_camel_case_fields()
        {
            const string endpoint = "config/metadataprovider";
            const string field = "enableOpenLibraryProvider";
            var configClient = new ClientBase<MetadataProviderConfigResource>(RestClient, ApiKey, endpoint);
            var original = configClient.GetSingle();
            var updated = configClient.GetSingle();
            updated.EnableOpenLibraryProvider = !original.EnableOpenLibraryProvider;

            try
            {
                var save = new SimpleRestRequest($"{endpoint}/{updated.Id}", HttpMethod.Put);
                save.AddJsonBody(updated);
                var accepted = ExecuteRequest(save);
                Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
                using var acceptedBody = JsonDocument.Parse(accepted.Content);
                Assert.That(acceptedBody.RootElement.GetProperty(field).GetBoolean(), Is.EqualTo(updated.EnableOpenLibraryProvider));

                var get = ExecuteRequest(new SimpleRestRequest(endpoint));
                Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                using var getBody = JsonDocument.Parse(get.Content);
                Assert.That(getBody.RootElement.GetProperty(field).GetBoolean(), Is.EqualTo(updated.EnableOpenLibraryProvider));
            }
            finally
            {
                configClient.Put(original);
            }
        }

        [Test]
        public void manual_import_without_a_catalogue_mapping_should_return_bad_request()
        {
            var request = new SimpleRestRequest("manualimport", HttpMethod.Post);
            request.AddJsonBody(new[] { new ManualImportUpdateResource { Path = "unmapped.epub" } });
            var response = ExecuteRequest(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            using var payload = JsonDocument.Parse(response.Content);
            Assert.That(payload.RootElement[0].GetProperty("rejections").GetArrayLength(), Is.Positive);
        }
    }
}
