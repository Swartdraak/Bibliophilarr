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
            Http.Validation.RuleBuilderExtensions.IsInEnum(
                SharedValidator.RuleFor(s => s.MonitorNewItems),
                typeof(NzbDrone.Core.Books.NewItemMonitorTypes));
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
        public ActionResult<AuthorFormatProfileResource> Update([FromRoute] int id, [FromBody] AuthorFormatProfileResource resource)
        {
            if (resource.Id != id)
            {
                throw new BadRequestException("The format profile does not match the requested ID.");
            }

            var stored = _formatProfileService.Get(id);
            if (resource.AuthorId != stored.AuthorId || resource.FormatType != stored.FormatType)
            {
                throw new BadRequestException("The format profile's author and format cannot be changed.");
            }

            var model = resource.ToModel();
            model.Path = stored.Path;
            model.Tags = resource.Tags ?? stored.Tags;
            _formatProfileService.Update(model);
            return Accepted(id);
        }

        [RestDeleteById]
        public void Delete(int id)
        {
            _formatProfileService.Delete(id);
        }
    }
}
