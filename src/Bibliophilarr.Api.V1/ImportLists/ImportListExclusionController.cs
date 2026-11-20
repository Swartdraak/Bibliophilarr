using System.Collections.Generic;
using Bibliophilarr.Http;
using Bibliophilarr.Http.REST;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Validation;
using NzbDrone.Http.REST.Attributes;

namespace Bibliophilarr.Api.V1.ImportLists
{
    [V1ApiController]
    public class ImportListExclusionController : RestController<ImportListExclusionResource>
    {
        private const string DuplicateMessage = "This exclusion has already been added.";
        private readonly IImportListExclusionService _importListExclusionService;

        public ImportListExclusionController(IImportListExclusionService importListExclusionService,
                                             GuidValidator guidValidator)
        {
            _importListExclusionService = importListExclusionService;

            SharedValidator.RuleFor(c => c.ForeignId).NotEmpty();
            SharedValidator.RuleFor(c => c.AuthorName).NotEmpty();

            PostValidator.RuleFor(c => c.ForeignId).SetValidator(guidValidator)
                .Must(foreignId => IsForeignIdAvailable(foreignId)).WithMessage(DuplicateMessage);

            // Automatically created exclusions can already have a provider-specific ID.
            PutValidator.RuleFor(c => c.ForeignId).SetValidator(guidValidator)
                .When(resource => FindExclusion(resource.ForeignId)?.Id != resource.Id);
            PutValidator.RuleFor(c => c.ForeignId)
                .Must((resource, foreignId) => IsForeignIdAvailable(foreignId, resource.Id))
                .WithMessage(DuplicateMessage);
        }

        private bool IsForeignIdAvailable(string foreignId, int? currentId = null)
        {
            var existing = FindExclusion(foreignId);
            return existing == null || existing.Id == currentId;
        }

        private ImportListExclusion FindExclusion(string foreignId)
        {
            return string.IsNullOrWhiteSpace(foreignId) ? null : _importListExclusionService.FindByForeignId(foreignId);
        }

        protected override ImportListExclusionResource GetResourceById(int id)
        {
            return _importListExclusionService.Get(id).ToResource();
        }

        [HttpGet]
        public List<ImportListExclusionResource> GetImportListExclusions()
        {
            return _importListExclusionService.All().ToResource();
        }

        [RestPostById]
        public ActionResult<ImportListExclusionResource> AddImportListExclusion([FromBody] ImportListExclusionResource resource)
        {
            var customFilter = _importListExclusionService.Add(resource.ToModel());

            return Created(customFilter.Id);
        }

        [RestPutById]
        public ActionResult<ImportListExclusionResource> UpdateImportListExclusion([FromBody] ImportListExclusionResource resource)
        {
            _importListExclusionService.Update(resource.ToModel());
            return Accepted(resource.Id);
        }

        [RestDeleteById]
        public void DeleteImportListExclusionResource(int id)
        {
            _importListExclusionService.Delete(id);
        }
    }
}
