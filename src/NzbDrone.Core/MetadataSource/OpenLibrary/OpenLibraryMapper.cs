using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

            var olid = ExtractOlid(doc.Key);

            var authorMetadata = BuildAuthorMetadataFromSearchDoc(doc);

            var edition = new Edition
            {
                ForeignEditionId = $"OL-work-{olid}",
                TitleSlug = olid,
                Title = doc.Title.CleanSpaces(),
                Isbn13 = doc.Isbn?.FirstOrDefault(),
                PageCount = doc.NumberOfPagesMedian ?? 0,
                Monitored = true,
                Ratings = new Ratings
                {
                    Votes = doc.RatingsCount ?? 0,
                    Value = (decimal)(doc.RatingsAverage ?? 0)
                }
            };

            if (doc.CoverId.HasValue)
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
                ForeignBookId = $"OL{olid}W",
                TitleSlug = olid,
                Title = doc.Title.CleanSpaces(),
                CleanTitle = Parser.Parser.CleanAuthorName(doc.Title ?? string.Empty),
                ReleaseDate = doc.FirstPublishYear.HasValue
                    ? (DateTime?)new DateTime(doc.FirstPublishYear.Value, 1, 1)
                    : null,
                Genres = doc.Subject?.Take(10).ToList() ?? new List<string>(),
                Ratings = new Ratings
                {
                    Votes = doc.RatingsCount ?? 0,
                    Value = (decimal)(doc.RatingsAverage ?? 0)
                },
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

            return book;
        }

        /// <summary>Maps an OL work (with a separately fetched primary author) to a Book.</summary>
        public static Book MapWorkToBook(OlWorkResource work, OlAuthorResource primaryAuthor)
        {
            if (work == null || work.Key.IsNullOrWhiteSpace())
            {
                return null;
            }

            var olid = ExtractOlid(work.Key);

            var authorMetadata = primaryAuthor != null
                ? MapAuthorToMetadata(primaryAuthor)
                : new AuthorMetadata { ForeignAuthorId = "OL-unknown", Name = "Unknown Author" };

            var edition = BuildEditionFromWork(work, olid);

            var book = new Book
            {
                ForeignBookId = $"OL{olid}W",
                TitleSlug = olid,
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

            var olid = ExtractOlid(author.Key);

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = $"OL{olid}A",
                TitleSlug = olid,
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

            if (author.Photos?.Any() == true)
            {
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    Url = string.Format(OlCoverTemplate.Replace("/b/", "/a/"), author.Photos.First()),
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

            var olid = ExtractOlid(edition.Key);
            var isbn13 = edition.Isbn13?.FirstOrDefault() ?? edition.Isbn10?.FirstOrDefault();

            var result = new Edition
            {
                ForeignEditionId = $"OL{olid}M",
                TitleSlug = olid,
                Title = (edition.Title ?? string.Empty).CleanSpaces(),
                Isbn13 = isbn13,
                Publisher = edition.Publishers?.FirstOrDefault(),
                PageCount = edition.NumberOfPages ?? 0,
                ReleaseDate = ParsePublishDate(edition.PublishDate),
                Format = edition.PhysicalFormat,
                Overview = edition.Description,
                Monitored = true,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            if (edition.Covers?.Any() == true)
            {
                result.Images.Add(new MediaCover.MediaCover
                {
                    Url = string.Format(OlCoverTemplate, edition.Covers.First()),
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
            var authorOlid = doc.AuthorKey?.FirstOrDefault();
            var foreignAuthorId = authorOlid.IsNotNullOrWhiteSpace()
                ? $"OL{ExtractOlid(authorOlid)}A"
                : $"OL-search-{authorName.ToLower().Replace(" ", "-")}";

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = foreignAuthorId,
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
                Monitored = true,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            if (work.Covers?.Any() == true)
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = string.Format(OlCoverTemplate, work.Covers.First()),
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
    }
}
