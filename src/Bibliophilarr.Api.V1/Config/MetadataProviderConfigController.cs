using Bibliophilarr.Http;
using FluentValidation;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Api.V1.Config
{
    [V1ApiController("config/metadataprovider")]
    public class MetadataProviderConfigController : ConfigController<MetadataProviderConfigResource>
    {
        public MetadataProviderConfigController(IConfigService configService)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.MetadataAuthorAliases)
                .Must(BeValidJsonObject)
                .WithMessage("Must be a valid JSON object mapping canonical author names to alias arrays")
                .When(c => !string.IsNullOrWhiteSpace(c.MetadataAuthorAliases));

            SharedValidator.RuleFor(c => c.MetadataTitleStripPatterns)
                .Must(BeValidJsonArray)
                .WithMessage("Must be a valid JSON array of regular expression patterns")
                .When(c => !string.IsNullOrWhiteSpace(c.MetadataTitleStripPatterns));

            SharedValidator.RuleFor(c => c.HardcoverRequestTimeoutSeconds)
                .InclusiveBetween(0, 120)
                .WithMessage("Hardcover request timeout must be between 0 and 120 seconds");

            SharedValidator.RuleFor(c => c.IsbnContextFallbackLimit)
                .InclusiveBetween(1, 10)
                .WithMessage("ISBN contextual fallback attempt limit must be between 1 and 10");

            SharedValidator.RuleFor(c => c.OpenLibrarySearchTimeoutSeconds)
                .InclusiveBetween(0, 120)
                .WithMessage("OpenLibrary search timeout must be between 0 and 120 seconds");

            SharedValidator.RuleFor(c => c.OpenLibraryIsbnTimeoutSeconds)
                .InclusiveBetween(0, 120)
                .WithMessage("OpenLibrary ISBN timeout must be between 0 and 120 seconds");

            SharedValidator.RuleFor(c => c.OpenLibraryWorkTimeoutSeconds)
                .InclusiveBetween(0, 120)
                .WithMessage("OpenLibrary work timeout must be between 0 and 120 seconds");

            SharedValidator.RuleFor(c => c.OpenLibrarySearchRetryBudget)
                .InclusiveBetween(-1, 10)
                .WithMessage("OpenLibrary search retry budget must be between -1 and 10");

            SharedValidator.RuleFor(c => c.OpenLibraryIsbnRetryBudget)
                .InclusiveBetween(-1, 10)
                .WithMessage("OpenLibrary ISBN retry budget must be between -1 and 10");

            SharedValidator.RuleFor(c => c.OpenLibraryWorkRetryBudget)
                .InclusiveBetween(-1, 10)
                .WithMessage("OpenLibrary work retry budget must be between -1 and 10");

            SharedValidator.RuleFor(c => c.BookImportMatchThresholdPercent)
                .InclusiveBetween(50, 100)
                .WithMessage("Book import match threshold must be between 50 and 100 percent");

            SharedValidator.RuleFor(c => c.IdentificationWorkerCount)
                .InclusiveBetween(1, 8)
                .WithMessage("Identification worker count must be between 1 and 8");

            SharedValidator.RuleFor(c => c.ImportTagReadWorkerCount)
                .InclusiveBetween(1, 8)
                .WithMessage("Import tag read worker count must be between 1 and 8");

            SharedValidator.RuleFor(c => c.RemoteCandidateSearchWorkerCount)
                .InclusiveBetween(1, 8)
                .WithMessage("Remote candidate search worker count must be between 1 and 8");
        }

        protected override MetadataProviderConfigResource ToResource(IConfigService model)
        {
            return MetadataProviderConfigResourceMapper.ToResource(model);
        }

        private static bool BeValidJsonObject(string value)
        {
            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(value);
                if (token.Type != Newtonsoft.Json.Linq.JTokenType.Object)
                {
                    return false;
                }

                foreach (var property in token.Children<Newtonsoft.Json.Linq.JProperty>())
                {
                    if (property.Value.Type != Newtonsoft.Json.Linq.JTokenType.Array)
                    {
                        return false;
                    }

                    foreach (var child in property.Value.Children())
                    {
                        if (child.Type != Newtonsoft.Json.Linq.JTokenType.String)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool BeValidJsonArray(string value)
        {
            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(value);
                if (token.Type != Newtonsoft.Json.Linq.JTokenType.Array)
                {
                    return false;
                }

                foreach (var child in token.Children())
                {
                    if (child.Type != Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        return false;
                    }

                    _ = new global::System.Text.RegularExpressions.Regex(child.ToObject<string>());
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
