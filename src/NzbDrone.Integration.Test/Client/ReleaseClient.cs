using System.Net.Http;
using Bibliophilarr.Api.V1.Indexers;

namespace NzbDrone.Integration.Test.Client
{
    public class ReleaseClient : ClientBase<ReleaseResource>
    {
        public ReleaseClient(HttpClient httpClient, string apiKey)
            : base(httpClient, apiKey)
        {
        }
    }
}
