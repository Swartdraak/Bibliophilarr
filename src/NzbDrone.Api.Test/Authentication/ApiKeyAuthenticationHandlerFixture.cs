using System;
using System.Threading.Tasks;
using Bibliophilarr.Http.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class ApiKeyAuthenticationHandlerFixture
    {
        private const string HeaderName = "X-Api-Key";
        private const string QueryName = "apikey";

        [Test]
        public async Task persisted_key_should_authenticate_after_cold_boot_finalization()
        {
            // Cold-boot defect (issue #309): the handler used to capture
            // config.ApiKey exactly once in its constructor. On a fresh data
            // directory the value captured at construction time is a
            // pre-finalization key, while startup later finalizes (persists) a
            // different ApiKey into config.xml. Because the old one-shot cache
            // was never refreshed, the persisted key could not authenticate
            // until a restart.
            //
            // The ColdBootConfigProvider models exactly that: the read at
            // construction returns the pre-finalization key, and after
            // finalization every subsequent read returns the persisted key.
            // The client presents the persisted key; it must authenticate.
            const string preFinalizationKey = "pre-finalization-key-captured-at-construction";
            const string persistedKey = "persisted-key-finalized-during-startup";

            var config = new ColdBootConfigProvider(preFinalizationKey, persistedKey);

            var handler = CreateHandler(config.Object, persistedKey);
            config.MarkFinalized();

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeTrue();
        }

        [Test]
        public async Task wrong_key_should_not_authenticate()
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.ApiKey).Returns("the-only-valid-key");

            var handler = CreateHandler(config.Object, "an-attacker-guess");

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeFalse();
        }

        [Test]
        public async Task missing_key_should_not_authenticate()
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.ApiKey).Returns("the-only-valid-key");

            var handler = CreateHandler(config.Object, null);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeFalse();
        }

        [Test]
        public async Task blank_configured_key_should_not_authenticate_anything()
        {
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.ApiKey).Returns(string.Empty);

            var handler = CreateHandler(config.Object, string.Empty);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeFalse();
        }

        [TestCase(HeaderName)]
        [TestCase(QueryName)]
        [TestCase("Bearer")]
        [TestCase("bEaReR")]
        public async Task supported_api_key_transport_should_authenticate(string source)
        {
            const string persistedKey = "persisted-key-finalized-during-startup";
            var config = Mock.Of<IConfigFileProvider>(c => c.ApiKey == persistedKey);
            var handler = source switch
            {
                HeaderName => CreateHandler(config, persistedKey),
                QueryName => CreateHandler(config, null, presentedQueryKey: persistedKey),
                _ => CreateHandler(config, null, authorizationHeader: $"{source} {persistedKey}")
            };

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeTrue();
        }

        [TestCase("opaque-fixture-key")]
        [TestCase("Basic fixture-value")]
        [TestCase("Digest fixture-value")]
        public async Task unsupported_authorization_format_should_not_authenticate(string authorizationHeader)
        {
            var config = Mock.Of<IConfigFileProvider>(c => c.ApiKey == authorizationHeader);
            var handler = CreateHandler(config, null, authorizationHeader: authorizationHeader);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeFalse();
        }

        private static ApiKeyAuthenticationHandler CreateHandler(
            IConfigFileProvider config,
            string presentedKey,
            string authorizationHeader = null,
            string presentedQueryKey = null)
        {
            var context = new DefaultHttpContext();

            if (presentedKey != null)
            {
                context.Request.Headers[HeaderName] = presentedKey;
            }

            if (authorizationHeader != null)
            {
                context.Request.Headers["Authorization"] = authorizationHeader;
            }

            if (presentedQueryKey != null)
            {
                context.Request.QueryString = QueryString.Create(QueryName, presentedQueryKey);
            }

            var options = new ApiKeyAuthenticationOptions
            {
                HeaderName = HeaderName,
                QueryName = QueryName
            };

            var handler = new ApiKeyAuthenticationHandler(
                new StaticOptionsMonitor<ApiKeyAuthenticationOptions>(options),
                LoggerFactory.Create(builder => { }),
                System.Text.Encodings.Web.UrlEncoder.Default,
                config);

            var scheme = new AuthenticationScheme("API", "API Key", typeof(ApiKeyAuthenticationHandler));
            handler.InitializeAsync(scheme, context).GetAwaiter().GetResult();
            return handler;
        }

        private sealed class ColdBootConfigProvider : Mock<IConfigFileProvider>
        {
            private readonly string _persistedKey;
            private bool _finalized;

            public ColdBootConfigProvider(string preFinalizationKey, string persistedKey)
            {
                _persistedKey = persistedKey;
                SetupGet(c => c.ApiKey).Returns(() => _finalized ? _persistedKey : preFinalizationKey);
            }

            public void MarkFinalized()
            {
                _finalized = true;
            }
        }

        private sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
        {
            public StaticOptionsMonitor(TOptions currentValue)
            {
                CurrentValue = currentValue;
            }

            public TOptions CurrentValue { get; }

            public TOptions Get(string name) => CurrentValue;

            public IDisposable OnChange(Action<TOptions, string> listener) => Mock.Of<IDisposable>();
        }
    }
}
