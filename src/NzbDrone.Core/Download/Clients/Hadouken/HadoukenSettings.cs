using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Hadouken
{
    public class HadoukenSettingsValidator : AbstractValidator<HadoukenSettings>
    {
        public HadoukenSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.UrlBase).ValidUrlBase().When(c => c.UrlBase.IsNotNullOrWhiteSpace());

            RuleFor(c => c.Username).NotEmpty()
                                    .WithMessage("Username must not be empty.");

            RuleFor(c => c.Password).NotEmpty()
                                    .WithMessage("Password must not be empty.");
        }
    }

    public class HadoukenSettings : IProviderConfig, IFormatCategorySettings
    {
        private static readonly HadoukenSettingsValidator Validator = new HadoukenSettingsValidator();

        public HadoukenSettings()
        {
            Host = "localhost";
            Port = 7070;
            EbookCategory = "bibliophilarr-ebooks";
            AudiobookCategory = "bibliophilarr-audiobooks";
        }

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox)]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "Use SSL", Type = FieldType.Checkbox, HelpText = "Use secure connection when connecting to Hadouken")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "Url Base", Type = FieldType.Textbox, Advanced = true, HelpText = "Adds a prefix to the Hadouken url, e.g. http://[host]:[port]/[urlBase]/api")]
        public string UrlBase { get; set; }

        [FieldDefinition(4, Label = "Username", Type = FieldType.Textbox, Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(5, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(6, Label = "Ebook Category", Type = FieldType.Textbox, HelpText = "Label for ebook downloads. Avoids conflicts with unrelated downloads.")]
        public string EbookCategory { get; set; }

        [FieldDefinition(7, Label = "Audiobook Category", Type = FieldType.Textbox, HelpText = "Label for audiobook downloads. Avoids conflicts with unrelated downloads.")]
        public string AudiobookCategory { get; set; }

        // Hadouken doesn't support post-import categories
        public string EbookImportedCategory { get; set; }
        public string AudiobookImportedCategory { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
