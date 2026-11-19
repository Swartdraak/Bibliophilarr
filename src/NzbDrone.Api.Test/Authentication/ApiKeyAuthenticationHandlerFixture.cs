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

        [Test]
        public async Task key_via_bearer_authorization_header_should_authenticate()
        {
            const string persistedKey = "persisted-key-finalized-during-startup";

            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(c => c.ApiKey).Returns(persistedKey);

            var handler = CreateHandler(config.Object, persistedKey, viaAuthorizationHeader: true);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeTrue();
        }

        private static ApiKeyAuthenticationHandler CreateHandler(IConfigFileProvider config, string presentedKey, bool viaAuthorizationHeader = false)
        {
            var context = new DefaultHttpContext();

            if (viaAuthorizationHeader)
            {
                context.Request.Headers["Authorization"] = $"Bearer {presentedKey}";
            }
            else if (presentedKey != null)
            {
                context.Request.Headers[HeaderName] = presentedKey;
            }

            var options = new ApiKeyAuthenticationOptions
            {
                HeaderName = HeaderName,
                QueryName = "apikey"
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
