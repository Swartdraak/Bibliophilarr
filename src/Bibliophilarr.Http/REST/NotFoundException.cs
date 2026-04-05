using System.Net;
using Bibliophilarr.Http.Exceptions;

namespace Bibliophilarr.Http.REST
{
    public class NotFoundException : ApiException
    {
        public NotFoundException(object content = null)
            : base(HttpStatusCode.NotFound, content)
        {
        }
    }
}
