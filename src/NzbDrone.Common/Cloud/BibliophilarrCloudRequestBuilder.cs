using System;
using NzbDrone.Common.Http;

namespace NzbDrone.Common.Cloud
{
    public interface IBibliophilarrCloudRequestBuilder
    {
        IHttpRequestBuilderFactory Services { get; }
        IHttpRequestBuilderFactory Metadata { get; }
        bool HasServices { get; }
    }

    public class BibliophilarrCloudRequestBuilder : IBibliophilarrCloudRequestBuilder
    {
        private const string ServicesUrlEnvironmentVariable = "BIBLIOPHILARR_SERVICES_URL";

        public BibliophilarrCloudRequestBuilder()
        {
            var servicesUrl = Environment.GetEnvironmentVariable(ServicesUrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(servicesUrl))
            {
                Services = new HttpRequestBuilder(NormalizeServicesUrl(servicesUrl))
                    .CreateFactory();
                HasServices = true;
            }

            Metadata = new HttpRequestBuilder("https://openlibrary.org/{route}")
                .CreateFactory();
        }

        public IHttpRequestBuilderFactory Services { get; }

        public IHttpRequestBuilderFactory Metadata { get; }

        public bool HasServices { get; }

        private static string NormalizeServicesUrl(string rawUrl)
        {
            var trimmed = rawUrl.Trim().TrimEnd('/');

            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed + "/";
            }

            return trimmed + "/v1/";
        }
    }
}
