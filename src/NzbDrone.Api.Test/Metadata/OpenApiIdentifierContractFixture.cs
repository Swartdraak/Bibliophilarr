using System.IO;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NzbDrone.Api.Test.Metadata
{
    [TestFixture]
    public class OpenApiIdentifierContractFixture
    {
        [Test]
        public void openapi_should_not_expose_legacy_goodreads_identifier_contracts()
        {
            var openApiPath = ResolveOpenApiPath();
            var json = File.ReadAllText(openApiPath);
            var doc = JObject.Parse(json);
            var raw = doc.ToString();

            raw.Should().NotContain("goodreads", "legacy identifier contracts must stay removed");
            raw.Should().Contain("openlibrary", "OpenLibrary identifiers must remain in the API contract");
        }

        private static string ResolveOpenApiPath()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Bibliophilarr.Api.V1", "openapi.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            Assert.Fail("Unable to resolve src/Bibliophilarr.Api.V1/openapi.json from test directory");
            return null;
        }
    }
}
