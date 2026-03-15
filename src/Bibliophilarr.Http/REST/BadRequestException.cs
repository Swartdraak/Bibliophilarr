using System.Net;
using Bibliophilarr.Http.Exceptions;

namespace Bibliophilarr.Http.REST
{
    public class BadRequestException : ApiException
    {
        public BadRequestException(object content = null)
            : base(HttpStatusCode.BadRequest, content)
        {
        }
    }
}
