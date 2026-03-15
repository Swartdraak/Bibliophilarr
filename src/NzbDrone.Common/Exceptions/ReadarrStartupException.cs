using System;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Exceptions
{
    public class ReadarrStartupException : NzbDroneException
    {
        public ReadarrStartupException(string message, params object[] args)
            : base(BuildInfo.AppName + " failed to start: " + string.Format(message, args))
        {
        }

        public ReadarrStartupException(string message)
            : base(BuildInfo.AppName + " failed to start: " + message)
        {
        }

        public ReadarrStartupException()
            : base(BuildInfo.AppName + " failed to start")
        {
        }

        public ReadarrStartupException(Exception innerException, string message, params object[] args)
            : base(BuildInfo.AppName + " failed to start: " + string.Format(message, args), innerException)
        {
        }

        public ReadarrStartupException(Exception innerException, string message)
            : base(BuildInfo.AppName + " failed to start: " + message, innerException)
        {
        }

        public ReadarrStartupException(Exception innerException)
            : base(BuildInfo.AppName + " failed to start: " + innerException.Message)
        {
        }
    }
}
