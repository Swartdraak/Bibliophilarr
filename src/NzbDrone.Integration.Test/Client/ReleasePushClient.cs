using System.Net.Http;
using Bibliophilarr.Api.V1.Indexers;

namespace NzbDrone.Integration.Test.Client
{
    public class ReleasePushClient : ClientBase<ReleaseResource>
    {
        public ReleasePushClient(HttpClient httpClient, string apiKey)
            : base(httpClient, apiKey, "release/push")
        {
        }
    }
}
