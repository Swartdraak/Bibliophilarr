using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]

    public class GetAuthorFolderFixture : CoreTest<FileNameBuilder>
    {
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _namingConfig = NamingConfig.Default;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig()).Returns(_namingConfig);
        }

        [TestCase("Avenged Sevenfold", "{Author Name}", "Avenged Sevenfold")]
        [TestCase("Avenged Sevenfold", "{Author.Name}", "Avenged.Sevenfold")]
        [TestCase("AC/DC", "{Author Name}", "AC+DC")]
        [TestCase("In the Woods...", "{Author.Name}", "In.the.Woods")]
        [TestCase("3OH!3", "{Author.Name}", "3OH!3")]
        [TestCase("Avenged Sevenfold", ".{Author.Name}.", "Avenged.Sevenfold")]
        public void should_use_authorFolderFormat_to_build_folder_name(string authorName, string format, string expected)
        {
            _namingConfig.AuthorFolderFormat = format;

            var author = new Author { Name = authorName };

            Subject.GetAuthorFolder(author).Should().Be(expected);
        }

        [Test]
        public void should_not_throw_when_building_author_folder_for_author_with_null_name()
        {
            // Issue #325 (DEFECT-RECOVERY-AUTHOR-ADD-500-NRE): POST /api/v1/author for a
            // LOCAL author WITHOUT an explicit path (only rootFolderPath — the
            // documented local-add path) returned 500 'Object reference not set to an
            // instance of an object'. AuthorFolderAsRootFolderValidator (fires when
            // rootFolderPath is set + path empty) calls FileNameBuilder.GetAuthorFolder
            // on an author whose Author.Name => Metadata.Value.Name was null before
            // provider resolution, and the "{Author Name}" token handler
            // (author.Name) had no null guard. The folder builder must degrade
            // gracefully (empty token / fallback to the clean name) instead of
            // crashing the API.
            var author = new Author();

            Subject.Invoking(s => s.GetAuthorFolder(author))
                   .Should().NotThrow();
        }

        [Test]
        public void should_fall_back_to_clean_name_when_building_author_folder_for_author_with_null_name()
        {
            // Issue #325 companion: the graceful degradation must produce a
            // deterministic folder (the clean name) for an author whose metadata
            // name is not yet loaded, not an empty path.
            var author = new Author { CleanName = "Avenged Sevenfold" };

            _namingConfig.AuthorFolderFormat = "{Author Name}";

            Subject.GetAuthorFolder(author).Should().Be("Avenged Sevenfold");
        }
    }
}
