using System.Collections.Generic;
using System.Net.Http;
using Bibliophilarr.Api.V1.DownloadClient;

namespace NzbDrone.Integration.Test.Client
{
    public class DownloadClientClient : ClientBase<DownloadClientResource>
    {
        public DownloadClientClient(HttpClient httpClient, string apiKey)
            : base(httpClient, apiKey)
        {
        }

        public List<DownloadClientResource> Schema()
        {
            var request = BuildRequest("/schema");
            return Get<List<DownloadClientResource>>(request);
        }
    }
}
