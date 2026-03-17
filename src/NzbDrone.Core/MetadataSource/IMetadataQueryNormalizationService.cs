using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataQueryNormalizationService
    {
        List<string> ExpandAuthorAliases(IEnumerable<string> authorNames);
        List<string> BuildTitleVariants(string title);
    }
}
