using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests.Repositories
{
    [TestFixture]
    public class AuthorMetadataBiographyFixture : DbTest<AuthorMetadataRepository, AuthorMetadata>
    {
        [TestCase("hardcover:author:123", "hardcover:author:123", null)]
        [TestCase("hardcover:author:Author%20Name", "hardcover:author:123", null)]
        [TestCase("googlebooks:author:Author%20Name", "googlebooks:author:Author%20Name", null)]
        [TestCase("hardcover:author:123", "hardcover:author:123", "Provider author biography")]
        public void should_keep_existing_biography_when_provider_omits_it_or_apply_a_supplied_bio(
            string previousId, string remoteId, string providerBiography)
        {
            var prior = new AuthorMetadata
            {
                ForeignAuthorId = previousId,
                Name = "Author Name",
                SortName = "author name",
                NameLastFirst = "Name, Author",
                SortNameLastFirst = "name, author",
                TitleSlug = "author-name",
                Overview = "Biographical data previously supplied by an author source",
                OpenLibraryAuthorId = "OL123A"
            };
            Db.Insert(prior);
            var remote = new AuthorMetadata
            {
                ForeignAuthorId = remoteId,
                Name = prior.Name,
                SortName = prior.SortName,
                NameLastFirst = prior.NameLastFirst,
                SortNameLastFirst = prior.SortNameLastFirst,
                TitleSlug = prior.TitleSlug,
                Overview = providerBiography,
                OpenLibraryAuthorId = prior.OpenLibraryAuthorId
            };

            Subject.UpsertMany(new List<AuthorMetadata> { remote });

            var saved = Db.All<AuthorMetadata>().Single();
            saved.Id.Should().Be(prior.Id);
            saved.ForeignAuthorId.Should().Be(remoteId);
            saved.Overview.Should().Be(providerBiography ?? prior.Overview);
            remote.Overview.Should().Be(providerBiography ?? prior.Overview);
        }
    }
}
