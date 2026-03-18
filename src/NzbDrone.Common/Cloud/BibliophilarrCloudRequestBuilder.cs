using NzbDrone.Common.Http;

namespace NzbDrone.Common.Cloud
{
    public interface IBibliophilarrCloudRequestBuilder
    {
        IHttpRequestBuilderFactory Services { get; }
        IHttpRequestBuilderFactory Metadata { get; }
    }

    public class BibliophilarrCloudRequestBuilder : IBibliophilarrCloudRequestBuilder
    {
        public BibliophilarrCloudRequestBuilder()
        {
            //TODO: Create Update Endpoint
            Services = new HttpRequestBuilder("https://services.bibliophilarr.org/v1/")
                .CreateFactory();

            Metadata = new HttpRequestBuilder("https://api.bookinfo.club/v1/{route}")
                .CreateFactory();
        }

        public IHttpRequestBuilderFactory Services { get; }

        public IHttpRequestBuilderFactory Metadata { get; }
    }
}
