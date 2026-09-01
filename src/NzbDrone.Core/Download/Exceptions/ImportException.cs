using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Download.Exceptions
{
    /// <summary>
    /// Raised during import processing to signal a terminal failure for a tracked download.
    /// The message is surfaced verbatim as the download failure message.
    /// </summary>
    public class ImportException : NzbDroneException
    {
        public ImportException(string message, params object[] args)
            : base(message, args)
        {
        }

        public ImportException(string message, System.Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
