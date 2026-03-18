using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Instrumentation
{
    public class InitializeLogger
    {
        public InitializeLogger(IOsInfo osInfo)
        {
            _ = osInfo;
        }

        public void Initialize()
        {
            // Sentry initialization has been removed; keep method for compatibility.
        }
    }
}
