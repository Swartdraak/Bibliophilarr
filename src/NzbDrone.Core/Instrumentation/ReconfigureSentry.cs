using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Instrumentation
{
    public class ReconfigureSentry : IHandleAsync<ApplicationStartedEvent>
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IMainDatabase _database;

        public ReconfigureSentry(IConfigFileProvider configFileProvider,
                                 IPlatformInfo platformInfo,
                                 IMainDatabase database)
        {
            _configFileProvider = configFileProvider;
            _ = platformInfo;
            _database = database;
        }

        public void Reconfigure()
        {
            _ = _configFileProvider.Branch;
            _ = _database.Version;
        }

        public void HandleAsync(ApplicationStartedEvent message)
        {
            Reconfigure();
        }
    }
}
