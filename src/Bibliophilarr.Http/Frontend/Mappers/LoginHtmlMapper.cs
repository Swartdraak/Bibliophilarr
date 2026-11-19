using System;
using System.IO;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Http.Frontend.Mappers
{
    public class LoginHtmlMapper : HtmlMapperBase
    {
        public LoginHtmlMapper(IAppFolderInfo appFolderInfo,
                               IDiskProvider diskProvider,
                               Lazy<ICacheBreakerProvider> cacheBreakProviderFactory,
                               IConfigFileProvider configFileProvider,
                               Logger logger)
            : base(diskProvider, cacheBreakProviderFactory, logger)
        {
            HtmlPath = Path.Combine(appFolderInfo.StartUpFolder, configFileProvider.UiFolder, "login.html");
            UrlBase = configFileProvider.UrlBase;
        }

        public override string Map(string resourceUrl)
        {
            return HtmlPath;
        }

        public override bool CanHandle(string resourceUrl)
        {
            return resourceUrl.StartsWith("/login");
        }

        public IActionResult GetSetupResponse(string token, bool invalid)
        {
            var html = GetHtmlText()
                .Replace("<div id=\"login-form\">", "<div id=\"login-form\" hidden>")
                .Replace("<div id=\"setup-form\" hidden>", "<div id=\"setup-form\">")
                .Replace("__FIRST_ACCOUNT_TOKEN__", HtmlEncoder.Default.Encode(token))
                .Replace("__FIRST_ACCOUNT_ERROR_CLASS__", invalid ? "login-failed" : "login-failed hidden");

            return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8" };
        }
    }
}
