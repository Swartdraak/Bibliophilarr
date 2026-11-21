using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Http;
using Bibliophilarr.Http.ErrorManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;

namespace Bibliophilarr.Api.V1.Search
{
    [V1ApiController]
    public class SearchController : Controller
    {
        private static readonly Logger Logger = LogManager.GetLogger("Search");

        private readonly IMetadataProviderOrchestrator _searchProxy;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IMapCoversToLocal _coverMapper;
        private readonly ISearchTelemetryService _searchTelemetry;

        public SearchController(IMetadataProviderOrchestrator searchProxy, IBuildFileNames fileNameBuilder, IMapCoversToLocal coverMapper, ISearchTelemetryService searchTelemetry)
        {
            _searchProxy = searchProxy;
            _fileNameBuilder = fileNameBuilder;
            _coverMapper = coverMapper;
            _searchTelemetry = searchTelemetry;
        }

        [HttpGet]
        public object Search([FromQuery] string term, [FromQuery] string scope = null)
        {
            if (scope is not null and not "" and not "all" and not "author" and not "book" and not "series")
            {
                return BadRequest(new ErrorModel { Message = "Choose a valid metadata search scope." });
            }

            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 3)
            {
                return new List<SearchResource>();
            }

            term = term.Trim();
            var searchResults = scope switch
            {
                "author" => _searchProxy.SearchForNewAuthor(term)?.Cast<object>(),
                "book" => _searchProxy.SearchForNewBook(term, null, false)?.Cast<object>(),
                "series" => _searchProxy.SearchForNewSeries(term)?.Cast<object>(),
                _ => _searchProxy.SearchForNewEntity(term)
            };

            if (scope == "series" && searchResults == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorModel
                {
                    Message = "Series search is unavailable. Check the Hardcover settings or try again later."
                });
            }

            return MapToResource(searchResults, term).ToList();
        }

        private IEnumerable<SearchResource> MapToResource(IEnumerable<object> results, string term)
        {
            // Issue #250: MetadataProviderOrchestrator.ExecuteFirst returns null when no
            // provider returns a result (every provider failed or returned null, or no
            // provider supported the search). The original code enumerated `results`
            // directly, so a null return from _searchProxy.SearchForNewEntity threw a
            // NullReferenceException at the foreach — the live 500 on
            // /api/v1/search?term=<any term with provider fan-out>. Guard the null so a
            // no-result search degrades to an empty list instead of a 500.
            if (results == null)
            {
                yield break;
            }

            var id = 1;
            foreach (var result in results)
            {
                var resource = new SearchResource();
                resource.Id = id++;

                if (result is NzbDrone.Core.Books.Author author)
                {
                    resource.Author = author.ToResource();
                    resource.ForeignId = author.ForeignAuthorId;

                    _coverMapper.ConvertToLocalUrls(resource.Author.Id, MediaCoverEntity.Author, resource.Author.Images);

                    var poster = resource.Author.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);

                    if (poster != null)
                    {
                        resource.Author.RemotePoster = poster.RemoteUrl;
                    }

                    resource.Author.Folder = _fileNameBuilder.GetAuthorFolder(author);
                }
                else if (result is NzbDrone.Core.Books.Book book)
                {
                    resource.Book = book.ToResource();

                    // Issue #248: book.Author is a LazyLoaded<Author> whose Value can be
                    // null (provider did not resolve the author).  The ?.Value?.ToResource()
                    // chain already null-guards this, but capture the Author explicitly so
                    // the folder mapping below does not dereference a null LazyLoaded.
                    var bookAuthor = book.Author?.Value;
                    resource.Book.Author = bookAuthor?.ToResource();
                    resource.ForeignId = book.ForeignBookId;

                    _coverMapper.ConvertToLocalUrls(resource.Book.Id, MediaCoverEntity.Book, resource.Book.Images);

                    var cover = resource.Book.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Cover);

                    if (cover != null)
                    {
                        resource.Book.RemoteCover = cover.RemoteUrl;
                    }

                    if (bookAuthor != null)
                    {
                        resource.Book.Author.Folder = _fileNameBuilder.GetAuthorFolder(bookAuthor);
                    }
                }
                else if (result is SeriesSearchSummary series)
                {
                    resource.Series = series;
                    resource.ForeignId = series.ForeignSeriesId;
                }
                else
                {
                    _searchTelemetry.RecordUnsupportedEntityType(term, result?.GetType());
                    Logger.Warn("Ignoring unsupported search entity type [{0}] for search term [{1}]", result?.GetType().FullName ?? "<null>", LogSanitizer.Sanitize(term));
                    continue;
                }

                yield return resource;
            }
        }
    }
}
