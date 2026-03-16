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
