using System.Collections.Generic;
using System.Net.Http;
using Bibliophilarr.Api.V1.Indexers;

namespace NzbDrone.Integration.Test.Client
{
    public class IndexerClient : ClientBase<IndexerResource>
    {
        public IndexerClient(HttpClient httpClient, string apiKey)
            : base(httpClient, apiKey)
        {
        }

        public List<IndexerResource> Schema()
        {
            var request = BuildRequest("/schema");
            return Get<List<IndexerResource>>(request);
        }
    }
}
