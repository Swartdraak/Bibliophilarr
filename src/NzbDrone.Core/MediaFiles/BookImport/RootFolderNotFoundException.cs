using System;
using System.IO;
using System.Runtime.Serialization;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class RootFolderNotFoundException : DirectoryNotFoundException
    {
        public RootFolderNotFoundException()
        {
        }

        public RootFolderNotFoundException(string message)
            : base(message)
        {
        }

        public RootFolderNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

#pragma warning disable SYSLIB0051 // Type or member is obsolete
        protected RootFolderNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#pragma warning restore SYSLIB0051 // Type or member is obsolete
    }
}
