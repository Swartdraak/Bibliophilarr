using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataProviderSettings : ModelBase
    {
        public string ProviderName { get; set; }
        public bool IsEnabled { get; set; }
        public int Priority { get; set; }
    }
}
