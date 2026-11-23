using System.Net;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.GoogleBooks
{
    internal sealed class GoogleBooksServerErrorException : HttpException
    {
        public GoogleBooksServerErrorException(HttpStatusCode statusCode)
            : base(null, null, $"Google Books returned HTTP {(int)statusCode}.")
        {
        }
    }
}
