using System;
using System.Collections.Generic;
using Bibliophilarr.Api.V1.Search;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Api.Test.Search
{
    [TestFixture]
    public class SearchControllerFixture
    {
        private SearchTelemetryService _telemetry;
        private MemoryTarget _memoryTarget;
        private LoggingConfiguration _originalConfiguration;

        [SetUp]
        public void SetUp()
        {
            _telemetry = new SearchTelemetryService();
            _originalConfiguration = LogManager.Configuration;
            _memoryTarget = new MemoryTarget("search-messages") { Layout = "${level}|${message}" };

            var configuration = new LoggingConfiguration();
            configuration.AddTarget(_memoryTarget);
            configuration.LoggingRules.Add(new LoggingRule("Search", LogLevel.Warn, _memoryTarget));
            LogManager.Configuration = configuration;
        }

        [TearDown]
        public void TearDown()
        {
            LogManager.Configuration = _originalConfiguration;
        }

        [Test]
        public void should_skip_unsupported_search_entities_and_record_observability()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "Anne Shirley",
                    ForeignAuthorId = "OL123A",
                    Images = new List<MediaCover>()
                }
            };

            var searchProxy = new Mock<ISearchForNewEntity>();
            searchProxy.Setup(v => v.SearchForNewEntity("anne"))
                .Returns(new List<object> { author, new Version(1, 0) });

            var fileNameBuilder = new Mock<IBuildFileNames>();
            fileNameBuilder.Setup(v => v.GetAuthorFolder(It.IsAny<Author>(), null))
                .Returns("Anne Shirley");

            var coverMapper = new Mock<IMapCoversToLocal>();

            var controller = new SearchController(searchProxy.Object, fileNameBuilder.Object, coverMapper.Object, _telemetry);

            var result = controller.Search("anne");

            result.Should().BeOfType<List<SearchResource>>();
            var resources = (List<SearchResource>)result;
            resources.Should().HaveCount(1);
            resources[0].Author.Should().NotBeNull();

            var snapshot = _telemetry.GetSnapshot();
            snapshot.UnsupportedEntityCount.Should().Be(1);
            snapshot.UnsupportedEntityTypes[typeof(Version).FullName].Should().Be(1);
            snapshot.Terms["anne"].Should().Be(1);
            _memoryTarget.Logs.Should().ContainSingle(log => log.Contains("unsupported search entity type") && log.Contains("anne"));
        }
    }
}
