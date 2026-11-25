using System;
using System.IO;
using Equ;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaCover
{
    public enum MediaCoverTypes
    {
        Unknown = 0,
        Poster = 1,
        Banner = 2,
        Fanart = 3,
        Screenshot = 4,
        Headshot = 5,
        Cover = 6,
        Disc = 7,
        Logo = 8,
        Clearlogo = 9
    }

    public enum MediaCoverEntity
    {
        Author = 0,
        Book = 1
    }

    public class MediaCover : MemberwiseEquatable<MediaCover>, IEmbeddedDocument
    {
        private string _url;
        public string Url
        {
            get => _url;
            set
            {
                _url = value;

                if (Extension.IsNullOrWhiteSpace())
                {
                    Extension = GetImageExtension(value);
                }
            }
        }

        public MediaCoverTypes CoverType { get; set; }
        public string Extension { get; private set; }
        public string RemoteUrl { get; set; }
        public string SourceProvider { get; set; }
        public string SourceId { get; set; }

        public MediaCover()
        {
        }

        public MediaCover(MediaCoverTypes coverType, string url)
        {
            CoverType = coverType;
            Url = url;
        }

        internal static string GetImageExtension(string url)
        {
            if (url.IsNullOrWhiteSpace())
            {
                return null;
            }

            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.AbsolutePath
                : url;
            var extension = Path.GetExtension(path);

            return extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" ? extension : ".jpg";
        }
    }
}
