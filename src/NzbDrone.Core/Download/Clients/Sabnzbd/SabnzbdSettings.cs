using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Sabnzbd
{
    public class SabnzbdSettingsValidator : AbstractValidator<SabnzbdSettings>
    {
        public SabnzbdSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.UrlBase).ValidUrlBase().When(c => c.UrlBase.IsNotNullOrWhiteSpace());

            RuleFor(c => c.ApiKey).NotEmpty()
                                  .WithMessage("API Key is required when username/password are not configured")
                                  .When(c => string.IsNullOrWhiteSpace(c.Username));

            RuleFor(c => c.Username).NotEmpty()
                                    .WithMessage("Username is required when API key is not configured")
                                    .When(c => string.IsNullOrWhiteSpace(c.ApiKey));

            RuleFor(c => c.Password).NotEmpty()
                                    .WithMessage("Password is required when API key is not configured")
                                    .When(c => string.IsNullOrWhiteSpace(c.ApiKey));

            RuleFor(c => c.EbookCategory).NotEmpty()
                                      .WithMessage("A category is recommended")
                                      .AsWarning();

            RuleFor(c => c.EbookCategory).Must(cat => !cat.Contains('\\') && !cat.Contains("//"))
                                      .WithMessage(@"Can not contain '\' or '//'")
                                      .When(c => c.EbookCategory.IsNotNullOrWhiteSpace());
            RuleFor(c => c.AudiobookCategory).Must(cat => !cat.Contains('\\') && !cat.Contains("//"))
                                      .WithMessage(@"Can not contain '\' or '//'")
                                      .When(c => c.AudiobookCategory.IsNotNullOrWhiteSpace());
        }
    }

    public class SabnzbdSettings : IProviderConfig, IFormatCategorySettings
    {
        private static readonly SabnzbdSettingsValidator Validator = new SabnzbdSettingsValidator();

        public SabnzbdSettings()
        {
            Host = "localhost";
            Port = 8080;
            EbookCategory = "bibliophilarr-ebooks";
            AudiobookCategory = "bibliophilarr-audiobooks";
            RecentTvPriority = (int)SabnzbdPriority.Default;
            OlderTvPriority = (int)SabnzbdPriority.Default;
        }

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox)]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "Use SSL", Type = FieldType.Checkbox, HelpText = "Use secure connection when connecting to Sabnzbd")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "Url Base", Type = FieldType.Textbox, Advanced = true, HelpText = "Adds a prefix to the Sabnzbd url, e.g. http://[host]:[port]/[urlBase]/api")]
        public string UrlBase { get; set; }

        [FieldDefinition(4, Label = "API Key", Type = FieldType.Textbox, Privacy = PrivacyLevel.ApiKey)]
        public string ApiKey { get; set; }

        [FieldDefinition(5, Label = "Username", Type = FieldType.Textbox, Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(6, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(7, Label = "Ebook Category", Type = FieldType.Textbox, HelpText = "Category for ebook downloads. Avoids conflicts with unrelated downloads. Strongly recommended.")]
        public string EbookCategory { get; set; }

        [FieldDefinition(8, Label = "Audiobook Category", Type = FieldType.Textbox, HelpText = "Category for audiobook downloads. Avoids conflicts with unrelated downloads. Strongly recommended.")]
        public string AudiobookCategory { get; set; }

        // Usenet clients don't use post-import categories (not applicable)
        public string EbookImportedCategory { get; set; }
        public string AudiobookImportedCategory { get; set; }

        [FieldDefinition(10, Label = "Recent Priority", Type = FieldType.Select, SelectOptions = typeof(SabnzbdPriority), HelpText = "Priority to use when grabbing books released within the last 14 days")]
        public int RecentTvPriority { get; set; }

        [FieldDefinition(11, Label = "Older Priority", Type = FieldType.Select, SelectOptions = typeof(SabnzbdPriority), HelpText = "Priority to use when grabbing books released over 14 days ago")]
        public int OlderTvPriority { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
