using System.Collections.Generic;
using System.Net.Http;
using Bibliophilarr.Api.V1.Notifications;

namespace NzbDrone.Integration.Test.Client
{
    public class NotificationClient : ClientBase<NotificationResource>
    {
        public NotificationClient(HttpClient httpClient, string apiKey)
            : base(httpClient, apiKey)
        {
        }

        public List<NotificationResource> Schema()
        {
            var request = BuildRequest("/schema");
            return Get<List<NotificationResource>>(request);
        }
    }
}
