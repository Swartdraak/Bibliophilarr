using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataQueryNormalizationService : IMetadataQueryNormalizationService
    {
        private const string DefaultAliasConfig = "{\"terry mancour\":[\"t. l. mancour\",\"t l mancour\"],\"t. l. mancour\":[\"terry mancour\"],\"tl mancour\":[\"terry mancour\"]}";
        private const string DefaultTitleStripPatternConfig = "[\"\\\\s*[:\\\\-]\\\\s*(a\\\\s+litrpg\\\\s+adventure|an?\\\\s+audible\\\\s+original)\\\\s*$\",\"\\\\s*\\\\((book|volume)\\\\s*\\\\d+[^)]*\\\\)\\\\s*$\",\"\\\\s*[:\\\\-]\\\\s*book\\\\s*\\\\d+[^$]*$\",\"\\\\s*[:\\\\-]\\\\s*(the\\\\s+)?(book|volume)\\\\s*\\\\d+\\\\s+of\\\\s+[^$]*$\"]";

        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MetadataQueryNormalizationService(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public List<string> ExpandAuthorAliases(IEnumerable<string> authorNames)
        {
            var aliases = ParseAliasMap();
            var results = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var authorName in authorNames ?? Enumerable.Empty<string>())
            {
                if (authorName.IsNullOrWhiteSpace())
                {
                    continue;
                }

                results.Add(authorName.Trim());
                var canonical = Canonicalize(authorName);

                if (aliases.TryGetValue(canonical, out var mappedAliases))
                {
                    foreach (var alias in mappedAliases)
                    {
                        if (alias.IsNotNullOrWhiteSpace())
                        {
                            results.Add(alias.Trim());
                        }
                    }
                }

                foreach (var kvp in aliases)
                {
                    if (!kvp.Value.Contains(canonical))
                    {
                        continue;
                    }

                    var reverse = kvp.Key;
                    if (reverse.IsNotNullOrWhiteSpace())
                    {
                        results.Add(reverse);
                    }
                }
            }

            return results.ToList();
        }

        public List<string> BuildTitleVariants(string title)
        {
            var results = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            if (title.IsNullOrWhiteSpace())
            {
                return results.ToList();
            }

            var normalized = NormalizeWhitespace(title);
            if (normalized.IsNullOrWhiteSpace())
            {
                return results.ToList();
            }

            results.Add(normalized);

            foreach (var pattern in ParseTitleStripPatterns())
            {
                var stripped = pattern.Replace(normalized, string.Empty);
                stripped = NormalizeWhitespace(stripped);

                if (stripped.IsNotNullOrWhiteSpace() && !stripped.Equals(normalized, StringComparison.InvariantCultureIgnoreCase))
                {
                    results.Add(stripped);
                }
            }

            return results.ToList();
        }

        private Dictionary<string, HashSet<string>> ParseAliasMap()
        {
            var raw = _configService.MetadataAuthorAliases;
            if (raw.IsNullOrWhiteSpace())
            {
                raw = DefaultAliasConfig;
            }

            try
            {
                var map = Json.Deserialize<Dictionary<string, List<string>>>(raw) ?? new Dictionary<string, List<string>>();
                return map.ToDictionary(
                    kvp => Canonicalize(kvp.Key),
                    kvp => new HashSet<string>((kvp.Value ?? new List<string>()).Where(v => v.IsNotNullOrWhiteSpace()).Select(v => v.Trim()), StringComparer.InvariantCultureIgnoreCase),
                    StringComparer.InvariantCultureIgnoreCase);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Unable to parse MetadataAuthorAliases config. Using defaults.");

                var defaults = Json.Deserialize<Dictionary<string, List<string>>>(DefaultAliasConfig);
                return defaults.ToDictionary(
                    kvp => Canonicalize(kvp.Key),
                    kvp => new HashSet<string>((kvp.Value ?? new List<string>()).Where(v => v.IsNotNullOrWhiteSpace()).Select(v => v.Trim()), StringComparer.InvariantCultureIgnoreCase),
                    StringComparer.InvariantCultureIgnoreCase);
            }
        }

        private List<Regex> ParseTitleStripPatterns()
        {
            var raw = _configService.MetadataTitleStripPatterns;
            if (raw.IsNullOrWhiteSpace())
            {
                raw = DefaultTitleStripPatternConfig;
            }

            List<string> patterns;
            try
            {
                patterns = Json.Deserialize<List<string>>(raw) ?? new List<string>();
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Unable to parse MetadataTitleStripPatterns config. Using defaults.");
                patterns = Json.Deserialize<List<string>>(DefaultTitleStripPatternConfig) ?? new List<string>();
            }

            var regexes = new List<Regex>();
            foreach (var pattern in patterns.Where(p => p.IsNotNullOrWhiteSpace()))
            {
                try
                {
                    regexes.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Ignoring invalid MetadataTitleStripPatterns regex: {0}", pattern);
                }
            }

            return regexes;
        }

        private static string Canonicalize(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var lowered = value.Trim().ToLowerInvariant();
            return Regex.Replace(lowered, @"\s+", " ");
        }

        private static string NormalizeWhitespace(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            return Regex.Replace(value, @"\s+", " ").Trim();
        }
    }
}
