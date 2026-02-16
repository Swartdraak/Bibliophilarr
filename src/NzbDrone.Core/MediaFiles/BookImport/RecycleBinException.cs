using System;
using System.IO;
using System.Runtime.Serialization;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class RecycleBinException : DirectoryNotFoundException
    {
        public RecycleBinException()
        {
        }

        public RecycleBinException(string message)
            : base(message)
        {
        }

        public RecycleBinException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

#pragma warning disable SYSLIB0051 // Type or member is obsolete
        protected RecycleBinException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#pragma warning restore SYSLIB0051 // Type or member is obsolete
    }
}
