using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Inventaire;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class InventaireSeriesDiscoveryFixture : CoreTest<InventaireSeriesDiscovery>
    {
        private const string Series = "wd:Q41";
        private const string Native = "inv:0f25f75dba901ddb9817c3e4bf001d85";
        private const string ScopedSeries = "inventaire:series:" + Series;
        private List<HttpRequest> _requests;

        [SetUp]
        public void PrepareInventaire()
        {
            _requests = new List<HttpRequest>();
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableInventaireFallback).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.MetadataProviderTimeoutSeconds).Returns(10);
        }

        [Test]
        public void should_return_distinct_exact_typed_series_and_ignore_a_mismatched_id_or_work()
        {
            Given(
                (HttpStatusCode.OK, """
                    {"results":[
                        {"type":"series","id":"Q41","uri":"wd:Q41","label":"Shared title"},
                        {"type":"series","id":"Q42","uri":"wd:Q42","label":"Shared title"},
                        {"type":"series","id":"Q999","uri":"wd:Q43","label":"Different ID"},
                        {"type":"works","id":"Q44","uri":"wd:Q44","label":"Wrong type"},
                        {"type":"series","id":"Q41","uri":"wd:Q41","label":"Repeated"}],
                      "total":5}
                    """));

            var output = Subject.Search("Shared title");

            output.Select(series => series.ForeignSeriesId).Should().Equal(ScopedSeries, "inventaire:series:wd:Q42");
            output.Should().OnlyContain(series => series.SourceProvider == "Inventaire");
            output[0].SourceUrl.Should().Be("https://inventaire.io/entity/wd:Q41");
            var request = _requests.Should().ContainSingle().Which;
            request.Url.Path.Should().Be("/api/search");
            request.Url.ToString().Should().Contain("types=series");
        }

        [Test]
        public void should_hydrate_exact_direct_work_claims_and_keep_wikidata_position_unknown()
        {
            Given(
                (HttpStatusCode.OK, Parts(
                    new JObject { ["uri"] = "wd:Q42", ["type"] = "work", ["ordinal"] = "1" },
                    new JObject { ["uri"] = Native, ["type"] = "work", ["ordinal"] = "3.5" },
                    new JObject { ["uri"] = "wd:Q43", ["type"] = "serie" },
                    new JObject { ["uri"] = "wd:Q44", ["type"] = "work", ["date"] = "2012" })),
                (HttpStatusCode.OK, Entities(
                    Entity(Series, "serie", "Fixture Series"),
                    Entity("wd:Q42", "work", "Œuvre Wikidata", Series, "1", "fr"),
                    Entity(Native, "work", "Native work", Series, "3.5"),
                    Entity("wd:Q43", "serie", "Nested series"),
                    Entity("wd:Q44", "work", "A different relationship"))));

            var books = Subject.GetMembers(ScopedSeries);

            books.Select(book => book.ForeignWorkId).Should().Equal("inventaire:work:" + Native, "inventaire:work:wd:Q42");
            books[0].Position.Should().Be("3.5");
            books[1].Position.Should().BeNull();
            books[1].Title.Should().Be("Œuvre Wikidata");
            books[0].SourceUrl.Should().Be("https://inventaire.io/entity/" + Native);
            _requests.Select(request => request.Url.Path).Should().Equal("/api/entities/serie-parts", "/api/entities/by-uris");
            _requests.Should().OnlyContain(request => !request.AllowAutoRedirect && request.IgnoreStoredOriginCredentials);
        }

        [Test]
        public void should_not_publish_partial_members_after_a_later_429()
        {
            Given((HttpStatusCode.OK, Parts(new JObject { ["uri"] = "wd:Q42", ["type"] = "work" })),
                (HttpStatusCode.TooManyRequests, """{"error":"fixture rate limit"}"""));

            Subject.GetMembers(ScopedSeries).Should().BeNull();
            _requests.Should().HaveCount(2);
        }

        [TestCase("""{"entities":{},"redirects":{}}""")]
        [TestCase("""{"entities":{},"redirects":{"wd:Q42":"wd:Q44"}}""")]
        [TestCase("""{"entities":{},"entities":{},"redirects":{}}""")]
        public void should_decline_missing_redirected_or_ambiguous_hydration(string envelope)
        {
            Given((HttpStatusCode.OK, Parts(new JObject { ["uri"] = "wd:Q42", ["type"] = "work" })),
                (HttpStatusCode.OK, envelope));

            Subject.GetMembers(ScopedSeries).Should().BeNull();
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_stop_before_hydration_on_an_over_budget_membership_list()
        {
            var dense = Enumerable.Range(100, 41)
                .Select(id => new JObject { ["uri"] = $"wd:Q{id}", ["type"] = "work" }).ToArray();
            Given((HttpStatusCode.OK, Parts(dense)));

            Subject.GetMembers(ScopedSeries).Should().BeNull();
            _requests.Should().ContainSingle();
        }

        [Test]
        public void should_never_dispatch_for_invalid_or_disabled_source()
        {
            Subject.GetMembers("inventaire:series:wd:Q0").Should().BeNull();
            Subject.GetMembers("hardcover:series:41").Should().BeNull();
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableInventaireFallback).Returns(false);
            Subject.GetMembers(ScopedSeries).Should().BeNull();
            Subject.Search("Fixture series").Should().BeNull();
            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_stop_between_parts_and_hydration_when_the_caller_disconnects()
        {
            using var caller = new CancellationTokenSource();
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request =>
            {
                _requests.Add(request);
                request.CancellationToken.CanBeCanceled.Should().BeTrue();
                request.ResponseStream.Write(Encoding.UTF8.GetBytes(Parts(new JObject { ["uri"] = "wd:Q42", ["type"] = "work" })));
                caller.Cancel();
                request.CancellationToken.IsCancellationRequested.Should().BeTrue();
                return new HttpResponse(request, new HttpHeader(), System.Array.Empty<byte>(), HttpStatusCode.OK);
            });

            Subject.GetMembers(ScopedSeries, caller.Token).Should().BeNull();
            _requests.Should().ContainSingle();
        }

        [Test]
        public void should_limit_success_body_during_receive()
        {
            Given((HttpStatusCode.OK, new string(' ', (256 * 1024) + 1)));

            Subject.Search("Fixture series").Should().BeNull();
            _requests.Should().ContainSingle().Which.DiscardNonOkResponseBody.Should().BeTrue();
        }

        private void Given(params (HttpStatusCode Status, string Body)[] responses)
        {
            var remaining = new Queue<(HttpStatusCode, string)>(responses);
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request =>
            {
                _requests.Add(request);
                var (status, body) = remaining.Dequeue();
                if (status == HttpStatusCode.OK)
                {
                    request.ResponseStream.Write(Encoding.UTF8.GetBytes(body));
                }

                return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, System.Array.Empty<byte>(), status);
            });
        }

        private static string Parts(params JObject[] entries) => new JObject { ["parts"] = new JArray(entries) }.ToString();

        private static JObject Entity(string uri, string type, string title, string seriesUri = null, string ordinal = null, string language = "en")
        {
            var claims = new JObject();
            if (seriesUri != null)
            {
                claims["wdt:P179"] = new JArray(seriesUri);
            }

            if (ordinal != null)
            {
                claims["wdt:P1545"] = new JArray(ordinal);
            }

            return new JObject
            {
                ["uri"] = uri,
                ["type"] = type,
                ["labels"] = new JObject { [language] = title },
                ["claims"] = claims
            };
        }

        private static string Entities(params JObject[] records)
        {
            var results = new JObject(records.Select(record => new JProperty(record.Value<string>("uri"), record)));
            return new JObject { ["entities"] = results, ["redirects"] = new JObject() }.ToString();
        }
    }
}
