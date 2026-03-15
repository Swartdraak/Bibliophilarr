using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataProviderSettingsRepository : IBasicRepository<MetadataProviderSettings>
    {
        MetadataProviderSettings FindByProviderName(string providerName);
    }

    public class MetadataProviderSettingsRepository : BasicRepository<MetadataProviderSettings>, IMetadataProviderSettingsRepository
    {
        public MetadataProviderSettingsRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public MetadataProviderSettings FindByProviderName(string providerName)
        {
            return Query(x => x.ProviderName == providerName).SingleOrDefault();
        }
    }
}
