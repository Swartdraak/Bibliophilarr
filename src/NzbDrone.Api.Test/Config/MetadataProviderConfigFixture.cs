using Bibliophilarr.Api.V1.Config;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Config
{
    [TestFixture]
    public class MetadataProviderConfigFixture
    {
        private static IConfigService BuildConfigService(
            bool enableHardcover = true,
            string hardcoverToken = "raw-jwt-token",
            int hardcoverTimeout = 30,
            bool enableGoogleBooks = false,
            string googleBooksKey = "",
            bool enableInventaire = true,
            bool enableConflictStrategyVariants = false,
            int identificationWorkerCount = 4,
            int importTagReadWorkerCount = 2,
            int remoteCandidateSearchWorkerCount = 3)
        {
            var mock = new Mock<IConfigService>();
            mock.SetupGet(x => x.EnableHardcoverFallback).Returns(enableHardcover);
            mock.SetupGet(x => x.HardcoverApiToken).Returns(hardcoverToken);
            mock.SetupGet(x => x.HardcoverRequestTimeoutSeconds).Returns(hardcoverTimeout);
            mock.SetupGet(x => x.EnableInventaireFallback).Returns(enableInventaire);
            mock.SetupGet(x => x.EnableGoogleBooksFallback).Returns(enableGoogleBooks);
            mock.SetupGet(x => x.GoogleBooksApiKey).Returns(googleBooksKey);
            mock.SetupGet(x => x.EnableMetadataConflictStrategyVariants).Returns(enableConflictStrategyVariants);
            mock.SetupGet(x => x.MetadataAuthorAliases).Returns(string.Empty);
            mock.SetupGet(x => x.MetadataTitleStripPatterns).Returns(string.Empty);
            mock.SetupGet(x => x.IdentificationWorkerCount).Returns(identificationWorkerCount);
            mock.SetupGet(x => x.ImportTagReadWorkerCount).Returns(importTagReadWorkerCount);
            mock.SetupGet(x => x.RemoteCandidateSearchWorkerCount).Returns(remoteCandidateSearchWorkerCount);
            return mock.Object;
        }

        private static InlineValidator<MetadataProviderConfigResource> BuildTimeoutValidator()
        {
            var v = new InlineValidator<MetadataProviderConfigResource>();
            v.RuleFor(c => c.HardcoverRequestTimeoutSeconds)
                .InclusiveBetween(0, 120)
                .WithMessage("Hardcover request timeout must be between 0 and 120 seconds");
            v.RuleFor(c => c.IdentificationWorkerCount)
                .InclusiveBetween(1, 8)
                .WithMessage("Identification worker count must be between 1 and 8");
            v.RuleFor(c => c.ImportTagReadWorkerCount)
                .InclusiveBetween(1, 8)
                .WithMessage("Import tag read worker count must be between 1 and 8");
            v.RuleFor(c => c.RemoteCandidateSearchWorkerCount)
                .InclusiveBetween(1, 8)
                .WithMessage("Remote candidate search worker count must be between 1 and 8");
            return v;
        }

        [Test]
        public void mapper_should_include_inventaire_enable_flag()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(enableInventaire: true));
            resource.EnableInventaireFallback.Should().BeTrue();
        }

        [Test]
        public void mapper_should_map_inventaire_disabled_state()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(enableInventaire: false));
            resource.EnableInventaireFallback.Should().BeFalse();
        }

        [Test]
        public void mapper_should_include_hardcover_enable_flag()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(enableHardcover: true));
            resource.EnableHardcoverFallback.Should().BeTrue();
        }

        [Test]
        public void mapper_should_round_trip_hardcover_api_token_with_bearer_prefix()
        {
            const string stored = "Bearer eyJhbGciOiJIUzI1NiJ9.test.sig";
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(hardcoverToken: stored));
            resource.HardcoverApiToken.Should().Be(stored);
        }

        [Test]
        public void mapper_should_round_trip_hardcover_api_token_without_bearer_prefix()
        {
            const string stored = "plain-jwt-token";
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(hardcoverToken: stored));
            resource.HardcoverApiToken.Should().Be(stored);
        }

        [Test]
        public void mapper_should_round_trip_hardcover_request_timeout_seconds()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(hardcoverTimeout: 45));
            resource.HardcoverRequestTimeoutSeconds.Should().Be(45);
        }

        [Test]
        public void mapper_should_map_zero_timeout_as_disabled()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(hardcoverTimeout: 0));
            resource.HardcoverRequestTimeoutSeconds.Should().Be(0);
        }

        [Test]
        public void mapper_should_map_hardcover_disabled_state()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(enableHardcover: false));
            resource.EnableHardcoverFallback.Should().BeFalse();
        }

        [Test]
        public void mapper_should_include_conflict_strategy_variant_flag()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(enableConflictStrategyVariants: true));
            resource.EnableMetadataConflictStrategyVariants.Should().BeTrue();
        }

        [Test]
        public void mapper_should_map_conflict_strategy_variant_disabled_state()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(enableConflictStrategyVariants: false));
            resource.EnableMetadataConflictStrategyVariants.Should().BeFalse();
        }

        [Test]
        public void mapper_should_round_trip_identification_worker_count()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(identificationWorkerCount: 6));
            resource.IdentificationWorkerCount.Should().Be(6);
        }

        [Test]
        public void mapper_should_round_trip_import_tag_read_worker_count()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(importTagReadWorkerCount: 3));
            resource.ImportTagReadWorkerCount.Should().Be(3);
        }

        [Test]
        public void mapper_should_round_trip_remote_candidate_search_worker_count()
        {
            var resource = MetadataProviderConfigResourceMapper.ToResource(BuildConfigService(remoteCandidateSearchWorkerCount: 5));
            resource.RemoteCandidateSearchWorkerCount.Should().Be(5);
        }

        [Test]
        public void validation_should_accept_timeout_of_zero()
        {
            var result = BuildTimeoutValidator().Validate(new MetadataProviderConfigResource
            {
                HardcoverRequestTimeoutSeconds = 0,
                IdentificationWorkerCount = 4,
                ImportTagReadWorkerCount = 2,
                RemoteCandidateSearchWorkerCount = 3
            });
            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void validation_should_accept_timeout_at_max_boundary()
        {
            var result = BuildTimeoutValidator().Validate(new MetadataProviderConfigResource
            {
                HardcoverRequestTimeoutSeconds = 120,
                IdentificationWorkerCount = 4,
                ImportTagReadWorkerCount = 2,
                RemoteCandidateSearchWorkerCount = 3
            });
            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void validation_should_reject_timeout_above_maximum()
        {
            var result = BuildTimeoutValidator().Validate(new MetadataProviderConfigResource
            {
                HardcoverRequestTimeoutSeconds = 121,
                IdentificationWorkerCount = 4,
                ImportTagReadWorkerCount = 2,
                RemoteCandidateSearchWorkerCount = 3
            });
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "HardcoverRequestTimeoutSeconds");
        }

        [Test]
        public void validation_should_reject_negative_timeout()
        {
            var result = BuildTimeoutValidator().Validate(new MetadataProviderConfigResource
            {
                HardcoverRequestTimeoutSeconds = -1,
                IdentificationWorkerCount = 4,
                ImportTagReadWorkerCount = 2,
                RemoteCandidateSearchWorkerCount = 3
            });
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "HardcoverRequestTimeoutSeconds");
        }

        [Test]
        public void validation_should_accept_worker_settings_within_range()
        {
            var result = BuildTimeoutValidator().Validate(new MetadataProviderConfigResource
            {
                HardcoverRequestTimeoutSeconds = 30,
                IdentificationWorkerCount = 4,
                ImportTagReadWorkerCount = 2,
                RemoteCandidateSearchWorkerCount = 3
            });

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void validation_should_reject_identification_worker_count_above_maximum()
        {
            var result = BuildTimeoutValidator().Validate(new MetadataProviderConfigResource
            {
                HardcoverRequestTimeoutSeconds = 30,
                IdentificationWorkerCount = 9,
                ImportTagReadWorkerCount = 2,
                RemoteCandidateSearchWorkerCount = 3
            });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "IdentificationWorkerCount");
        }

        [Test]
        public void validation_should_reject_import_tag_read_worker_count_below_minimum()
        {
            var result = BuildTimeoutValidator().Validate(new MetadataProviderConfigResource
            {
                HardcoverRequestTimeoutSeconds = 30,
                IdentificationWorkerCount = 4,
                ImportTagReadWorkerCount = 0,
                RemoteCandidateSearchWorkerCount = 3
            });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "ImportTagReadWorkerCount");
        }

        [Test]
        public void validation_should_reject_remote_candidate_search_worker_count_below_minimum()
        {
            var result = BuildTimeoutValidator().Validate(new MetadataProviderConfigResource
            {
                HardcoverRequestTimeoutSeconds = 30,
                IdentificationWorkerCount = 4,
                ImportTagReadWorkerCount = 2,
                RemoteCandidateSearchWorkerCount = 0
            });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "RemoteCandidateSearchWorkerCount");
        }
    }
}
