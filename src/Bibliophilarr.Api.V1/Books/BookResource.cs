using System;
using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Http.REST;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Qualities;
using Swashbuckle.AspNetCore.Annotations;

namespace Bibliophilarr.Api.V1.Books
{
    public class BookResource : RestResource
    {
        public string Title { get; set; }
        public string AuthorTitle { get; set; }
        public string SeriesTitle { get; set; }
        public string Disambiguation { get; set; }
        public string Overview { get; set; }
        public int AuthorId { get; set; }
        public string ForeignBookId { get; set; }
        public string ForeignEditionId { get; set; }
        [SwaggerSchema(ReadOnly = true)]
        public string OpenLibraryWorkId { get; set; }
        [SwaggerSchema(ReadOnly = true)]
        public string MetadataSourceLabel { get; set; }
        public string TitleSlug { get; set; }
        public bool Monitored { get; set; }
        public bool AnyEditionOk { get; set; }
        public Ratings Ratings { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public int PageCount { get; set; }
        public List<string> Genres { get; set; }
        public AuthorResource Author { get; set; }
        public List<MediaCover> Images { get; set; }
        public List<Links> Links { get; set; }
        public BookStatisticsResource Statistics { get; set; }
        public DateTime? Added { get; set; }
        public AddBookOptions AddOptions { get; set; }
        public string RemoteCover { get; set; }
        public DateTime? LastSearchTime { get; set; }
        public List<EditionResource> Editions { get; set; }
        public List<BookFormatStatusResource> FormatStatuses { get; set; }

        internal bool AllowsFormatMonitoring { get; set; }

        //Hiding this so people don't think its usable (only used to set the initial state)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [SwaggerIgnore]
        public bool Grabbed { get; set; }
    }

    public static class BookResourceMapper
    {
        public static BookResource ToResource(this Book model)
        {
            if (model == null)
            {
                return null;
            }

            // Issue #250: complete the #249 null-safe mapping repair for the BOOK
            // branch.  Every nested LazyLoaded here can hold a null Value when a
            // metadata provider returns a partial result (e.g. the Editions list
            // exists but is empty/null, the Author row was resolved but its
            // AuthorMetadata row was not, or a SeriesBookLink points at an
            // unresolved Series).  Capture each nested value exactly once and
            // guard every dereference, mirroring the AuthorResource fix from #249
            // (AuthorResourceMapper.ToResource).
            var editionsValue = model.Editions?.Value;
            var selectedEdition = editionsValue.GetPreferredEdition();

            var title = selectedEdition?.Title ?? model.Title;
            var canonicalOpenLibraryId = OpenLibraryIdNormalizer.NormalizeWorkId(model.ForeignBookId);
            var openLibraryWorkId = OpenLibraryIdNormalizer.NormalizeWorkId(model.OpenLibraryWorkId);
            var bookAuthor = model.Author?.Value;
            var allowsFormatMonitoring = model.Monitored && bookAuthor?.Monitored == true;
            var authorTitle = $"{bookAuthor?.Metadata?.Value?.SortNameLastFirst ?? string.Empty} {title}";

            var seriesLinks = model.SeriesLinks?.Value?.OrderBy(x => x.SeriesPosition);
            var seriesTitle = seriesLinks?
                .Select(x =>
                {
                    var series = x?.Series?.Value;
                    return series?.Title + (x?.Position.IsNotNullOrWhiteSpace() ?? false ? $" #{x.Position}" : string.Empty);
                })
                .ConcatToString("; ");

            var formatStatuses = new List<BookFormatStatusResource>();
            if (editionsValue != null)
            {
                // Collect all book files across all editions and group by derived format type.
                // This ensures format is determined from actual file quality (e.g. EPUB → Ebook,
                // M4B → Audiobook) rather than relying solely on Edition.IsEbook, which may not
                // be set correctly by all metadata providers.
                // Issue #250: Edition.BookFiles.Value can be a null list (provider partial
                // result) — guard the second dereference too.
                var allFiles = editionsValue
                    .Where(e => e.BookFiles?.Value != null)
                    .SelectMany(e => e.BookFiles.Value ?? new List<BookFile>())
                    .ToList();

                var ebookFiles = allFiles.Where(f => Quality.GetFormatType(f.Quality.Quality) == FormatType.Ebook).ToList();
                var audiobookFiles = allFiles.Where(f => Quality.GetFormatType(f.Quality.Quality) == FormatType.Audiobook).ToList();

                // Also check edition-level classification for books without files
                var hasEbookEdition = editionsValue.Any(e => e.IsEbook);
                var hasAudiobookEdition = editionsValue.Any(e => e.MatchesFormat(FormatType.Audiobook));

                // Determine monitored status from editions
                var monitoredEbookEdition = editionsValue.Where(e => e.IsEbook).FirstOrDefault(e => e.Monitored);
                var monitoredAudiobookEdition = editionsValue.FirstOrDefault(e => e.MatchesFormat(FormatType.Audiobook) && e.Monitored);

                // Emit ebook status if we have ebook files OR a classified ebook edition
                if (ebookFiles.Any() || hasEbookEdition)
                {
                    formatStatuses.Add(new BookFormatStatusResource
                    {
                        FormatType = FormatType.Ebook,
                        Monitored = monitoredEbookEdition != null,
                        HasFile = ebookFiles.Any(),
                        FileCount = ebookFiles.Count
                    });
                }

                // Emit audiobook status if we have audiobook files OR a classified audiobook edition
                if (audiobookFiles.Any() || hasAudiobookEdition)
                {
                    formatStatuses.Add(new BookFormatStatusResource
                    {
                        FormatType = FormatType.Audiobook,
                        Monitored = monitoredAudiobookEdition != null,
                        HasFile = audiobookFiles.Any(),
                        FileCount = audiobookFiles.Count
                    });
                }
            }

            return new BookResource
            {
                Id = model.Id,
                AuthorId = model.AuthorId,
                ForeignBookId = model.ForeignBookId,
                ForeignEditionId = selectedEdition?.ForeignEditionId,
                OpenLibraryWorkId = IsOpenLibraryWorkId(openLibraryWorkId) ? openLibraryWorkId : IsOpenLibraryWorkId(canonicalOpenLibraryId) ? canonicalOpenLibraryId : null,
                MetadataSourceLabel = GetMetadataSourceLabel(model.ForeignBookId, canonicalOpenLibraryId),
                TitleSlug = model.TitleSlug,
                Monitored = model.Monitored,
                AnyEditionOk = model.AnyEditionOk,
                ReleaseDate = model.ReleaseDate,
                PageCount = selectedEdition?.PageCount ?? 0,
                Genres = model.Genres,
                Title = title,
                AuthorTitle = authorTitle,
                SeriesTitle = seriesTitle,
                Disambiguation = selectedEdition?.Disambiguation,
                Images = editionsValue.GetBookImages().JsonClone(),
                Links = model.Links.Concat(selectedEdition?.Links ?? new List<Links>()).GroupBy(l => l.Url).Select(g => g.First()).ToList(),
                Ratings = selectedEdition?.Ratings ?? new Ratings(),
                Added = model.Added,
                LastSearchTime = model.LastSearchTime,
                Editions = editionsValue?.Select(e => e.ToResource()).ToList() ?? new List<EditionResource>(),
                FormatStatuses = formatStatuses,
                AllowsFormatMonitoring = allowsFormatMonitoring
            };
        }

        private static bool IsOpenLibraryWorkId(string value)
        {
            return value?.EndsWith("W", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static string GetMetadataSourceLabel(string foreignBookId, string canonicalOpenLibraryId)
        {
            if (IsOpenLibraryWorkId(canonicalOpenLibraryId))
            {
                return "Open Library";
            }

            return foreignBookId?.Split(':', 2)[0].ToLowerInvariant() switch
            {
                "hardcover" => "Hardcover",
                "googlebooks" => "Google Books",
                "inventaire" => "Inventaire",
                _ => null
            };
        }

        public static Book ToModel(this BookResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            var author = resource.Author?.ToModel() ?? new NzbDrone.Core.Books.Author();

            // Issue #279: resource.AddOptions is null when the client omits the
            // optional addOptions field (it is not required by the API schema).
            // Copying null here overwrote the Book constructor's
            // "AddOptions = new AddBookOptions()" initialization, and
            // AddBookService.AddBook then threw NullReferenceException on
            // "book.AddOptions.AddType = BookAddType.Manual;" (AddBookService.cs:63),
            // surfacing as a 500 instead of 201. Substitute a fresh AddBookOptions
            // so the service layer never sees a null AddOptions.
            return new Book
            {
                Id = resource.Id,
                ForeignBookId = resource.ForeignBookId,
                ForeignEditionId = resource.ForeignEditionId,
                TitleSlug = resource.TitleSlug,
                Title = resource.Title,
                Monitored = resource.Monitored,
                AnyEditionOk = resource.AnyEditionOk,
                Editions = resource.Editions.ToModel(),
                AddOptions = resource.AddOptions ?? new AddBookOptions(),
                Author = author,
                AuthorMetadata = author.Metadata.Value
            };
        }

        public static Book ToModel(this BookResource resource, Book book)
        {
            var updatedBook = resource.ToModel();

            book.ApplyChanges(updatedBook);
            book.Editions = updatedBook.Editions;

            return book;
        }

        public static List<BookResource> ToResource(this IEnumerable<Book> models)
        {
            return models?.Select(ToResource).ToList();
        }

        public static List<Book> ToModel(this IEnumerable<BookResource> resources)
        {
            return resources.Select(ToModel).ToList();
        }
    }
}
