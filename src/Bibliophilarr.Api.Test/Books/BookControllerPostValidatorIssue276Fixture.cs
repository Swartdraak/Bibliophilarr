using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Http.REST;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Validation;
using NzbDrone.SignalR;

namespace NzbDrone.Api.Test.Books
{
    /// <summary>
    /// Issue #276 regression: POST /api/v1/book returned 500 (instead of 400 with
    /// validation details) for two distinct server-side defects at head 0bd345aa.
    /// Both defects fixed on this branch; these fixtures pin the repaired behavior.
    ///
    /// DEFECT-1 — null-Author NRE (fixed): BookController.PostValidator declared
    /// RuleFor(s => s.Author.QualityProfileId / MetadataProfileId /
    /// RootFolderPath / ForeignAuthorId) with no null guard on s.Author. FluentValidation
    /// evaluates the member expression for every instance, so a payload without a
    /// nested Author object threw NullReferenceException inside validation, which the
    /// pipeline mapped to 500. Each Author-scoped rule is now guarded with
    /// When(s => s.Author != null), so a null Author degrades to the ForeignBookId
    /// not-empty failure (400 with validation details) instead of a 500.
    ///
    /// DEFECT-2 — null-Editions ANE (fixed): EditionResourceMapper.ToModel(IEnumerable)
    /// called resources.Select unguarded (its sibling ToResource used models?.Select).
    /// BookResourceMapper.ToModel invokes resource.Editions.ToModel() on the post-add
    /// mapping path, and Editions can be null for a book whose edition list was never
    /// loaded (e.g. the fresh book returned after AddBook), throwing
    /// ArgumentNullException("source") that surfaced as 500. The mapper now returns
    /// an empty list for a null collection, mirroring its sibling ToResource.
    ///
    /// These fixtures pin both behaviours: the validator must complete for a payload
    /// with null Author (producing validation failures, not an exception), and the
    /// collection ToModel mapper must map a null Editions list to an empty list
    /// instead of throwing.
    /// </summary>
    [TestFixture]
    public class BookControllerPostValidatorIssue276Fixture
    {
        // The PostValidator on RestController&lt;BookResource&gt; is protected; this
        // nested class exposes the constructor-built validator to the fixture.
        private sealed class ValidatorExposer : BookController
        {
            public ValidatorExposer(IQualityProfileService qualityProfileService,
                IMetadataProfileService metadataProfileService)
                : base(
                    Mock.Of<IAuthorService>(),
                    Mock.Of<IBookService>(),
                    Mock.Of<IAddBookService>(),
                    Mock.Of<IEditionService>(),
                    Mock.Of<ISeriesBookLinkService>(),
                    Mock.Of<IAuthorStatisticsService>(),
                    Mock.Of<IMapCoversToLocal>(),
                    Mock.Of<IUpgradableSpecification>(),
                    Mock.Of<IBroadcastSignalRMessage>(),
                    Mock.Of<IAuthorFormatProfileService>(),
                    qualityProfileService,
                    new QualityProfileExistsValidator(qualityProfileService),
                    new MetadataProfileExistsValidator(metadataProfileService))
            {
            }

            public ResourceValidator<BookResource> ExposedPostValidator => PostValidator;
        }

        [Test]
        public void post_validator_with_null_author_should_complete_with_failures_not_throw()
        {
            var controller = new ValidatorExposer(
                Mock.Of<IQualityProfileService>(),
                Mock.Of<IMetadataProfileService>());

            // A simple title payload (no nested Author object): s.Author is null.
            // The validator must not throw NullReferenceException; the Author-scoped
            // rules are skipped via When(s => s.Author != null) and ForeignBookId
            // not-empty is the expected 400-semantic failure.
            var resource = new BookResource { Title = "Test Book" };

            ValidationFailure[] failures = null;
            Assert.DoesNotThrow(() =>
            {
                var result = controller.ExposedPostValidator.Validate(resource);
                failures = result.Errors.ToArray();
            }, "null-Author payload must produce validation failures, never an unhandled exception (issue #276 DEFECT-1)");

            Assert.NotNull(failures);
            Assert.True(failures.Length > 0, "null-Author payload must yield at least one validation failure (400 semantics), not a silent pass");

            var failureProperties = failures.Select(f => f.PropertyName).ToList();

            // The ForeignBookId rule must have been evaluated as a failure (400
            // semantics), and the run must have completed without the pre-fix
            // NullReferenceException from the Author-scoped rules.
            Assert.Contains("ForeignBookId", failureProperties);

            // The Author-scoped rules must be SKIPPED (not silently dropped and not
            // throwing): with a null Author the ForeignBookId not-empty rule is the
            // 400-with-validation-details contract, and no author-scoped failures
            // (e.g. ForeignAuthorId) may be produced or thrown.
            Assert.False(
                failureProperties.Contains("ForeignAuthorId"),
                "author-scoped rules must be skipped for a null Author, not produce failures");
        }

        [Test]
        public void post_validator_with_empty_payload_and_null_author_should_complete_with_failures_not_throw()
        {
            var controller = new ValidatorExposer(
                Mock.Of<IQualityProfileService>(),
                Mock.Of<IMetadataProfileService>());

            // Minimal payload: no ForeignBookId, no nested Author.
            var resource = new BookResource();

            Assert.DoesNotThrow(() => controller.ExposedPostValidator.Validate(resource),
                "completely empty payload with null Author must not throw (issue #276 DEFECT-1)");
        }

        [Test]
        public void post_validator_with_seeded_author_should_validate_foreign_author_id_and_paths_not_throw()
        {
            // Regression guard: the fix must not silently drop the Author-scoped rules
            // when Author IS present — all four rules stay active in that case.
            var qualityProfileService = Mock.Of<IQualityProfileService>(s => s.Exists(1) == true);
            var metadataProfileService = Mock.Of<IMetadataProfileService>(s => s.Exists(1) == true);
            var controller = new ValidatorExposer(qualityProfileService, metadataProfileService);

            // Contract-conforming shape with Author present (the payload shape from
            // issue #276's DEFECT-2 probe): the seeded profile ids pass the profile
            // exists validators, but ForeignAuthorId is empty and RootFolderPath is
            // not a valid path, so both Author-scoped rules must fire as failures.
            var resource = new BookResource
            {
                Title = "Seeded",
                ForeignBookId = "OLW123",

                Author = new Bibliophilarr.Api.V1.Author.AuthorResource
                {
                    QualityProfileId = 1,
                    MetadataProfileId = 1,
                    RootFolderPath = "not-a-valid-path",
                    Path = null,
                    ForeignAuthorId = string.Empty,
                },
            };

            var failures = controller.ExposedPostValidator.Validate(resource).Errors;

            // FluentValidation reports nested property failures with the full path
            // (e.g. "Author.ForeignAuthorId"), so match on that.
            Assert.True(failures.Any(f => f.PropertyName == "Author.ForeignAuthorId"),
                "Author-scoped ForeignAuthorId rule must fire when Author is present but the id is missing");
            Assert.True(failures.Any(f => f.PropertyName == "Author.RootFolderPath"),
                "Author-scoped RootFolderPath rule must fire for an invalid root path");
            Assert.False(failures.Any(f => f.PropertyName == "QualityProfileId"),
                "seeded quality profile id 1 exists — rule must pass");
            Assert.False(failures.Any(f => f.PropertyName == "MetadataProfileId"),
                "seeded metadata profile id 1 exists — rule must pass");
        }
    }

    [TestFixture]
    public class EditionResourceMapperIssue276Fixture
    {
        [Test]
        public void to_model_null_editions_should_return_empty_list_not_throw()
        {
            List<EditionResource> editions = null;

            List<Edition> models = null;
            Assert.DoesNotThrow(() => models = editions.ToModel(),
                "null Editions collection must map to an empty list, never ArgumentNullException (issue #276 DEFECT-2)");

            Assert.NotNull(models);
            Assert.IsEmpty(models);
        }

        [Test]
        public void to_model_empty_editions_should_return_empty_list()
        {
            var editions = new List<EditionResource>();

            var models = editions.ToModel();

            Assert.NotNull(models);
            Assert.IsEmpty(models);
        }

        [Test]
        public void to_resource_null_models_should_return_null()
        {
            List<Edition> models = null;

            var resources = models.ToResource();

            Assert.Null(resources);
        }

        [Test]
        public void book_resource_to_model_with_null_author_and_null_editions_should_not_throw()
        {
            var resource = new BookResource
            {
                Title = "Post-add mapping regression",
                ForeignBookId = "OLW123",
            };

            // Author and Editions left null — exactly the post-AddBook mapping shape
            // that threw ArgumentNullException in head 0bd345aa (issue #276 DEFECT-2).
            Book book = null;
            Assert.DoesNotThrow(() => book = resource.ToModel(),
                "BookResource.ToModel with null Author and null Editions must not throw (issue #276 DEFECT-2)");

            Assert.NotNull(book);
            Assert.NotNull(book.Editions);
            Assert.IsEmpty(book.Editions.Value);

            // resource.Author is null — BookResourceMapper substitutes a fresh
            // Author model, so the mapping completes without throwing.
            Assert.NotNull(book.Author);
        }
    }
}
