using System;
using System.Collections.Generic;
using System.Net;
using FluentValidation.Results;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Bibliophilarr
{
    public interface IBibliophilarrV1Proxy
    {
        List<BibliophilarrAuthor> GetAuthors(BibliophilarrSettings settings);
        List<BibliophilarrBook> GetBooks(BibliophilarrSettings settings);
        List<BibliophilarrProfile> GetProfiles(BibliophilarrSettings settings);
        List<BibliophilarrRootFolder> GetRootFolders(BibliophilarrSettings settings);
        List<BibliophilarrTag> GetTags(BibliophilarrSettings settings);
        ValidationFailure Test(BibliophilarrSettings settings);
    }

    public class BibliophilarrV1Proxy : IBibliophilarrV1Proxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public BibliophilarrV1Proxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public List<BibliophilarrAuthor> GetAuthors(BibliophilarrSettings settings)
        {
            return Execute<BibliophilarrAuthor>("/api/v1/author", settings);
        }

        public List<BibliophilarrBook> GetBooks(BibliophilarrSettings settings)
        {
            return Execute<BibliophilarrBook>("/api/v1/book", settings);
        }

        public List<BibliophilarrProfile> GetProfiles(BibliophilarrSettings settings)
        {
            return Execute<BibliophilarrProfile>("/api/v1/qualityprofile", settings);
        }

        public List<BibliophilarrRootFolder> GetRootFolders(BibliophilarrSettings settings)
        {
            return Execute<BibliophilarrRootFolder>("api/v1/rootfolder", settings);
        }

        public List<BibliophilarrTag> GetTags(BibliophilarrSettings settings)
        {
            return Execute<BibliophilarrTag>("/api/v1/tag", settings);
        }

        public ValidationFailure Test(BibliophilarrSettings settings)
        {
            try
            {
                GetAuthors(settings);
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.Error(ex, "API Key is invalid");
                    return new ValidationFailure("ApiKey", "API Key is invalid");
                }

                if (ex.Response.HasHttpRedirect)
                {
                    _logger.Error(ex, "Bibliophilarr returned redirect and is invalid");
                    return new ValidationFailure("BaseUrl", "Bibliophilarr URL is invalid, are you missing a URL base?");
                }

                _logger.Error(ex, "Unable to connect to import list.");
                return new ValidationFailure(string.Empty, $"Unable to connect to import list: {ex.Message}. Check the log surrounding this error for details.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to connect to import list.");
                return new ValidationFailure(string.Empty, $"Unable to connect to import list: {ex.Message}. Check the log surrounding this error for details.");
            }

            return null;
        }

        private List<TResource> Execute<TResource>(string resource, BibliophilarrSettings settings)
        {
            if (settings.BaseUrl.IsNullOrWhiteSpace() || settings.ApiKey.IsNullOrWhiteSpace())
            {
                return new List<TResource>();
            }

            var baseUrl = settings.BaseUrl.TrimEnd('/');

            var request = new HttpRequestBuilder(baseUrl).Resource(resource)
                .Accept(HttpAccept.Json)
                .SetHeader("X-Api-Key", settings.ApiKey)
                .Build();

            var response = _httpClient.Get(request);

            if ((int)response.StatusCode >= 300)
            {
                throw new HttpException(response);
            }

            var results = JsonConvert.DeserializeObject<List<TResource>>(response.Content);

            return results;
        }
    }
}
