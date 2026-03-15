using System;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Exceptions
{
    public class BibliophilarrStartupException : NzbDroneException
    {
        public BibliophilarrStartupException(string message, params object[] args)
            : base(BuildInfo.AppName + " failed to start: " + string.Format(message, args))
        {
        }

        public BibliophilarrStartupException(string message)
            : base(BuildInfo.AppName + " failed to start: " + message)
        {
        }

        public BibliophilarrStartupException()
            : base(BuildInfo.AppName + " failed to start")
        {
        }

        public BibliophilarrStartupException(Exception innerException, string message, params object[] args)
            : base(BuildInfo.AppName + " failed to start: " + string.Format(message, args), innerException)
        {
        }

        public BibliophilarrStartupException(Exception innerException, string message)
            : base(BuildInfo.AppName + " failed to start: " + message, innerException)
        {
        }

        public BibliophilarrStartupException(Exception innerException)
            : base(BuildInfo.AppName + " failed to start: " + innerException.Message)
        {
        }
    }
}
