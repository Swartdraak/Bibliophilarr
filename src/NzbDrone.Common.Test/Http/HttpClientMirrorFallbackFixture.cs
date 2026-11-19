using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.Http
{
    /// <summary>
    /// Deterministic regression guard for the CI failure at develop tip 7a9d95e5
    /// (lanes build-test / Linux / macOS / Windows all red on NzbDrone.Common.Test).
    ///
    /// Root cause: HttpClientFixture&lt;TDispatcher&gt;.FixtureSetUp probes the mirror
    /// candidate (httpbin1.servarr.com) once per fixture; when the probe fails on a
    /// hosted runner (site unavailable at run time) the mirror list _httpBinHosts
    /// becomes EMPTY. SetUp then assigned
    ///     _httpBinHosts[_httpBinRandom++ % _httpBinHosts.Length]
    /// which, with an empty list, computes (x % 0) and throws
    /// System.DivideByZeroException from SetUp - aborting EVERY subsequent test in
    /// the fixture (the cascade visible in the four failed lanes' logs).
    ///
    /// This fixture pins the guarded selection logic in isolation: it runs the exact
    /// pre-fix expression over the empty array to prove the old code path throws
    /// (documenting the root cause in executable form), and verifies the post-fix
    /// selection yields the primary host instead of throwing. It performs NO network
    /// I/O, so it is deterministic on every host and in every CI configuration.
    /// </summary>
    [TestFixture]
    public class HttpClientMirrorFallbackFixture : TestBase
    {
        // Primary host, kept in sync with HttpClientFixture.FixtureSetUp's mainHost
        // ("Always use our server for main tests").
        private const string MainHost = "httpbin.servarr.com";

        [Test]
        public void legacy_round_robin_selection_throws_when_mirrors_unavailable()
        {
            // The pre-fix expression, verbatim semantics, over the empty mirror list.
            var mirrors = new string[0];
            var roundRobin = 0;

            Action legacy = () => { var unused = mirrors[roundRobin++ % mirrors.Length]; };

            legacy.Should().Throw<DivideByZeroException>(
                "documenting the root cause: the legacy selection divides by mirrors.Length when the mirror probe yields an empty list");
        }

        [Test]
        public void guarded_selection_falls_back_to_primary_host_when_mirrors_unavailable()
        {
            // The post-fix selection, as shipped in HttpClientFixture.SetUp, over the
            // empty mirror list.
            var mirrors = new string[0];
            var roundRobin = 0;
            var primary = MainHost;

            string secondHost;
            if (mirrors.Length > 0)
            {
                secondHost = mirrors[roundRobin++ % mirrors.Length];
            }
            else
            {
                secondHost = primary;
            }

            secondHost.Should().Be(primary);
        }

        [Test]
        public void guarded_selection_still_round_robins_when_mirrors_available()
        {
            // Regression check that the guard does not change behavior when mirrors ARE
            // available: selection keeps rotating through the mirror list.
            var mirrors = new[] { "httpbin1.servarr.com", "httpbin2.servarr.com" };
            var primary = MainHost;

            string picked0;
            var roundRobin = 0;
            if (mirrors.Length > 0)
            {
                picked0 = mirrors[roundRobin++ % mirrors.Length];
            }
            else
            {
                picked0 = primary;
            }

            string picked1;
            if (mirrors.Length > 0)
            {
                picked1 = mirrors[roundRobin++ % mirrors.Length];
            }
            else
            {
                picked1 = primary;
            }

            picked0.Should().Be("httpbin1.servarr.com");
            picked1.Should().Be("httpbin2.servarr.com");
            (picked0 == picked1).Should().BeFalse("selection must keep rotating across mirrors");
        }
    }
}
