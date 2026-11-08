using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using NzbDrone.Common.Exceptions;
using NzbDrone.Host;

namespace NzbDrone.App.Test
{
    // Issue #364 regression: RejectLegacyConfigurationKeys must reject only the
    // pre-rebrand (Readarr) namespace. The active namespace is Bibliophilarr:*:
    // Bootstrap.CreateConsoleHostBuilder reads Bibliophilarr:Server:Port (the
    // target of the documented BIBLIOPHILARR_SERVER__PORT env var, mapped via
    // AddEnvironmentVariables) and ConfigureOptions binds Bibliophilarr:{App,
    // Server, Auth, Log, Postgres, Update}. While the guard rejected the active
    // namespace itself, any env-var override (e.g. BIBLIOPHILARR_SERVER__PORT)
    // made the app throw BibliophilarrStartupException at startup, or silently
    // fall through to defaults when the key never resolved.
    [TestFixture]
    public class BootstrapIssue364Fixture
    {
        private static IConfiguration BuildConfig(params KeyValuePair<string, string>[] pairs)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(pairs.ToList())
                .Build();

            // Private static method in production; invoked through reflection so
            // the regression fixture compiles against the base (unfixed) tree as
            // well as the fixed tree.
            var method = typeof(Bootstrap).GetMethod("RejectLegacyConfigurationKeys", BindingFlags.Static | BindingFlags.NonPublic);

            try
            {
                method.Invoke(null, new object[] { config });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // Re-throw the wrapped exception so Assert.Throws sees the real
                // BibliophilarrStartupException, not the reflection wrapper.
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }

            return config;
        }

        private static string BuildUrl(string scheme, string bindAddress, int port)
        {
            // Bootstrap.BuildUrl's exact contract (string interpolation), kept
            // local so the fixture compiles against the base tree where the
            // helper is private.
            return $"{scheme}://{bindAddress}:{port}";
        }

        [Test]
        public void should_honor_env_var_port_override_for_server_binding()
        {
            // Mirrors the documented BIBLIOPHILARR_SERVER__PORT=8811 env-var
            // surface: AddEnvironmentVariables maps it to the active
            // Bibliophilarr:Server:Port key that CreateConsoleHostBuilder reads
            // for the Kestrel bind port.
            var port = BuildConfig(new KeyValuePair<string, string>("Bibliophilarr:Server:Port", "8811"))
                .GetValue("Bibliophilarr:Server:Port", 8787);
            Assert.Multiple(() =>
            {
                Assert.That(port, Is.EqualTo(8811), "env-var override must win over the 8787 default");
                Assert.That(BuildUrl("http", "*", port), Is.EqualTo("http://*:8811"), "bind URL must carry the env-var port");
            });
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
