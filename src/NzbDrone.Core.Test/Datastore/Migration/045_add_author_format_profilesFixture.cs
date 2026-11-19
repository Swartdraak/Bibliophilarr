using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_author_format_profilesFixture : MigrationTest<add_author_format_profiles>
    {
        [TestCase("[{\"quality\":12,\"allowed\":true}]", FormatType.Audiobook)]
        [TestCase("[{\"quality\":12,\"allowed\":false},{\"quality\":3,\"allowed\":true}]", FormatType.Ebook)]
        public void should_backfill_the_format_from_allowed_qualities(string items, FormatType expectedFormat)
        {
            var db = WithMigrationTestDb(migration =>
            {
                migration.Insert.IntoTable("QualityProfiles").Row(new { Id = 51, Name = "Sample profile", Cutoff = 3, Items = items });
                migration.Insert.IntoTable("Authors").Row(new
                {
                    Id = 50,
                    AuthorMetadataId = 49,
                    Path = "/sample-author",
                    CleanName = "sampleauthor",
                    QualityProfileId = 51,
                    MetadataProfileId = 1,
                    MonitorNewItems = 0,
                    Monitored = false
                });
            });

            var formatProfile = db.Query<FormatProfileRow>("SELECT \"AuthorId\", \"FormatType\", \"QualityProfileId\", \"Monitored\", \"Path\" FROM \"AuthorFormatProfiles\"").Single();
            formatProfile.AuthorId.Should().Be(50);
            formatProfile.FormatType.Should().Be((int)expectedFormat);
            formatProfile.QualityProfileId.Should().Be(51);
            formatProfile.Monitored.Should().BeTrue();
            formatProfile.Path.Should().Be("/sample-author");
        }

        private class FormatProfileRow
        {
            public int AuthorId { get; set; }
            public int FormatType { get; set; }
            public int QualityProfileId { get; set; }
            public bool Monitored { get; set; }
            public string Path { get; set; }
        }
    }
}
