using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Options;
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
        [TestCase("8811", "invalid", 8811)]
        [TestCase(null, null, null)]
        [NonParallelizable]
        public void should_apply_the_same_environment_port_to_the_listener_and_server_options(string canonical, string historical, int? expectedOverride)
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
                var expectedListener = expectedOverride ?? 8787;

                Assert.Multiple(() =>
                {
                    Assert.That(port, Is.EqualTo(expectedListener));
                    Assert.That(Bootstrap.BuildUrl("http", "*", port), Is.EqualTo($"http://*:{expectedListener}"));
                    Assert.That(GetServerOptions(config).Port, Is.EqualTo(expectedOverride));
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable(canonicalName, previousCanonical);
                Environment.SetEnvironmentVariable(historicalName, previousHistorical);
            }
        }

        [TestCase(null, 8790, null)]
        [TestCase("8811", 8811, 8811)]
        public void should_keep_the_flat_xml_port_as_the_persisted_fallback(string historical, int expectedListener, int? expectedOverride)
        {
            var config = BuildConfig(
                new KeyValuePair<string, string>("Port", "8790"),
                new KeyValuePair<string, string>("Bibliophilarr_Server:Port", historical));

            Assert.Multiple(() =>
            {
                Assert.That(Bootstrap.GetServerPort(config), Is.EqualTo(expectedListener));
                Assert.That(GetServerOptions(config).Port, Is.EqualTo(expectedOverride));
                Assert.That(config["Port"], Is.EqualTo("8790"));
            });
        }

        [TestCase(null, null, 8811, "127.0.0.1")]
        [TestCase("8812", "127.0.0.2", 8812, "127.0.0.2")]
        [NonParallelizable]
        public void should_apply_nested_xml_settings_to_the_listener_with_environment_precedence(
            string portOverride, string addressOverride, int expectedPort, string expectedAddress)
        {
            var directory = Directory.CreateTempSubdirectory();
            var variables = new Dictionary<string, string>
            {
                ["BIBLIOPHILARR__SERVER__PORT"] = portOverride,
                ["BIBLIOPHILARR__SERVER__BINDADDRESS"] = addressOverride,
                ["BIBLIOPHILARR_SERVER__PORT"] = null,
                ["PORT"] = null,
                ["BINDADDRESS"] = null
            };
            var previous = new Dictionary<string, string>();

            try
            {
                foreach (var (name, value) in variables)
                {
                    previous[name] = Environment.GetEnvironmentVariable(name);
                    Environment.SetEnvironmentVariable(name, value);
                }

                var configPath = Path.Combine(directory.FullName, "config.xml");
                File.WriteAllText(configPath, "<Config><Server><Port>8811</Port><BindAddress>127.0.0.1</BindAddress></Server></Config>");

                var config = Bootstrap.GetConfiguration(new StartupContext($"--data={directory.FullName}"));
                var port = Bootstrap.GetServerPort(config);
                var address = Bootstrap.GetServerBindAddress(config);

                Assert.Multiple(() =>
                {
                    Assert.That(port, Is.EqualTo(expectedPort));
                    Assert.That(address, Is.EqualTo(expectedAddress));
                    Assert.That(Bootstrap.BuildUrl("http", address, port), Is.EqualTo($"http://{expectedAddress}:{expectedPort}"));
                });
            }
            finally
            {
                foreach (var (name, value) in previous)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }

                directory.Delete(true);
            }
        }

        [TestCase("invalid", "8811")]
        [TestCase(null, "invalid")]
        public void should_reject_a_malformed_effective_port_in_both_configuration_paths(string canonical, string historical)
        {
            var config = BuildConfig(
                new KeyValuePair<string, string>("Bibliophilarr:Server:Port", canonical),
                new KeyValuePair<string, string>("Bibliophilarr_Server:Port", historical));

            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() => Bootstrap.GetServerPort(config));
                Assert.Throws<InvalidOperationException>(() => GetServerOptions(config));
            });
        }

        private static ServerOptions GetServerOptions(IConfiguration config)
        {
            var services = new ServiceCollection();
            Bootstrap.ConfigureServerOptions(services, config);

            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<ServerOptions>>().Value;
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
