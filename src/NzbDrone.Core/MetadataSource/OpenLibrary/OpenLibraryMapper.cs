using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Maps Open Library API resource types to Bibliophilarr domain models.
    /// All methods are static and deterministic — safe to unit-test without I/O.
    /// </summary>
    public static class OpenLibraryMapper
    {
        private const string OlCoverTemplate = "https://covers.openlibrary.org/b/id/{0}-L.jpg";
        private const string OlWorkBaseUrl = "https://openlibrary.org";
        private static readonly Regex SeriesWithNumberRegex = new Regex(@"^(?<title>.+?)\s*#(?<position>[0-9]+(?:\.[0-9]+)?)$", RegexOptions.Compiled);
        private static readonly Regex InvalidSeriesIdCharsRegex = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LeadingSeriesNumberRegex = new Regex(@"^(?<position>[0-9]+(?:\.[0-9]+)?)$", RegexOptions.Compiled);

        // ── Public entry points ──────────────────────────────────────────────

        /// <summary>
        /// Maps an OL search document to a lightweight Book (no editions populated).
        /// Useful for search-result lists where per-edition detail is not fetched.
        /// </summary>
        public static Book MapSearchDocToBook(OlSearchDoc doc)
        {
            if (doc == null || doc.Key.IsNullOrWhiteSpace())
            {
                return null;
            }

            var workId = OpenLibraryIdNormalizer.EnsureToken(ExtractOlid(doc.Key), "W");

            var authorMetadata = BuildAuthorMetadataFromSearchDoc(doc);

            var edition = new Edition
            {
                ForeignEditionId = $"openlibrary:work:{workId}",
                TitleSlug = workId,
                Title = doc.Title.CleanSpaces(),
                Isbn13 = doc.Isbn?.FirstOrDefault(),
                Language = GetPreferredLanguage(doc.Language),
                PageCount = doc.NumberOfPagesMedian ?? 0,
                Monitored = true,
                Ratings = BuildRatingsFromSearchDoc(doc)
            };

            if (doc.CoverId.GetValueOrDefault() > 0)
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = string.Format(OlCoverTemplate, doc.CoverId.Value),
                    CoverType = MediaCoverTypes.Cover
                });
            }

            edition.Links.Add(new Links { Url = $"{OlWorkBaseUrl}{doc.Key}", Name = "Open Library" });

            var book = new Book
            {
                ForeignBookId = $"openlibrary:work:{workId}",
                OpenLibraryWorkId = workId,
                TitleSlug = $"openlibrary:work:{workId}",
                Title = doc.Title.CleanSpaces(),
                CleanTitle = Parser.Parser.CleanAuthorName(doc.Title ?? string.Empty),
                ReleaseDate = ToYearDate(doc.FirstPublishYear),
                Genres = doc.Subject?.Take(10).ToList() ?? new List<string>(),
                Ratings = BuildRatingsFromSearchDoc(doc),
                AnyEditionOk = true
            };

            book.Editions = new List<Edition> { edition };
            book.Links.Add(new Links { Url = $"{OlWorkBaseUrl}{doc.Key}", Name = "Open Library" });

            var author = new Author
            {
                Metadata = authorMetadata,
                CleanName = Parser.Parser.CleanAuthorName(authorMetadata.Name ?? string.Empty)
            };

            book.Author = author;
            book.AuthorMetadata = authorMetadata;
            book.SeriesLinks = BuildSeriesLinks(doc, authorMetadata.OpenLibraryAuthorId, book);

            return book;
        }

        /// <summary>Maps an OL work (with a separately fetched primary author) to a Book.</summary>
        public static Book MapWorkToBook(OlWorkResource work, OlAuthorResource primaryAuthor)
        {
            if (work == null || work.Key.IsNullOrWhiteSpace())
            {
                return null;
            }

            var workId = OpenLibraryIdNormalizer.EnsureToken(ExtractOlid(work.Key), "W");

            var authorMetadata = primaryAuthor != null
                ? MapAuthorToMetadata(primaryAuthor)
                : new AuthorMetadata { ForeignAuthorId = "OL-unknown", Name = "Unknown Author" };

            var edition = BuildEditionFromWork(work, workId);

            var book = new Book
            {
                ForeignBookId = $"openlibrary:work:{workId}",
                OpenLibraryWorkId = workId,
                TitleSlug = $"openlibrary:work:{workId}",
                Title = (work.Title ?? string.Empty).CleanSpaces(),
                CleanTitle = Parser.Parser.CleanAuthorName(work.Title ?? string.Empty),
                ReleaseDate = ParseYear(work.FirstPublishDate),
                Genres = CombineSubjects(work),
                Ratings = new Ratings { Votes = 0, Value = 0 },
                AnyEditionOk = true
            };

            book.Editions = new List<Edition> { edition };
            book.Links.Add(new Links { Url = $"{OlWorkBaseUrl}{work.Key}", Name = "Open Library" });

            var author = new Author
            {
                Metadata = authorMetadata,
                CleanName = Parser.Parser.CleanAuthorName(authorMetadata.Name ?? string.Empty)
            };

            book.Author = author;
            book.AuthorMetadata = authorMetadata;

            return book;
        }

        /// <summary>Maps an OL author to a Bibliophilarr AuthorMetadata.</summary>
        public static AuthorMetadata MapAuthorToMetadata(OlAuthorResource author)
        {
            if (author == null || author.Key.IsNullOrWhiteSpace())
            {
                return null;
            }

            var authorId = OpenLibraryIdNormalizer.EnsureToken(ExtractOlid(author.Key), "A");

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = $"openlibrary:author:{authorId}",
                OpenLibraryAuthorId = authorId,
                TitleSlug = authorId,
                Name = (author.Name ?? author.PersonalName ?? string.Empty).CleanSpaces(),
                Overview = author.Bio,
                Status = AuthorStatusType.Continuing,
                Born = ParseDate(author.BirthDate),
                Died = ParseDate(author.DeathDate),
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();

            var authorPhotoId = author.Photos?.FirstOrDefault(x => x > 0);
            if (authorPhotoId.GetValueOrDefault() > 0)
            {
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    Url = string.Format(OlCoverTemplate.Replace("/b/", "/a/"), authorPhotoId.Value),
                    CoverType = MediaCoverTypes.Poster
                });
            }

            metadata.Links.Add(new Links { Url = $"{OlWorkBaseUrl}{author.Key}", Name = "Open Library" });

            if (author.Wikipedia.IsNotNullOrWhiteSpace())
            {
                metadata.Links.Add(new Links { Url = author.Wikipedia, Name = "Wikipedia" });
            }

            return metadata;
        }

        /// <summary>Maps an OL edition to a Bibliophilarr Edition.</summary>
        public static Edition MapEdition(OlEditionResource edition)
        {
            if (edition == null || edition.Key.IsNullOrWhiteSpace())
            {
                return null;
            }

            var editionId = OpenLibraryIdNormalizer.EnsureToken(ExtractOlid(edition.Key), "M");
            var isbn13 = edition.Isbn13?.FirstOrDefault() ?? edition.Isbn10?.FirstOrDefault();

            var result = new Edition
            {
                ForeignEditionId = $"openlibrary:edition:{editionId}",
                TitleSlug = editionId,
                Title = (edition.Title ?? string.Empty).CleanSpaces(),
                Isbn13 = isbn13,
                Publisher = edition.Publishers?.FirstOrDefault(),
                PageCount = edition.NumberOfPages ?? 0,
                ReleaseDate = ParsePublishDate(edition.PublishDate),
                Language = GetPreferredLanguage(edition.Languages?.Select(x => x?.Key)),
                Format = edition.PhysicalFormat,
                IsEbook = IsEbookFormat(edition.PhysicalFormat),
                Overview = edition.Description,
                Monitored = true,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            var editionCoverId = edition.Covers?.FirstOrDefault(x => x > 0);
            if (editionCoverId.GetValueOrDefault() > 0)
            {
                result.Images.Add(new MediaCover.MediaCover
                {
                    Url = string.Format(OlCoverTemplate, editionCoverId.Value),
                    CoverType = MediaCoverTypes.Cover
                });
            }

            result.Links.Add(new Links { Url = $"{OlWorkBaseUrl}{edition.Key}", Name = "Open Library Edition" });

            return result;
        }

        // ── Private helpers ──────────────────────────────────────────────────
        private static AuthorMetadata BuildAuthorMetadataFromSearchDoc(OlSearchDoc doc)
        {
            var authorName = doc.AuthorName?.FirstOrDefault() ?? "Unknown Author";
            var authorKey = doc.AuthorKey?.FirstOrDefault();
            var authorId = authorKey.IsNotNullOrWhiteSpace()
                ? OpenLibraryIdNormalizer.EnsureToken(ExtractOlid(authorKey), "A")
                : null;
            var foreignAuthorId = authorId.IsNotNullOrWhiteSpace()
                ? $"openlibrary:author:{authorId}"
                : $"OL-search-{authorName.ToLower().Replace(" ", "-")}";

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = foreignAuthorId,
                OpenLibraryAuthorId = authorId,
                TitleSlug = foreignAuthorId,
                Name = authorName,
                Status = AuthorStatusType.Continuing,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();

            return metadata;
        }

        private static Edition BuildEditionFromWork(OlWorkResource work, string workOlid)
        {
            var edition = new Edition
            {
                ForeignEditionId = $"OL-work-{workOlid}",
                TitleSlug = workOlid,
                Title = (work.Title ?? string.Empty).CleanSpaces(),
                Overview = work.Description,
                ReleaseDate = ParseYear(work.FirstPublishDate),
                Monitored = true,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            var workCoverId = work.Covers?.FirstOrDefault(x => x > 0);
            if (workCoverId.GetValueOrDefault() > 0)
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = string.Format(OlCoverTemplate, workCoverId.Value),
                    CoverType = MediaCoverTypes.Cover
                });
            }

            edition.Links.Add(new Links { Url = $"{OlWorkBaseUrl}{work.Key}", Name = "Open Library" });

            return edition;
        }

        private static List<string> CombineSubjects(OlWorkResource work)
        {
            var subjects = new List<string>();
            if (work.Subjects != null)
            {
                subjects.AddRange(work.Subjects);
            }

            if (work.SubjectPlaces != null)
            {
                subjects.AddRange(work.SubjectPlaces);
            }

            if (work.SubjectPeople != null)
            {
                subjects.AddRange(work.SubjectPeople);
            }

            return subjects.Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        }

        private static List<SeriesBookLink> BuildSeriesLinks(OlSearchDoc doc, string authorId, Book book)
        {
            var seriesLinks = new List<SeriesBookLink>();

            if (doc == null || book == null)
            {
                return seriesLinks;
            }

            var descriptors = ExtractSeriesDescriptors(doc);
            if (!descriptors.Any())
            {
                return seriesLinks;
            }

            var seenSeriesIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < descriptors.Count; i++)
            {
                var descriptor = descriptors[i];
                var foreignSeriesId = BuildSeriesForeignId(authorId, descriptor.Title);

                if (foreignSeriesId.IsNullOrWhiteSpace() || !seenSeriesIds.Add(foreignSeriesId))
                {
                    continue;
                }

                var series = new Series
                {
                    ForeignSeriesId = foreignSeriesId,
                    Title = descriptor.Title,
                    Numbered = descriptor.Position.IsNotNullOrWhiteSpace(),
                    LinkItems = new List<SeriesBookLink>()
                };

                var link = new SeriesBookLink
                {
                    Book = book,
                    Series = series,
                    Position = descriptor.Position,
                    SeriesPosition = ParseSeriesPosition(descriptor.Position),
                    IsPrimary = i == 0
                };

                series.LinkItems = new List<SeriesBookLink> { link };
                seriesLinks.Add(link);
            }

            return seriesLinks;
        }

        private static List<SeriesDescriptor> ExtractSeriesDescriptors(OlSearchDoc doc)
        {
            var descriptors = new List<SeriesDescriptor>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var value in doc.SeriesWithNumber ?? new List<string>())
            {
                var normalized = ParseSeriesWithNumber(value);

                if (normalized != null && seen.Add(normalized.Title))
                {
                    descriptors.Add(normalized);
                }
            }

            foreach (var value in doc.Series ?? new List<string>())
            {
                if (value.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var title = value.Trim();
                if (seen.Add(title))
                {
                    descriptors.Add(new SeriesDescriptor { Title = title });
                }
            }

            return descriptors;
        }

        private static SeriesDescriptor ParseSeriesWithNumber(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            var trimmed = value.Trim();
            var match = SeriesWithNumberRegex.Match(trimmed);

            if (!match.Success)
            {
                return new SeriesDescriptor { Title = trimmed };
            }

            var title = match.Groups["title"].Value.Trim();
            var position = match.Groups["position"].Value.Trim();

            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            return new SeriesDescriptor
            {
                Title = title,
                Position = position
            };
        }

        private static string BuildSeriesForeignId(string authorId, string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            var normalizedTitle = InvalidSeriesIdCharsRegex
                .Replace(title.Trim().ToLowerInvariant(), "-")
                .Trim('-');

            if (normalizedTitle.IsNullOrWhiteSpace())
            {
                return null;
            }

            var normalizedAuthor = authorId.IsNotNullOrWhiteSpace()
                ? InvalidSeriesIdCharsRegex.Replace(authorId.ToLowerInvariant(), "-").Trim('-')
                : "unknown-author";

            return $"openlibrary:series:{normalizedAuthor}:{normalizedTitle}";
        }

        private static int ParseSeriesPosition(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return 0;
            }

            var candidate = value.Trim();
            var match = LeadingSeriesNumberRegex.Match(candidate);

            if (!match.Success)
            {
                return 0;
            }

            var raw = match.Groups["position"].Value;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
            {
                return parsedInt;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                return (int)Math.Floor(parsedDouble);
            }

            return 0;
        }

        private static DateTime? ToYearDate(int? year)
        {
            if (!year.HasValue || year.Value < DateTime.MinValue.Year || year.Value > DateTime.MaxValue.Year)
            {
                return null;
            }

            return new DateTime(year.Value, 1, 1);
        }

        private sealed class SeriesDescriptor
        {
            public string Title { get; set; }
            public string Position { get; set; }
        }

        private static string ExtractOlid(string key)
        {
            if (key.IsNullOrWhiteSpace())
            {
                return key;
            }

            var last = key.LastIndexOf('/');
            var raw = last >= 0 ? key.Substring(last + 1) : key;

            // Strip type suffix letter + trailing chars: OL26320A -> 26320
            // Keep the full token as-is for use as ForeignId (e.g. OL26320A)
            return raw;
        }

        private static DateTime? ParseYear(string text)
        {
            if (text.IsNullOrWhiteSpace())
            {
                return null;
            }

            if (int.TryParse(text.Trim(), out var year) && year > 1000)
            {
                return new DateTime(year, 1, 1);
            }

            return null;
        }

        private static DateTime? ParseDate(string text)
        {
            if (text.IsNullOrWhiteSpace())
            {
                return null;
            }

            // OL uses formats like "3 January 1892" or just "1892"
            string[] formats =
            {
                "d MMMM yyyy", "MMMM d, yyyy", "yyyy", "d MMM yyyy", "MMM d, yyyy"
            };

            if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt;
            }

            if (int.TryParse(text.Trim(), out var year) && year > 1000)
            {
                return new DateTime(year, 1, 1);
            }

            return null;
        }

        private static DateTime? ParsePublishDate(string text)
        {
            if (text.IsNullOrWhiteSpace())
            {
                return null;
            }

            string[] formats =
            {
                "yyyy", "MMMM yyyy", "MMMM d, yyyy", "d MMMM yyyy",
                "MMM d, yyyy", "MMM yyyy", "MM/dd/yyyy"
            };

            if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt;
            }

            // last-resort: extract a 4-digit year
            var yearMatch = System.Text.RegularExpressions.Regex.Match(text, @"\b(\d{4})\b");
            if (yearMatch.Success && int.TryParse(yearMatch.Value, out var year) && year > 1000)
            {
                return new DateTime(year, 1, 1);
            }

            return null;
        }

        private static Ratings BuildRatingsFromSearchDoc(OlSearchDoc doc)
        {
            var ratingValue = (decimal)(doc.RatingsAverage ?? 0);
            var ratingVotes = doc.RatingsCount ?? 0;
            var ratingPopularity = (double)ratingValue * ratingVotes;
            var readingPopularity = (doc.WantToReadCount ?? 0) + (doc.CurrentlyReadingCount ?? 0) + (doc.AlreadyReadCount ?? 0);

            if (readingPopularity > ratingPopularity)
            {
                var effectiveValue = ratingValue > 0 ? ratingValue : 1m;
                var effectiveVotes = (int)Math.Ceiling(readingPopularity / (double)effectiveValue);

                return new Ratings
                {
                    Votes = Math.Max(effectiveVotes, 1),
                    Value = effectiveValue
                };
            }

            return new Ratings
            {
                Votes = ratingVotes,
                Value = ratingValue
            };
        }

        private static string GetPreferredLanguage(IEnumerable<string> languages)
        {
            var tokens = languages?
                .Select(ExtractLanguageToken)
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tokens == null || tokens.Count == 0)
            {
                return null;
            }

            return tokens.FirstOrDefault(x => x.Equals("eng", StringComparison.OrdinalIgnoreCase)) ?? tokens.First();
        }

        private static string ExtractLanguageToken(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            var last = value.LastIndexOf('/');
            return (last >= 0 ? value.Substring(last + 1) : value).Trim();
        }

        private static bool IsEbookFormat(string format)
        {
            if (format.IsNullOrWhiteSpace())
            {
                return false;
            }

            var normalized = format.ToLowerInvariant();
            return normalized.Contains("ebook") || normalized.Contains("e-book") || normalized.Contains("epub") || normalized.Contains("kindle") || normalized.Contains("pdf") || normalized.Contains("digital");
        }
    }
}
