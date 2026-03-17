using System.Net;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public class OpenLibraryException : NzbDroneClientException
    {
        public OpenLibraryException(string message)
            : base(HttpStatusCode.ServiceUnavailable, message)
        {
        }

        public OpenLibraryException(string message, params object[] args)
            : base(HttpStatusCode.ServiceUnavailable, message, args)
        {
        }

        public OpenLibraryException(string message, System.Exception innerException, params object[] args)
            : base(HttpStatusCode.ServiceUnavailable, message, innerException, args)
        {
        }
    }
}
