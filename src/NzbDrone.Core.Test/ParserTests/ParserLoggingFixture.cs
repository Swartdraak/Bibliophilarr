using System.Collections.Generic;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    [NonParallelizable]
    public class ParserLoggingFixture : CoreTest
    {
        private LoggingConfiguration _previousConfiguration;
        private MemoryTarget _messages;

        [SetUp]
        public void CaptureParserDiagnostics()
        {
            _previousConfiguration = LogManager.Configuration;
            _messages = new MemoryTarget { Layout = "${level}|${message}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Debug, LogLevel.Error, _messages, "Parser");
            configuration.AddRule(LogLevel.Debug, LogLevel.Error, _messages, nameof(QualityParser));
            LogManager.Configuration = configuration;
        }

        [TearDown]
        public void RestoreLogging()
        {
            LogManager.Configuration = _previousConfiguration;
        }

        [Test]
        public void should_log_quality_input_on_one_line_without_changing_classification()
        {
            const string title = "City Lights\r\nEPUB\tRetail";

            QualityParser.ParseQuality(title).Quality.Should().Be(Quality.EPUB);

            _messages.Logs.Should().Contain("Debug|Trying to parse quality for 'City LightsEPUB Retail'");
        }

        [Test]
        public void should_log_book_input_on_one_line_without_changing_parsed_metadata()
        {
            const string title = "Ada Vale - City Lights (2022) [EPUB]\r\n\t";

            var result = Parser.Parser.ParseBookTitle(title);

            result.AuthorName.Should().Be("Ada Vale");
            result.BookTitle.Should().Be("City Lights");
            result.Quality.Quality.Should().Be(Quality.EPUB);
            _messages.Logs.Should().Contain("Debug|Parsing string 'Ada Vale - City Lights (2022) [EPUB] '");
        }

        [Test]
        public void should_log_the_title_of_an_unmatched_book_on_one_line()
        {
            const string title = "City\r\nLights\tCatalogue";

            Parser.Parser.ParseBookTitle(title).Should().BeNull();

            _messages.Logs.Should().Contain("Debug|Unable to parse CityLights Catalogue");
        }

        [Test]
        public void should_log_fuzzy_search_context_on_one_line()
        {
            const string title = "Ada Vale - City Lights\r\nEPUB\tRetail";
            var author = new Author { Name = "Ada\tVale" };
            var books = new List<Book>
            {
                new Book { Title = "City\r\nLights", Editions = new List<Edition>() }
            };

            Parser.Parser.ParseBookTitleWithSearchCriteria(title, author, books).Should().BeNull();

            _messages.Logs.Should().Contain("Debug|Parsing string 'Ada Vale - City LightsEPUB Retail' using search criteria author: 'Ada Vale' books: 'CityLights'");
            _messages.Logs.Should().Contain("Debug|No books with monitored editions found for fuzzy matching");
        }
    }
}
