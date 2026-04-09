using System;
using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Http.REST;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
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

            var selectedEdition = model.Editions?.Value.Where(x => x.Monitored).FirstOrDefault()
                                  ?? model.Editions?.Value.FirstOrDefault();

            var title = selectedEdition?.Title ?? model.Title;
            var authorTitle = $"{model.Author?.Value?.Metadata?.Value?.SortNameLastFirst} {title}";

            var seriesLinks = model.SeriesLinks?.Value?.OrderBy(x => x.SeriesPosition);
            var seriesTitle = seriesLinks?.Select(x => x?.Series?.Value?.Title + (x?.Position.IsNotNullOrWhiteSpace() ?? false ? $" #{x.Position}" : string.Empty)).ConcatToString("; ");

            var formatStatuses = new List<BookFormatStatusResource>();
            var editions = model.Editions?.Value;
            if (editions != null)
            {
                // Collect all book files across all editions and group by derived format type.
                // This ensures format is determined from actual file quality (e.g. EPUB → Ebook,
                // M4B → Audiobook) rather than relying solely on Edition.IsEbook, which may not
                // be set correctly by all metadata providers.
                var allFiles = editions
                    .Where(e => e.BookFiles?.Value != null)
                    .SelectMany(e => e.BookFiles.Value)
                    .ToList();

                var ebookFiles = allFiles.Where(f => Quality.GetFormatType(f.Quality.Quality) == FormatType.Ebook).ToList();
                var audiobookFiles = allFiles.Where(f => Quality.GetFormatType(f.Quality.Quality) == FormatType.Audiobook).ToList();

                // Also check edition-level classification for books without files
                var hasEbookEdition = editions.Any(e => e.IsEbook);
                var hasAudiobookEdition = editions.Any(e => !e.IsEbook);

                // Determine monitored status from editions
                var monitoredEbookEdition = editions.Where(e => e.IsEbook).FirstOrDefault(e => e.Monitored);
                var monitoredAudiobookEdition = editions.Where(e => !e.IsEbook).FirstOrDefault(e => e.Monitored);

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
                Images = selectedEdition?.Images ?? new List<MediaCover>(),
                Links = model.Links.Concat(selectedEdition?.Links ?? new List<Links>()).GroupBy(l => l.Url).Select(g => g.First()).ToList(),
                Ratings = selectedEdition?.Ratings ?? new Ratings(),
                Added = model.Added,
                LastSearchTime = model.LastSearchTime,
                Editions = editions?.Select(e => e.ToResource()).ToList() ?? new List<EditionResource>(),
                FormatStatuses = formatStatuses
            };
        }

        public static Book ToModel(this BookResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            var author = resource.Author?.ToModel() ?? new NzbDrone.Core.Books.Author();

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
                AddOptions = resource.AddOptions,
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
