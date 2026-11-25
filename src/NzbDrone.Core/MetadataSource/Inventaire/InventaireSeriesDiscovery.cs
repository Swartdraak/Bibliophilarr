using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.Inventaire
{
    public interface IInventaireSeriesDiscovery
    {
        List<SeriesSearchSummary> Search(string term, CancellationToken requestAborted);
        List<InventaireSeriesMember> GetMembers(string foreignSeriesId, CancellationToken requestAborted);
    }

    public sealed class InventaireSeriesMember
    {
        public string ForeignWorkId { get; init; }
        public string Title { get; init; }
        public string Position { get; init; }
        public string SourceUrl { get; init; }
    }

    public sealed class InventaireSeriesDiscovery : IInventaireSeriesDiscovery
    {
        private const string BaseUrl = "https://inventaire.io";
        private const string SeriesPrefix = "inventaire:series:";
        private const string WorkPrefix = "inventaire:work:";
        private const string SeriesClaim = "wdt:P179";
        private const string OrdinalClaim = "wdt:P1545";
        private const int SearchLimit = 10;
        private const int MaximumMembers = 40;
        private const int MaximumResponseBytes = 256 * 1024;
        private static readonly Regex EntityUri = new Regex(@"\A(?:wd:Q[1-9][0-9]{0,18}|inv:[a-f0-9]{32})\z", RegexOptions.Compiled);
        private static readonly Regex ExactOrdinal = new Regex(@"\A[1-9][0-9]{0,5}(?:\.[0-9]{1,4})?\z", RegexOptions.Compiled);

        private readonly IConfigService _config;
        private readonly IHttpClient _client;

        public InventaireSeriesDiscovery(IConfigService config, IHttpClient client)
        {
            _config = config;
            _client = client;
        }

        public static bool IsSeriesId(string foreignId)
        {
            return foreignId?.StartsWith(SeriesPrefix, StringComparison.Ordinal) == true &&
                   IsEntityUri(foreignId[SeriesPrefix.Length..]);
        }

        public List<SeriesSearchSummary> Search(string term, CancellationToken requestAborted = default)
        {
            var query = term?.Trim();
            if (!_config.EnableInventaireFallback || query is not { Length: >= 3 and <= 128 } || query.Any(character => char.IsControl(character) || character is '/' or '\\') || Guid.TryParse(query, out _))
            {
                return null;
            }

            using var budget = CreateBudget(requestAborted);
            var response = Read(new HttpRequestBuilder(BaseUrl + "/api/search")
                .AddQueryParam("types", "series")
                .AddQueryParam("search", query)
                .AddQueryParam("lang", "en")
                .AddQueryParam("limit", SearchLimit.ToString(CultureInfo.InvariantCulture)), budget.Token);
            if (response?["results"] is not JArray { Count: <= SearchLimit } results)
            {
                return null;
            }

            var series = new List<SeriesSearchSummary>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in results.OfType<JObject>())
            {
                var uri = Scalar(item["uri"]);
                var title = Label(item["label"]);
                if (Scalar(item["type"]) == "series" && IsEntityUri(uri) &&
                    Scalar(item["id"]) == uri.Substring(uri.IndexOf(':') + 1) && title != null && seen.Add(uri))
                {
                    series.Add(new SeriesSearchSummary
                    {
                        ForeignSeriesId = SeriesPrefix + uri,
                        Title = title,
                        SourceProvider = InventaireRateLimitGate.ProviderName,
                        SourceUrl = BaseUrl + "/entity/" + uri
                    });
                }
            }

            return results.Count > 0 && series.Count == 0 ? null : series;
        }

        public List<InventaireSeriesMember> GetMembers(string foreignSeriesId, CancellationToken requestAborted = default)
        {
            if (!_config.EnableInventaireFallback || !IsSeriesId(foreignSeriesId))
            {
                return null;
            }

            var seriesUri = foreignSeriesId[SeriesPrefix.Length..];
            using var budget = CreateBudget(requestAborted);
            var parts = ReadParts(seriesUri, budget.Token);
            if (parts == null)
            {
                return null;
            }

            var ids = parts.Select(part => part.Uri).Prepend(seriesUri).ToHashSet(StringComparer.Ordinal);
            var data = Read(new HttpRequestBuilder(BaseUrl + "/api/entities/by-uris")
                .AddQueryParam("uris", string.Join('|', parts.Select(part => part.Uri).Prepend(seriesUri)))
                .AddQueryParam("attributes", "info|labels|claims")
                .AddQueryParam("lang", "en"), budget.Token);
            if (data?["entities"] is not JObject entities || data["redirects"] is not JObject { Count: 0 } ||
                (data["notFound"] != null && data["notFound"] is not JArray { Count: 0 }) ||
                !ids.SetEquals(entities.Properties().Select(entry => entry.Name)) ||
                !IsExactEntity(entities[seriesUri], seriesUri, "serie"))
            {
                return null;
            }

            var direct = new List<(Part Part, string Title, decimal? Position)>();
            foreach (var part in parts)
            {
                var entity = entities[part.Uri] as JObject;
                var type = Scalar(entity?["type"]);
                if (!IsExactEntity(entity, part.Uri, type) || (part.Type != null && part.Type != type))
                {
                    return null;
                }

                if (type != "work")
                {
                    continue;
                }

                if (entity["claims"] is not JObject claims ||
                    (claims[SeriesClaim] != null && claims[SeriesClaim] is not JArray))
                {
                    return null;
                }

                if (claims[SeriesClaim] is not JArray memberships || !memberships.Any(value => Scalar(value) == seriesUri))
                {
                    continue;
                }

                var title = SelectedLabel(entity["labels"]);
                if (title == null)
                {
                    return null;
                }

                direct.Add((part, title, Position(part, claims, memberships, seriesUri)));
            }

            var uniquePositions = direct.Where(item => item.Position.HasValue).GroupBy(item => item.Position.Value)
                .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet();
            return direct.Select(item => (item.Part, item.Title,
                    Position: item.Position.HasValue && uniquePositions.Contains(item.Position.Value) ? item.Position : null))
                .OrderBy(item => !item.Position.HasValue)
                .ThenBy(item => item.Position)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Part.Uri, StringComparer.Ordinal)
                .Select(item => new InventaireSeriesMember
                {
                    ForeignWorkId = WorkPrefix + item.Part.Uri,
                    Title = item.Title,
                    Position = item.Position?.ToString(CultureInfo.InvariantCulture),
                    SourceUrl = BaseUrl + "/entity/" + item.Part.Uri
                })
                .ToList();
        }

        private List<Part> ReadParts(string seriesUri, CancellationToken cancellationToken)
        {
            var data = Read(new HttpRequestBuilder(BaseUrl + "/api/entities/serie-parts").AddQueryParam("uri", seriesUri), cancellationToken);
            if (data?["parts"] is not JArray { Count: <= MaximumMembers } entries)
            {
                return null;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var parts = new List<Part>();
            foreach (var token in entries)
            {
                if (token is not JObject entry)
                {
                    return null;
                }

                var uri = Scalar(entry["uri"]);
                var type = Scalar(entry["type"]);
                if (!IsEntityUri(uri) || uri == seriesUri || !seen.Add(uri) ||
                    (entry["type"] != null && type is not "work" and not "serie"))
                {
                    return null;
                }

                parts.Add(new Part(uri, type, Scalar(entry["ordinal"])));
            }

            return parts;
        }

        private CancellationTokenSource CreateBudget(CancellationToken requestAborted)
        {
            var budget = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_config.MetadataProviderTimeoutSeconds, 5, 15)));
            return budget;
        }

        private JObject Read(HttpRequestBuilder builder, CancellationToken cancellationToken)
        {
            if (!_config.EnableInventaireFallback || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var request = builder.Accept(HttpAccept.Json).Build();
            request.AllowAutoRedirect = false;
            request.IgnoreStoredOriginCredentials = true;
            request.OmitSensitiveDiagnostics = true;
            request.SuppressHttpError = true;
            request.RateLimitKey = InventaireRateLimitGate.ProviderName;
            request.CancellationToken = cancellationToken;
            request.DiscardNonOkResponseBody = true;
            var buffer = new byte[MaximumResponseBytes];
            using var body = new MemoryStream(buffer, writable: true);
            body.SetLength(0);
            request.ResponseStream = body;
            try
            {
                var response = _client.Get(request);
                if (cancellationToken.IsCancellationRequested || response?.StatusCode != HttpStatusCode.OK || body.Length == 0)
                {
                    return null;
                }

                body.Position = 0;
                using var reader = new StreamReader(body);
                using var json = new JsonTextReader(reader);
                var parsed = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                return json.Read() ? null : parsed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception error) when (error is HttpException or System.Net.Http.HttpRequestException or WebException or TimeoutException or JsonException or IOException or NotSupportedException)
            {
                return null;
            }
        }

        private static bool IsExactEntity(JToken value, string uri, string type)
        {
            return type != null && value is JObject entity && Scalar(entity["uri"]) == uri && Scalar(entity["type"]) == type &&
                   (entity["_meta_type"] == null || Scalar(entity["_meta_type"]) == "entity");
        }

        private static decimal? Position(Part part, JObject claims, JArray memberships, string seriesUri)
        {
            if (!part.Uri.StartsWith("inv:", StringComparison.Ordinal) || part.Ordinal == null ||
                !ExactOrdinal.IsMatch(part.Ordinal) || memberships.Count != 1 || Scalar(memberships[0]) != seriesUri ||
                claims[OrdinalClaim] is not JArray { Count: 1 } ordinals || Scalar(ordinals[0]) != part.Ordinal)
            {
                return null;
            }

            return decimal.Parse(part.Ordinal, CultureInfo.InvariantCulture);
        }

        private static string SelectedLabel(JToken value)
        {
            if (value is not JObject labels)
            {
                return null;
            }

            // Inventaire returns a single selected label, possibly in the original language.
            return Label(labels["en"]) ?? (labels.Count == 1 ? Label(labels.Properties().Single().Value) : null);
        }

        private static bool IsEntityUri(string uri) => uri is { Length: <= 36 } && EntityUri.IsMatch(uri);
        private static string Scalar(JToken value) => value?.Type == JTokenType.String ? value.Value<string>() : null;

        private static string Label(JToken value)
        {
            var text = Scalar(value)?.Trim();
            return text is { Length: > 0 and <= 255 } && !text.Any(char.IsControl) ? text : null;
        }

        private sealed class Part(string uri, string type, string ordinal)
        {
            public string Uri { get; } = uri;
            public string Type { get; } = type;
            public string Ordinal { get; } = ordinal;
        }
    }
}
