using System;
using System.IO;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.MediaCover
{
    public interface ICoverExistsSpecification
    {
        bool AlreadyExists(DateTime? serverModifiedDate, long? serverContentLength, string localPath);
    }

    public class CoverAlreadyExistsSpecification : ICoverExistsSpecification
    {
        private readonly IDiskProvider _diskProvider;

        public CoverAlreadyExistsSpecification(IDiskProvider diskProvider)
        {
            _diskProvider = diskProvider;
        }

        public bool AlreadyExists(DateTime? serverModifiedDate, long? serverContentLength, string localPath)
        {
            try
            {
                if (!_diskProvider.FileExists(localPath))
                {
                    return false;
                }

                var fileSize = _diskProvider.GetFileSize(localPath);
                if (fileSize <= 0)
                {
                    return false;
                }

                if (serverContentLength.HasValue && serverContentLength.Value > 0)
                {
                    return fileSize == serverContentLength;
                }

                return serverModifiedDate.HasValue &&
                       _diskProvider.FileGetLastWrite(localPath).ToUniversalTime() == serverModifiedDate.Value.ToUniversalTime();
            }
            catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return false;
            }
        }
    }
}
