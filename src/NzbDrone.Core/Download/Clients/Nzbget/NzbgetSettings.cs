using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Nzbget
{
    public class NzbgetSettingsValidator : AbstractValidator<NzbgetSettings>
    {
        public NzbgetSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.UrlBase).ValidUrlBase().When(c => c.UrlBase.IsNotNullOrWhiteSpace());

            RuleFor(c => c.Username).NotEmpty().When(c => !string.IsNullOrWhiteSpace(c.Password));
            RuleFor(c => c.Password).NotEmpty().When(c => !string.IsNullOrWhiteSpace(c.Username));

            RuleFor(c => c.EbookCategory).NotEmpty().WithMessage("A category is recommended").AsWarning();
        }
    }

    public class NzbgetSettings : IProviderConfig, IFormatCategorySettings
    {
        private static readonly NzbgetSettingsValidator Validator = new NzbgetSettingsValidator();

        public NzbgetSettings()
        {
            Host = "localhost";
            Port = 6789;
            EbookCategory = "bibliophilarr-ebooks";
            AudiobookCategory = "bibliophilarr-audiobooks";
            Username = "nzbget";
            Password = "tegbzn6789";
            RecentTvPriority = (int)NzbgetPriority.Normal;
            OlderTvPriority = (int)NzbgetPriority.Normal;
        }

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox)]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "Use SSL", Type = FieldType.Checkbox, HelpText = "Use secure connection when connecting to NZBGet")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "Url Base", Type = FieldType.Textbox, Advanced = true, HelpText = "Adds a prefix to the nzbget url, e.g. http://[host]:[port]/[urlBase]/jsonrpc")]
        public string UrlBase { get; set; }

        [FieldDefinition(3, Label = "Username", Type = FieldType.Textbox, Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(5, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(6, Label = "Ebook Category", Type = FieldType.Textbox, HelpText = "Category for ebook downloads. Avoids conflicts with unrelated downloads. Strongly recommended.")]
        public string EbookCategory { get; set; }

        [FieldDefinition(7, Label = "Audiobook Category", Type = FieldType.Textbox, HelpText = "Category for audiobook downloads. Avoids conflicts with unrelated downloads. Strongly recommended.")]
        public string AudiobookCategory { get; set; }

        // Usenet clients don't use post-import categories (not applicable)
        public string EbookImportedCategory { get; set; }
        public string AudiobookImportedCategory { get; set; }

        [FieldDefinition(9, Label = "Recent Priority", Type = FieldType.Select, SelectOptions = typeof(NzbgetPriority), HelpText = "Priority to use when grabbing books released within the last 14 days")]
        public int RecentTvPriority { get; set; }

        [FieldDefinition(10, Label = "Older Priority", Type = FieldType.Select, SelectOptions = typeof(NzbgetPriority), HelpText = "Priority to use when grabbing books released over 14 days ago")]
        public int OlderTvPriority { get; set; }

        [FieldDefinition(11, Label = "Add Paused", Type = FieldType.Checkbox, HelpText = "This option requires at least NzbGet version 16.0")]
        public bool AddPaused { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
