using System.Linq;
using Bibliophilarr.Api.V1.Author;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class AuthorEditorFixture : CatalogueIntegrationTest
    {
        [Test]
        public void should_be_able_to_update_multiple_author()
        {
            var authors = new[] { SeedAuthor("Fixture Author One"), SeedAuthor("Fixture Author Two") };

            var authorEditor = new AuthorEditorResource
            {
                QualityProfileId = 2,
                AuthorIds = authors.Select(o => o.Id).ToList()
            };

            var result = Author.Editor(authorEditor);

            result.Should().HaveCount(2);
            result.TrueForAll(s => s.QualityProfileId == 2).Should().BeTrue();
        }
    }
}
