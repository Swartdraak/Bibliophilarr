using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Bibliophilarr.Http.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using AuthenticationService = Bibliophilarr.Http.Authentication.IAuthenticationService;
using AuthenticationUser = NzbDrone.Core.Authentication.User;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class BasicAuthenticationHandlerFixture
    {
        private AuthenticationScheme _scheme;
        private Mock<AuthenticationService> _authService;

        [SetUp]
        public void SetUp()
        {
            _scheme = new AuthenticationScheme("Basic", "Basic", typeof(BasicAuthenticationHandler));
            _authService = new Mock<AuthenticationService>();
        }

        [TestCase("Basic !!!")]
        [TestCase("Basic dXNlcm5hbWU=")]
        public async Task malformed_basic_headers_should_fail_without_throwing_and_challenge_with_401(string authorizationHeader)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Authorization = authorizationHeader;

            var handler = CreateHandler(context);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeFalse();
            _authService.Verify(v => v.Login(It.IsAny<HttpRequest>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            await handler.ChallengeAsync(new AuthenticationProperties());

            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            context.Response.Headers.WWWAuthenticate.ToString().Should().Contain("Basic realm=");
        }

        [Test]
        public async Task valid_basic_header_should_authenticate_successfully()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Authorization = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:admin"))}";

            _authService.Setup(v => v.Login(It.IsAny<HttpRequest>(), "admin", "admin"))
                .Returns(new AuthenticationUser
                {
                    Username = "admin",
                    Identifier = Guid.NewGuid()
                });

            var handler = CreateHandler(context);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeTrue();
            _authService.Verify(v => v.Login(It.IsAny<HttpRequest>(), "admin", "admin"), Times.Once());
        }

        private BasicAuthenticationHandler CreateHandler(DefaultHttpContext context)
        {
            var handler = new BasicAuthenticationHandler(
                _authService.Object,
                new StaticOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
                LoggerFactory.Create(builder => { }),
                UrlEncoder.Default);

            handler.InitializeAsync(_scheme, context).GetAwaiter().GetResult();
            return handler;
        }

        private sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
        {
            public StaticOptionsMonitor(TOptions currentValue)
            {
                CurrentValue = currentValue;
            }

            public TOptions CurrentValue { get; }

            public TOptions Get(string name)
            {
                return CurrentValue;
            }

            public IDisposable OnChange(Action<TOptions, string> listener)
            {
                return Mock.Of<IDisposable>();
            }
        }
    }
}
