using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using NzbDrone.Common.Exceptions;
using NzbDrone.Host;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class BootstrapIssue364Fixture
    {
        private static IConfiguration BuildConfig(params KeyValuePair<string, string>[] pairs)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
            Bootstrap.RejectLegacyConfigurationKeys(config);
            return config;
        }

        [TestCase(null, "8811", 8811)]
        [TestCase("8811", null, 8811)]
        [TestCase("8811", "8812", 8811)]
        [TestCase(null, null, 8787)]
        [NonParallelizable]
        public void should_resolve_the_server_port_from_environment(string canonical, string historical, int expected)
        {
            const string canonicalName = "BIBLIOPHILARR__SERVER__PORT";
            const string historicalName = "BIBLIOPHILARR_SERVER__PORT";
            var previousCanonical = Environment.GetEnvironmentVariable(canonicalName);
            var previousHistorical = Environment.GetEnvironmentVariable(historicalName);

            try
            {
                Environment.SetEnvironmentVariable(canonicalName, canonical);
                Environment.SetEnvironmentVariable(historicalName, historical);
                var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();

                var port = Bootstrap.GetServerPort(config);

                Assert.That(port, Is.EqualTo(expected));
                Assert.That(Bootstrap.BuildUrl("http", "*", port), Is.EqualTo($"http://*:{expected}"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(canonicalName, previousCanonical);
                Environment.SetEnvironmentVariable(historicalName, previousHistorical);
            }
        }

        [Test]
        public void should_not_reject_active_bibliophilarr_namespace_keys()
        {
            // AC(b): the active namespace the code reads must never be treated
            // as legacy — this exact shape is what the broken guard threw on.
            var config = BuildConfig(
                new KeyValuePair<string, string>("Bibliophilarr:Server:Port", "8811"),
                new KeyValuePair<string, string>("Bibliophilarr:Server:SslPort", "8812"),
                new KeyValuePair<string, string>("Bibliophilarr:Server:BindAddress", "*"),
                new KeyValuePair<string, string>("Bibliophilarr:Server:EnableSsl", "False"),
                new KeyValuePair<string, string>("Bibliophilarr:Server:SslCertPath", ""),
                new KeyValuePair<string, string>("Bibliophilarr:Server:SslCertPassword", ""),
                new KeyValuePair<string, string>("Bibliophilarr:App:InstanceName", "test"),
                new KeyValuePair<string, string>("Bibliophilarr:Log:LogLevel", "info"),
                new KeyValuePair<string, string>("Bibliophilarr:Auth:AuthenticationMethod", "forms"),
                new KeyValuePair<string, string>("Bibliophilarr:Postgres:Host", "localhost"),
                new KeyValuePair<string, string>("Bibliophilarr:Update:Branch", "develop"));

            Assert.Multiple(() =>
            {
                Assert.That(config.GetValue("Bibliophilarr:Server:Port", 0), Is.EqualTo(8811));
                Assert.That(config.GetValue("Bibliophilarr:Log:LogLevel", ""), Is.EqualTo("info"));
            });
        }

        [Test]
        public void should_still_reject_legacy_readarr_keys()
        {
            // AC(c): the pre-rebrand namespace must keep its hard rejection.
            Assert.Throws<BibliophilarrStartupException>(() => BuildConfig(
                new KeyValuePair<string, string>("Readarr:Server:Port", "8811")),
                "Readarr:* keys must still throw at startup");
        }

        [Test]
        public void should_reject_case_insensitive_legacy_readarr_keys()
        {
            Assert.Throws<BibliophilarrStartupException>(() => BuildConfig(
                new KeyValuePair<string, string>("readarr:server:port", "8811")),
                "legacy rejection must remain case-insensitive");
        }

        [Test]
        public void should_ignore_blank_legacy_values()
        {
            // Mirrors the pre-fix behavior for the legacy namespace: blank
            // values are not active configuration and must not trip the guard
            // (the non-empty-value filter in RejectLegacyConfigurationKeys).
            var config = BuildConfig(
                new KeyValuePair<string, string>("Readarr:Server:Port", ""),
                new KeyValuePair<string, string>("Readarr:Server:SslPort", "   "));

            Assert.Multiple(() =>
            {
                Assert.That(config["Readarr:Server:Port"], Is.EqualTo(""), "blank value stays configured but inert");
                Assert.That(config["Readarr:Server:SslPort"], Is.EqualTo("   "));
            });
        }
    }
}
