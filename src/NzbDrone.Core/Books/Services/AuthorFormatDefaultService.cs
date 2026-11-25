using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IAuthorFormatDefaultService
    {
        void ResolvePending(int? authorId = null);
    }

    public class AuthorFormatDefaultService : IAuthorFormatDefaultService
    {
        private readonly IConfigService _configService;
        private readonly IAuthorFormatProfileRepository _formatProfiles;
        private readonly IRootFolderRepository _rootFolders;
        private readonly IProfileRepository _qualityProfiles;

        public AuthorFormatDefaultService(IConfigService configService,
                                          IAuthorFormatProfileRepository formatProfiles,
                                          IRootFolderRepository rootFolders,
                                          IProfileRepository qualityProfiles)
        {
            _configService = configService;
            _formatProfiles = formatProfiles;
            _rootFolders = rootFolders;
            _qualityProfiles = qualityProfiles;
        }

        public void ResolvePending(int? authorId = null)
        {
            if (!_configService.EnableDualFormatTracking)
            {
                return;
            }

            var pending = _formatProfiles.GetPendingDefaults(authorId);
            if (pending.Count == 0)
            {
                return;
            }

            var qualityProfiles = _qualityProfiles.All().ToDictionary(profile => profile.Id);
            var rootsByFormat = _rootFolders.All()
                .Where(root => !root.IsCalibreLibrary && qualityProfiles.ContainsKey(root.DefaultQualityProfileId))
                .Select(root => (Root: root, Format: GetExclusiveFormat(qualityProfiles[root.DefaultQualityProfileId])))
                .Where(candidate => candidate.Format.HasValue)
                .ToLookup(candidate => candidate.Format.Value, candidate => candidate.Root);

            foreach (var profile in pending)
            {
                var candidates = rootsByFormat[profile.FormatType].Take(2).ToList();
                if (candidates.Count == 1)
                {
                    _formatProfiles.TryResolvePendingDefault(profile, candidates[0]);
                }
            }
        }

        internal static FormatType? GetExclusiveFormat(QualityProfile profile)
        {
            var allowed = profile?.Items?
                .Where(item => item.Allowed)
                .SelectMany(item => item.GetQualities())
                .Where(quality => quality != null)
                .ToList();

            if (allowed?.Any(quality => quality != Quality.Unknown && quality != Quality.UnknownAudio) != true)
            {
                return null;
            }

            var formats = allowed.Select(Quality.GetFormatType).Distinct().Take(2).ToList();
            return formats.Count == 1 ? formats[0] : null;
        }
    }
}
