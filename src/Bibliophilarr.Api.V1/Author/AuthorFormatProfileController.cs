using System.Collections.Generic;
using Bibliophilarr.Http;
using Bibliophilarr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Http.REST.Attributes;

namespace Bibliophilarr.Api.V1.Author
{
    [V1ApiController]
    public class AuthorFormatProfileController : RestController<AuthorFormatProfileResource>
    {
        private readonly IAuthorFormatProfileService _formatProfileService;

        public AuthorFormatProfileController(IAuthorFormatProfileService formatProfileService)
        {
            _formatProfileService = formatProfileService;

            Http.Validation.RuleBuilderExtensions.ValidId(SharedValidator.RuleFor(s => s.AuthorId));
            Http.Validation.RuleBuilderExtensions.ValidId(SharedValidator.RuleFor(s => s.QualityProfileId));
        }

        protected override AuthorFormatProfileResource GetResourceById(int id)
        {
            return _formatProfileService.Get(id).ToResource();
        }

        [HttpGet]
        public List<AuthorFormatProfileResource> GetAll([FromQuery] int? authorId = null)
        {
            if (authorId.HasValue)
            {
                return _formatProfileService.GetByAuthorId(authorId.Value).ToResource();
            }

            return new List<AuthorFormatProfileResource>();
        }

        [RestPostById]
        public ActionResult<AuthorFormatProfileResource> Create([FromBody] AuthorFormatProfileResource resource)
        {
            var model = _formatProfileService.Add(resource.ToModel());
            return Created(model.Id);
        }

        [RestPutById]
        public ActionResult<AuthorFormatProfileResource> Update([FromBody] AuthorFormatProfileResource resource)
        {
            _formatProfileService.Update(resource.ToModel());
            return Accepted(resource.Id);
        }

        [RestDeleteById]
        public void Delete(int id)
        {
            _formatProfileService.Delete(id);
        }
    }
}
