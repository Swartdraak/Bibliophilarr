using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class DownloadDecisionMakerFixture : CoreTest<DownloadDecisionMaker>
    {
        private List<ReleaseInfo> _reports;
        private RemoteBook _remoteBook;

        private Mock<IDecisionEngineSpecification> _pass1;
        private Mock<IDecisionEngineSpecification> _pass2;
        private Mock<IDecisionEngineSpecification> _pass3;

        private Mock<IDecisionEngineSpecification> _fail1;
        private Mock<IDecisionEngineSpecification> _fail2;
        private Mock<IDecisionEngineSpecification> _fail3;

        private Mock<IDecisionEngineSpecification> _failDelayed1;

        [SetUp]
        public void Setup()
        {
            _pass1 = new Mock<IDecisionEngineSpecification>();
            _pass2 = new Mock<IDecisionEngineSpecification>();
            _pass3 = new Mock<IDecisionEngineSpecification>();

            _fail1 = new Mock<IDecisionEngineSpecification>();
            _fail2 = new Mock<IDecisionEngineSpecification>();
            _fail3 = new Mock<IDecisionEngineSpecification>();

            _failDelayed1 = new Mock<IDecisionEngineSpecification>();

            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Accept);
            _pass2.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Accept);
            _pass3.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Accept);

            _fail1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Reject("fail1"));
            _fail2.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Reject("fail2"));
            _fail3.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Reject("fail3"));

            _failDelayed1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Reject("failDelayed1"));
            _failDelayed1.SetupGet(c => c.Priority).Returns(SpecificationPriority.Disk);

            _reports = new List<ReleaseInfo> { new ReleaseInfo { Title = "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" } };
            _remoteBook = new RemoteBook
            {
                Author = new Author(),
                Books = new List<Book> { new Book() },
                ParsedBookInfo = Builder<ParsedBookInfo>.CreateNew().With(x => x.Quality = new QualityModel(Quality.FLAC)).Build()
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(_remoteBook);
        }

        private void GivenSpecifications(params Mock<IDecisionEngineSpecification>[] mocks)
        {
            Mocker.SetConstant<IEnumerable<IDecisionEngineSpecification>>(mocks.Select(c => c.Object));
        }

        [Test]
        public void should_log_search_and_reparse_context_on_one_line_without_changing_the_release()
        {
            using var logging = CaptureDecisionMessages(out var messages);
            const string title = "Ada Vale - City Lights (2022) [EPUB]\r\n\t";
            const string indexer = "Library Catalogue\r\n\tFeed";
            var report = _reports.Single();
            report.Title = title;
            report.Indexer = indexer;
            report.IndexerId = 12;
            _remoteBook.Author = null;
            var criteria = new BookSearchCriteria { Author = new Author { Name = "Ada Vale" }, Books = new List<Book>() };
            GivenSpecifications(_pass1);

            var decision = Subject.GetSearchDecision(_reports, criteria).Single();

            decision.RemoteBook.Release.Should().BeSameAs(report);
            decision.RemoteBook.Release.Title.Should().Be(title);
            decision.RemoteBook.Release.Indexer.Should().Be(indexer);
            decision.RemoteBook.Release.IndexerId.Should().Be(12);
            decision.RemoteBook.ReleaseSource.Should().Be(ReleaseSourceType.Search);
            decision.Rejections.Should().ContainSingle().Which.Reason.Should().Be("Unknown Author");
            messages.Logs.Should().Contain("Processing release 'Ada Vale - City Lights (2022) [EPUB] ' from 'Library Catalogue Feed'");
            messages.Logs.Should().Contain("Author/Book null for Ada Vale - City Lights (2022) [EPUB] , reparsing with search criteria");
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void should_keep_exception_data_compact_and_preserve_the_original_rejection(bool hasReport, bool hasParsed)
        {
            using var logging = CaptureDecisionMessages(out var messages);
            const string title = "Ada Vale - City Lights (2022) [EPUB]\r\n\t";
            var report = _reports.Single();
            report.Title = title;
            report.Guid = "https://indexer.invalid/releases/12";
            report.DownloadUrl = "https://indexer.invalid/download/12";
            report.InfoUrl = "https://indexer.invalid/details/12";
            report.CommentUrl = "https://indexer.invalid/comments/12";
            var failure = new InvalidOperationException("Edition lookup paused.");
            failure.Data["stage"] = "fixture specification";
            if (hasReport)
            {
                failure.Data["report"] = "upstream report context";
            }

            if (hasParsed)
            {
                failure.Data["parsed"] = "upstream parser context";
            }

            _fail1.Setup(spec => spec.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Throws(failure);
            GivenSpecifications(_fail1);

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.RemoteBook.Release.Should().BeSameAs(report);
            decision.Rejected.Should().BeTrue();
            decision.Rejections.Should().ContainSingle().Which.Reason.Should().Be($"{_fail1.Object.GetType().Name}: {failure.Message}");
            messages.Logs.Should().Contain("Couldn't evaluate decision on Ada Vale - City Lights (2022) [EPUB] ");
            messages.Logs.Should().NotContain("Couldn't process release.");
            failure.Data["stage"].Should().Be("fixture specification");
            var releaseJson = (string)failure.Data["report"];
            releaseJson.Should().NotContain("\r").And.NotContain("\n");
            releaseJson.Should().NotContain("indexer.invalid");
            Json.Deserialize<ReleaseInfo>(releaseJson).Title.Should().Be(title);
            decision.RemoteBook.Release.DownloadUrl.Should().Be("https://indexer.invalid/download/12");
            var parsedJson = (string)failure.Data["parsed"];
            parsedJson.Should().NotContain("\r").And.NotContain("\n");
            Json.Deserialize<ParsedBookInfo>(parsedJson).Quality.Quality.Should().Be(Quality.FLAC);
        }

        [Test]
        public void should_write_a_rejection_on_one_line_without_changing_its_reason()
        {
            using var logging = CaptureDecisionMessages(out var messages);
            const string reason = "Edition temporarily unavailable\r\n\tTry again later.";
            _fail1.Setup(spec => spec.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Reject(reason));
            GivenSpecifications(_fail1);

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.Rejections.Should().ContainSingle().Which.Reason.Should().Be(reason);
            messages.Logs.Should().Contain("Release rejected for the following reasons: [Permanent] Edition temporarily unavailable Try again later.");
        }

        private LogFactory CaptureDecisionMessages(out MemoryTarget messages)
        {
            messages = new MemoryTarget { Layout = "${message}" };
            var config = new LoggingConfiguration();
            config.AddRule(LogLevel.Debug, LogLevel.Error, messages);
            var factory = new LogFactory { Configuration = config };
            Mocker.SetConstant(factory.GetLogger(nameof(DownloadDecisionMaker)));
            return factory;
        }

        [Test]
        public void should_call_all_specifications()
        {
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetRssDecision(_reports).ToList();

            _fail1.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _fail2.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _fail3.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _pass1.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _pass2.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _pass3.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
        }

        [Test]
        public void should_call_delayed_specifications_if_non_delayed_passed()
        {
            GivenSpecifications(_pass1, _failDelayed1);

            Subject.GetRssDecision(_reports).ToList();
            _failDelayed1.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
        }

        [Test]
        public void should_not_call_delayed_specifications_if_non_delayed_failed()
        {
            GivenSpecifications(_fail1, _failDelayed1);

            Subject.GetRssDecision(_reports).ToList();

            _failDelayed1.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Never());
        }

        [Test]
        public void should_return_rejected_if_single_specs_fail()
        {
            GivenSpecifications(_fail1);

            var result = Subject.GetRssDecision(_reports);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_of_specs_fail()
        {
            GivenSpecifications(_pass1, _fail1, _pass2, _pass3);

            var result = Subject.GetRssDecision(_reports);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_pass_if_all_specs_pass()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetRssDecision(_reports);

            result.Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_have_same_number_of_rejections_as_specs_that_failed()
        {
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            var result = Subject.GetRssDecision(_reports);
            result.Single().Rejections.Should().HaveCount(3);
        }

        [Test]
        public void should_not_attempt_to_map_book_if_not_parsable()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            _reports[0].Title = "Not parsable";

            Subject.GetRssDecision(_reports).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Never());

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
        }

        [Test]
        public void should_not_attempt_to_map_book_if_author_title_is_blank()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            _reports[0].Title = "2013 - Night Visions";

            var results = Subject.GetRssDecision(_reports).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Never());

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());

            results.Should().BeEmpty();
        }

        [Test]
        public void should_return_rejected_result_for_unparsable_search()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            _reports[0].Title = "1937 - Snow White and the Seven Dwarves";

            var author = new Author { Name = "Some Author" };
            var books = new List<Book>
            {
                new Book
                {
                    Title = "Some Book",
                    Editions = new List<Edition>
                    {
                        new Edition { Title = "Some Edition Title" }
                    }
                }
            };

            Subject.GetSearchDecision(_reports, new BookSearchCriteria { Author = author, Books = books }).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Never());

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
        }

        [Test]
        public void should_not_attempt_to_make_decision_if_author_is_unknown()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteBook.Author = null;

            Subject.GetRssDecision(_reports);

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
        }

        [Test]
        public void broken_report_shouldnt_blowup_the_process()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IParsingService>().Setup(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                     .Throws<TestException>();

            _reports = new List<ReleaseInfo>
                {
                    new ReleaseInfo { Title = "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" },
                    new ReleaseInfo { Title = "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" },
                    new ReleaseInfo { Title = "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" }
                };

            Subject.GetRssDecision(_reports);

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Exactly(_reports.Count));

            ExceptionVerification.ExpectedErrors(3);
        }

        [Test]
        public void should_return_unknown_author_rejection_if_author_is_unknown()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteBook.Author = null;

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);
        }

        [Test]
        public void should_only_include_reports_for_requested_books()
        {
            var author = Builder<Author>.CreateNew().Build();

            var books = Builder<Book>.CreateListOfSize(2)
                .All()
                .With(v => v.AuthorId, author.Id)
                .With(v => v.Author, new LazyLoaded<Author>(author))
                .BuildList();

            var criteria = new AuthorSearchCriteria { Books = books.Take(1).ToList() };

            var reports = books.Select(v =>
                new ReleaseInfo()
                {
                    Title = string.Format("{0}-{1}[FLAC][2017][DRONE]", author.Name, v.Title)
                }).ToList();

            Mocker.GetMock<IParsingService>()
                .Setup(v => v.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                .Returns<ParsedBookInfo, SearchCriteriaBase>((p, c) =>
                    new RemoteBook
                    {
                        DownloadAllowed = true,
                        ParsedBookInfo = p,
                        Author = author,
                        Books = books.Where(v => v.Title == p.BookTitle).ToList()
                    });

            Mocker.SetConstant<IEnumerable<IDecisionEngineSpecification>>(new List<IDecisionEngineSpecification>
            {
                Mocker.Resolve<NzbDrone.Core.DecisionEngine.Specifications.Search.BookRequestedSpecification>()
            });

            var decisions = Subject.GetSearchDecision(reports, criteria);

            var approvedDecisions = decisions.Where(v => v.Approved).ToList();

            approvedDecisions.Count.Should().Be(1);
        }

        [Test]
        public void should_not_allow_download_if_author_is_unknown()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteBook.Author = null;

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);

            result.First().RemoteBook.DownloadAllowed.Should().BeFalse();
        }

        [Test]
        public void should_not_allow_download_if_no_books_found()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteBook.Books = new List<Book>();

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);

            result.First().RemoteBook.DownloadAllowed.Should().BeFalse();
        }

        [Test]
        public void should_return_a_decision_when_exception_is_caught()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IParsingService>().Setup(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                     .Throws<TestException>();

            _reports = new List<ReleaseInfo>
                {
                    new ReleaseInfo { Title = "Alien Ant Farm - TruAnt (FLAC) DRONE" },
                };

            Subject.GetRssDecision(_reports).Should().HaveCount(1);

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
