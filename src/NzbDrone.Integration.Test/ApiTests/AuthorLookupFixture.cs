using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class AuthorLookupFixture : IntegrationTest
    {
        [TestCase("author:nonexistent-000000000000")]
        [TestCase("gibberish-qwertyuiop-zxcvbnm-123456789")]
        public void lookup_should_return_a_valid_response_for_unresolvable_terms(string term)
        {
            var author = Author.Lookup(term);

            author.Should().NotBeNull();
        }

        [Test]
        public void lookup_with_malformed_identifier_should_not_return_server_error()
        {
            var request = new SimpleRestRequest("author/lookup");
            request.AddQueryParameter("term", "edition:bad value with spaces");

            var response = ExecuteRequest(request);

            ((int)response.StatusCode).Should().BeLessThan(500);
        }
    }
}
