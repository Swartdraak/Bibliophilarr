using System.Collections.Generic;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests.Repositories
{
    [TestFixture]
    public class AuthorMetadataBiographyFixture : DbTest<AuthorMetadataRepository, AuthorMetadata>
    {
        [TestCase("blank")]
        [TestCase("newer-bio")]
        [TestCase("newer-portrait")]
        [TestCase("newer-both")]
        [TestCase("changed-source-evidence")]
        [TestCase("conflicting-second-source")]
        public void should_fill_only_currently_missing_profile_fields_for_same_verified_author(string interveningChange)
        {
            const string sourceId = "OL123A";
            const string primaryId = "hardcover:author:123";
            const string storedDescription = "Current biography";
            const string existingBanner = "https://example.com/existing-banner.jpg";
            const string currentPortrait = "https://example.com/primary-portrait.jpg";
            const string alternatePortrait = "https://covers.openlibrary.org/a/id/123-L.jpg";
            var prior = Subject.Insert(new AuthorMetadata
            {
                ForeignAuthorId = primaryId,
                Name = "Stored Name",
                SortName = "stored name",
                NameLastFirst = "Name, Stored",
                SortNameLastFirst = "name, stored",
                TitleSlug = "stored-name",
                Overview = " \t ",
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Banner, existingBanner) }
            });
            var author = Db.Insert(new Author { AuthorMetadataId = prior.Id, Metadata = prior, Path = "/author", CleanName = "storedname" });
            var identities = Mocker.Resolve<CatalogueIdentityRepository>();
            identities.TryAddSource(CatalogueEntityKind.Author, prior.Id, "OpenLibrary", sourceId, AuthorCanonicalizationService.BibliographyEvidence).Should().BeTrue();

            var current = Subject.Get(prior.Id);
            var hasNewerBiography = interveningChange is "newer-bio" or "newer-both";
            var hasNewerPortrait = interveningChange is "newer-portrait" or "newer-both";
            current.Name = "Current Name";
            if (hasNewerBiography)
            {
                current.Overview = storedDescription;
            }

            if (hasNewerPortrait)
            {
                current.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, currentPortrait));
            }

            Subject.SetFields(current, fields => fields.Name, fields => fields.Overview, fields => fields.Images);
            if (interveningChange == "changed-source-evidence")
            {
                using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
                connection.Execute("""UPDATE "AuthorSourceAssociations" SET "Evidence" = 'legacy-linked-id' WHERE "Provider" = 'OpenLibrary'""");
            }
            else if (interveningChange == "conflicting-second-source")
            {
                identities.TryAddSource(CatalogueEntityKind.Author, prior.Id, "OpenLibrary", "OL456A", AuthorCanonicalizationService.BibliographyEvidence).Should().BeTrue();
            }

            var originalSources = identities.GetSources(CatalogueEntityKind.Author, prior.Id);
            var portrait = new MediaCover.MediaCover(MediaCoverTypes.Poster, alternatePortrait) { SourceProvider = "OpenLibrary", SourceId = sourceId };
            var invalidSource = interveningChange is "changed-source-evidence" or "conflicting-second-source";
            var changed = Subject.TryFillOpenLibraryProfile(author, sourceId, " Verified overview ", portrait);

            changed.Should().Be(!invalidSource && (!hasNewerBiography || !hasNewerPortrait));
            var saved = Subject.Get(prior.Id);
            saved.Name.Should().Be("Current Name");
            saved.CanonicalId.Should().Be(prior.CanonicalId);
            saved.ForeignAuthorId.Should().Be(primaryId);
            saved.Overview.Should().Be(invalidSource ? prior.Overview : hasNewerBiography ? storedDescription : "Verified overview");
            var expectedImages = invalidSource ? new[] { existingBanner } : new[] { existingBanner, hasNewerPortrait ? currentPortrait : alternatePortrait };
            saved.Images.Select(image => image.Url).Should().Equal(expectedImages);
            identities.GetSources(CatalogueEntityKind.Author, prior.Id).Should().BeEquivalentTo(originalSources);
        }

        [TestCase(" \t ", true, true)]
        [TestCase("{", true, false)]
        [TestCase("{", false, false)]
        public void should_fill_the_biography_without_replacing_unreadable_image_data(string storedImages, bool supplyPortrait, bool canFillPortrait)
        {
            const string sourceId = "OL123A";
            const string overview = "Verified biography";
            const string portraitUrl = "https://covers.openlibrary.org/a/id/123-L.jpg";
            var metadata = Subject.Insert(new AuthorMetadata
            {
                ForeignAuthorId = "hardcover:author:123",
                Name = "Stored Name",
                SortName = "stored name",
                NameLastFirst = "Name, Stored",
                SortNameLastFirst = "name, stored",
                TitleSlug = "stored-name"
            });
            var author = Db.Insert(new Author { AuthorMetadataId = metadata.Id, Metadata = metadata, Path = "/author", CleanName = "storedname" });
            Mocker.Resolve<CatalogueIdentityRepository>()
                .TryAddSource(CatalogueEntityKind.Author, metadata.Id, "OpenLibrary", sourceId, AuthorCanonicalizationService.BibliographyEvidence)
                .Should().BeTrue();

            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            connection.Execute("""UPDATE "AuthorMetadata" SET "Images" = @storedImages WHERE "Id" = @Id""", new { metadata.Id, storedImages });

            var portrait = supplyPortrait
                ? new MediaCover.MediaCover(MediaCoverTypes.Poster, portraitUrl) { SourceProvider = "OpenLibrary", SourceId = sourceId }
                : null;
            Subject.TryFillOpenLibraryProfile(author, sourceId, overview, portrait).Should().BeTrue();

            var current = connection.QuerySingle<(string Overview, string Images)>(
                """SELECT "Overview", "Images" FROM "AuthorMetadata" WHERE "Id" = @Id""", new { metadata.Id });
            current.Overview.Should().Be(overview);
            if (canFillPortrait)
            {
                Subject.Get(metadata.Id).Images.Should().ContainSingle().Which.Url.Should().Be(portraitUrl);
            }
            else
            {
                current.Images.Should().Be(storedImages);
            }
        }

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
