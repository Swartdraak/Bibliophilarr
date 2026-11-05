using System.Collections.Generic;
using System.Reflection;
using Bibliophilarr.Api.V1.Author;
using Moq;
using NLog;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Api.Test.AuthorEditor
{
    /// <summary>
    /// Issue #236 regression: CWE-117 (Log Injection / cs/log-forging) in
    /// AuthorEditorController.cs — 6 open CodeQL alerts (#211–#216).
    ///
    /// The original fix (commit 690bdc6057) added the private static
    /// SanitizeForLog() helper and applied it to all user-controlled root-folder
    /// path values interpolated into NLog logger calls. However, the two
    /// _logger.Debug "Updated ... format profile for author '{0}'" lines still
    /// interpolated author.Name (a user-controlled value loaded from the database
    /// via resource.AuthorIds) without sanitization.
    ///
    /// This PR extends SanitizeForLog to cover author.Name. These fixtures pin
    /// the behavior with an in-memory NLog MemoryTarget so the log messages that
    /// SaveAll actually emits are captured and asserted on directly.
    /// </summary>
    [TestFixture]
    public class AuthorEditorControllerIssue236Fixture
    {
        private static readonly MethodInfo SanitizeForLogMethod =
            typeof(AuthorEditorController).GetMethod("SanitizeForLog", BindingFlags.NonPublic | BindingFlags.Static);

        private MemoryTarget _logTarget;
        private Logger _logger;

        [SetUp]
        public void SetUp()
        {
            _logTarget = new MemoryTarget("issue236");
            _logTarget.Layout = "${message}";
            var config = new NLog.Config.LoggingConfiguration();
            config.AddTarget(_logTarget);
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, _logTarget, "Test");
            NLog.LogManager.Configuration = config;
            _logger = NLog.LogManager.GetLogger("Test");
        }

        [TearDown]
        public void TearDown()
        {
            NLog.LogManager.Shutdown();
        }

        [Test]
        [Category("FAST")]
        public void save_all_with_crlf_in_author_name_should_not_emit_raw_newline_in_log()
        {
            // Author name containing CRLF — the exact vector CodeQL flags.
            const string maliciousName = "EvilAuthor\r\nFAKE LOG: compromised";
            const int authorId = 42;

            var author = BuildAuthor(authorId, maliciousName);
            var authorServiceMock = new Mock<IAuthorService>();
            authorServiceMock
                .Setup(s => s.GetAuthors(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Author> { author });
            authorServiceMock
                .Setup(s => s.UpdateAuthors(It.IsAny<List<Author>>(), It.IsAny<bool>()))
                .Returns(new List<Author> { author });

            var ebookProfile = new AuthorFormatProfile
            {
                AuthorId = authorId,
                FormatType = FormatType.Ebook,
                QualityProfileId = 7,
                RootFolderPath = "/data/ebooks"
            };
            var audiobookProfile = new AuthorFormatProfile
            {
                AuthorId = authorId,
                FormatType = FormatType.Audiobook,
                QualityProfileId = 8,
                RootFolderPath = "/data/audiobooks"
            };

            var formatProfileMock = new Mock<IAuthorFormatProfileService>();
            formatProfileMock
                .Setup(s => s.GetByAuthorIdAndFormat(authorId, FormatType.Ebook))
                .Returns(ebookProfile);
            formatProfileMock
                .Setup(s => s.GetByAuthorIdAndFormat(authorId, FormatType.Audiobook))
                .Returns(audiobookProfile);
            formatProfileMock
                .Setup(s => s.GetByAuthorId(authorId))
                .Returns(new List<AuthorFormatProfile> { ebookProfile, audiobookProfile });
            formatProfileMock
                .Setup(s => s.Update(It.IsAny<AuthorFormatProfile>()))
                .Returns((AuthorFormatProfile p) => p);

            var controller = new AuthorEditorController(
                authorServiceMock.Object,
                formatProfileMock.Object,
                Mock.Of<IManageCommandQueue>(),
                _logger);

            var resource = new AuthorEditorResource
            {
                AuthorIds = new List<int> { authorId },
                EbookQualityProfileId = 7,
                AudiobookQualityProfileId = 8,
                EbookRootFolderPath = "/data/ebooks",
                AudiobookRootFolderPath = "/data/audiobooks",
                MoveFiles = false
            };

            var result = controller.SaveAll(resource);

            // SaveAll returns Accepted(202) with the updated author resources.
            Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.AcceptedResult>());

            // The captured log must not contain a raw CR or LF originating from the
            // malicious author name. SanitizeForLog escapes them as \r and \n.
            // NLog 5.x MemoryTarget exposes the captured log events as Logs (a list
            // of formatted strings).
            var log = string.Join("", _logTarget.Logs);
            Assert.False(log.Contains("\r"),
                "Log output contains a raw carriage return — CWE-117 log injection vector");
            Assert.False(log.Contains("\n"),
                "Log output contains a raw newline — CWE-117 log injection vector");

            // The escaped form must be present, proving sanitization was applied.
            StringAssert.Contains(
                "\\r\\n",
                log,
                "CRLF in author name should be escaped as literal backslash-r backslash-n in log");
        }

        [Test]
        [Category("FAST")]
        public void save_all_with_crlf_in_root_folder_paths_should_sanitize_before_log()
        {
            const string maliciousEbookPath = "/data\r\nFORGED: admin action";
            const string maliciousAudiobookPath = "/data\nINJECTED";
            const int authorId = 99;

            var author = BuildAuthor(authorId, "NormalAuthor");
            var authorServiceMock = new Mock<IAuthorService>();
            authorServiceMock
                .Setup(s => s.GetAuthors(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Author> { author });
            authorServiceMock
                .Setup(s => s.UpdateAuthors(It.IsAny<List<Author>>(), It.IsAny<bool>()))
                .Returns(new List<Author> { author });

            var ebookProfile = new AuthorFormatProfile
            {
                AuthorId = authorId,
                FormatType = FormatType.Ebook,
                QualityProfileId = 3,
                RootFolderPath = maliciousEbookPath
            };
            var audiobookProfile = new AuthorFormatProfile
            {
                AuthorId = authorId,
                FormatType = FormatType.Audiobook,
                QualityProfileId = 4,
                RootFolderPath = maliciousAudiobookPath
            };

            var formatProfileMock = new Mock<IAuthorFormatProfileService>();
            formatProfileMock
                .Setup(s => s.GetByAuthorIdAndFormat(authorId, FormatType.Ebook))
                .Returns(ebookProfile);
            formatProfileMock
                .Setup(s => s.GetByAuthorIdAndFormat(authorId, FormatType.Audiobook))
                .Returns(audiobookProfile);
            formatProfileMock
                .Setup(s => s.GetByAuthorId(authorId))
                .Returns(new List<AuthorFormatProfile> { ebookProfile, audiobookProfile });
            formatProfileMock
                .Setup(s => s.Update(It.IsAny<AuthorFormatProfile>()))
                .Returns((AuthorFormatProfile p) => p);

            var controller = new AuthorEditorController(
                authorServiceMock.Object,
                formatProfileMock.Object,
                Mock.Of<IManageCommandQueue>(),
                _logger);

            var resource = new AuthorEditorResource
            {
                AuthorIds = new List<int> { authorId },
                EbookQualityProfileId = 3,
                AudiobookQualityProfileId = 4,
                EbookRootFolderPath = maliciousEbookPath,
                AudiobookRootFolderPath = maliciousAudiobookPath,
                MoveFiles = false
            };

            controller.SaveAll(resource);

            var log = string.Join("", _logTarget.Logs);
            Assert.False(log.Contains("\r"),
                "Log output contains a raw carriage return — CWE-117 log injection vector");
            Assert.False(log.Contains("\n"),
                "Log output contains a raw newline — CWE-117 log injection vector");

            // Both malicious paths must appear escaped.
            StringAssert.Contains("\\r\\n", log, "CRLF in ebook root folder should be escaped in log");
            StringAssert.Contains("\\n", log, "LF in audiobook root folder should be escaped in log");
        }

        [Test]
        [Category("FAST")]
        public void sanitize_for_log_should_escape_both_cr_and_lf_and_handle_null()
        {
            var crlf = "line1\r\nline2";
            var crOnly = "cr\ronly";
            var lfOnly = "lf\nonly";
            var clean = "no-control-chars";
            string nullInput = null;

            var crlfSanitized = (string)SanitizeForLogMethod.Invoke(null, new object[] { crlf });
            var crSanitized = (string)SanitizeForLogMethod.Invoke(null, new object[] { crOnly });
            var lfSanitized = (string)SanitizeForLogMethod.Invoke(null, new object[] { lfOnly });
            var cleanSanitized = (string)SanitizeForLogMethod.Invoke(null, new object[] { clean });
            var nullSanitized = (string)SanitizeForLogMethod.Invoke(null, new object[] { nullInput });

            // CRLF: both characters escaped.
            Assert.AreEqual("line1\\r\\nline2", crlfSanitized);
            Assert.False(crlfSanitized.Contains("\r") || crlfSanitized.Contains("\n"));

            // CR only.
            Assert.AreEqual("cr\\ronly", crSanitized);
            Assert.False(crSanitized.Contains("\r"));

            // LF only.
            Assert.AreEqual("lf\\nonly", lfSanitized);
            Assert.False(lfSanitized.Contains("\n"));

            // Clean string passes through unchanged.
            Assert.AreEqual(clean, cleanSanitized);

            // Null maps to empty string (the ?? string.Empty fallback).
            Assert.AreEqual(string.Empty, nullSanitized);
        }

        private static Author BuildAuthor(int id, string name)
        {
            var metadata = new AuthorMetadata { Name = name };
            var author = new Author { Id = id, CleanName = name.Replace(" ", "") };
            author.Metadata = metadata;
            return author;
        }
    }
}
