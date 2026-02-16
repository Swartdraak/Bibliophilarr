using System;

namespace NzbDrone.Core.MediaFiles.Azw
{
    [Serializable]
    public class AzwTagException : Exception
    {
        public AzwTagException(string message)
            : base(message)
        {
        }

#pragma warning disable SYSLIB0051 // Type or member is obsolete
        protected AzwTagException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }
#pragma warning restore SYSLIB0051 // Type or member is obsolete
    }
}
