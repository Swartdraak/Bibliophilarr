using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Http;
using Bibliophilarr.Http.ErrorManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;

namespace Bibliophilarr.Api.V1.Search
{
    [V1ApiController]
    public class SearchController : Controller
    {
        private const string HardcoverSeriesPrefix = "hardcover:series:";
        private static readonly Logger Logger = LogManager.GetLogger("Search");

        private readonly IMetadataProviderOrchestrator _searchProxy;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IMapCoversToLocal _coverMapper;
        private readonly ISearchTelemetryService _searchTelemetry;
        private readonly ICatalogueIdentityRepository _catalogueIdentity;
        private readonly ISeriesRepository _seriesRepository;
        private readonly IAuthorRepository _authorRepository;
        private readonly IBookRepository _bookRepository;

        public SearchController(IMetadataProviderOrchestrator searchProxy,
                                IBuildFileNames fileNameBuilder,
                                IMapCoversToLocal coverMapper,
                                ISearchTelemetryService searchTelemetry,
                                ICatalogueIdentityRepository catalogueIdentity,
                                ISeriesRepository seriesRepository,
                                IAuthorRepository authorRepository,
                                IBookRepository bookRepository)
        {
            _searchProxy = searchProxy;
            _fileNameBuilder = fileNameBuilder;
            _coverMapper = coverMapper;
            _searchTelemetry = searchTelemetry;
            _catalogueIdentity = catalogueIdentity;
            _seriesRepository = seriesRepository;
            _authorRepository = authorRepository;
            _bookRepository = bookRepository;
        }

        [HttpGet]
        public object Search([FromQuery] string term, [FromQuery] string scope = null)
        {
            if (scope is not null and not "" and not "all" and not "author" and not "book" and not "series" and not "series-books")
            {
                return BadRequest(new ErrorModel { Message = "Choose a valid metadata search scope." });
            }

            term = term?.Trim();
            if (scope == "series-books" && !IsHardcoverSeriesId(term))
            {
                return BadRequest(new ErrorModel { Message = "Choose a valid Hardcover series." });
            }

            if (string.IsNullOrEmpty(term) || term.Length < 3)
            {
                return new List<SearchResource>();
            }

            var searchResults = scope switch
            {
                "author" => _searchProxy.SearchForInteractiveAuthors(term, HttpContext?.RequestAborted ?? CancellationToken.None)?.Cast<object>(),
                "book" => _searchProxy.SearchForNewBook(term, null, false)?.Cast<object>(),
                "series" => _searchProxy.SearchForNewSeries(term)?.Cast<object>(),
                "series-books" => _searchProxy.GetSeriesBooks(term)?.Cast<object>(),
                _ => _searchProxy.SearchForNewEntity(term)
            };

            if ((scope is "series" or "series-books") && searchResults == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorModel
                {
                    Message = scope == "series"
                        ? "Series search is unavailable. Check the Hardcover settings or try again later."
                        : "The books in this series could not be loaded. Check the Hardcover settings or try again later."
                });
            }

            return MapToResource(searchResults, term).ToList();
        }

        private static bool IsHardcoverSeriesId(string term)
        {
            return term?.StartsWith(HardcoverSeriesPrefix, StringComparison.Ordinal) == true &&
                   int.TryParse(term.AsSpan(HardcoverSeriesPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var seriesId) &&
                   seriesId > 0;
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

            var groups = results.Select((result, index) =>
                {
                    var identity = GetSearchIdentity(result, out var hasVerifiedSource);
                    return (Result: result, Identity: identity, Index: index, HasVerifiedSource: hasVerifiedSource);
                })
                .GroupBy(hit => (hit.Identity, UngroupedIndex: hit.Identity.HasValue ? -1 : hit.Index));

            var id = 1;
            foreach (var group in groups)
            {
                var representative = group.First();
                var result = representative.Result;
                var resource = new SearchResource();
                resource.Id = id++;
                resource.CatalogueOwnerId = group.Key.Identity?.OwnerId;

                if (result is NzbDrone.Core.Books.Author author)
                {
                    resource.Author = author.ToResource();
                    resource.ForeignId = author.ForeignAuthorId;

                    var poster = SelectSearchArtwork(
                        resource.Author.Images,
                        MediaCoverTypes.Poster,
                        group.Skip(1).Select(hit => hit.Result).OfType<NzbDrone.Core.Books.Author>()
                            .SelectMany(alternate => alternate.Metadata?.Value?.Images ?? Enumerable.Empty<MediaCover>()));
                    _coverMapper.ConvertToLocalUrls(resource.Author.Id, MediaCoverEntity.Author, resource.Author.Images);

                    if (poster == null && representative.HasVerifiedSource && resource.CatalogueOwnerId is int authorMetadataId)
                    {
                        var storedAuthor = _authorRepository.GetAuthorByMetadataId(authorMetadataId);
                        if (storedAuthor is { Id: > 0 })
                        {
                            poster = AppendStoredArtwork(
                                resource.Author.Images,
                                storedAuthor.Id,
                                MediaCoverEntity.Author,
                                MediaCoverTypes.Poster,
                                storedAuthor.Metadata?.Value?.Images);
                        }
                    }

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

                    var cover = SelectSearchArtwork(
                        resource.Book.Images,
                        MediaCoverTypes.Cover,
                        group.Skip(1).Select(hit => hit.Result).OfType<NzbDrone.Core.Books.Book>()
                            .SelectMany(alternate => BookEditionSelector.GetBookImages(alternate.Editions?.Value)));
                    _coverMapper.ConvertToLocalUrls(resource.Book.Id, MediaCoverEntity.Book, resource.Book.Images);

                    if (cover == null && representative.HasVerifiedSource && resource.CatalogueOwnerId is int bookId)
                    {
                        var storedBook = _bookRepository.Find(bookId);
                        if (storedBook != null)
                        {
                            cover = AppendStoredArtwork(
                                resource.Book.Images,
                                bookId,
                                MediaCoverEntity.Book,
                                MediaCoverTypes.Cover,
                                BookEditionSelector.GetBookImages(storedBook.Editions?.Value));
                        }
                    }

                    if (cover != null)
                    {
                        resource.Book.RemoteCover = cover.RemoteUrl;
                    }

                    if (bookAuthor != null)
                    {
                        var authorIdentity = GetSearchIdentity(bookAuthor, out var hasVerifiedAuthorSource);
                        resource.Book.Author.AuthorMetadataId = hasVerifiedAuthorSource ? authorIdentity?.OwnerId ?? 0 : 0;
                        resource.Book.Author.Folder = _fileNameBuilder.GetAuthorFolder(bookAuthor);
                    }
                }
                else if (result is SeriesSearchSummary series)
                {
                    resource.Series = series;
                    resource.ForeignId = series.ForeignSeriesId;
                    if (resource.CatalogueOwnerId is int seriesId)
                    {
                        resource.SeriesCovers = GetSeriesCovers(seriesId);
                    }
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

        private static MediaCover SelectSearchArtwork(List<MediaCover> images, MediaCoverTypes coverType, IEnumerable<MediaCover> alternateImages)
        {
            var existing = images.FirstOrDefault(image => image?.CoverType == coverType && !string.IsNullOrWhiteSpace(image.Url));
            if (existing != null)
            {
                return existing;
            }

            var fallback = FindRemoteArtwork(alternateImages, coverType);

            if (fallback == null)
            {
                return null;
            }

            var cover = fallback.JsonClone();
            images.Add(cover);
            return cover;
        }

        private MediaCover AppendStoredArtwork(List<MediaCover> images,
                                               int ownerId,
                                               MediaCoverEntity entity,
                                               MediaCoverTypes coverType,
                                               IEnumerable<MediaCover> storedImages)
        {
            var cover = FindRemoteArtwork(storedImages, coverType)?.JsonClone();
            if (cover == null)
            {
                return null;
            }

            _coverMapper.ConvertToLocalUrls(ownerId, entity, new[] { cover });
            images.Add(cover);
            return cover;
        }

        private static MediaCover FindRemoteArtwork(IEnumerable<MediaCover> images, MediaCoverTypes coverType)
        {
            return images?.FirstOrDefault(image => image?.CoverType == coverType &&
                Uri.TryCreate(image.Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https");
        }

        private List<SeriesSearchCoverResource> GetSeriesCovers(int seriesId)
        {
            var covers = new List<SeriesSearchCoverResource>();
            var urls = new HashSet<string>(StringComparer.Ordinal);
            var links = _seriesRepository.Find(seriesId)?.LinkItems?.Value ?? new List<SeriesBookLink>();

            foreach (var link in links.Where(link => link.SeriesId == seriesId && link.BookId > 0)
                         .OrderByDescending(link => link.IsPrimary)
                         .ThenBy(link => link.SeriesPosition > 0 ? link.SeriesPosition : int.MaxValue)
                         .ThenBy(link => link.BookId)
                         .Take(6))
            {
                var book = link.Book?.Value;
                if (book?.Id != link.BookId || string.IsNullOrWhiteSpace(book.Title))
                {
                    continue;
                }

                var cover = BookEditionSelector.GetBookImages(book.Editions?.Value)
                    .FirstOrDefault(image => image.CoverType == MediaCoverTypes.Cover &&
                        Uri.TryCreate(image.Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https");
                if (cover == null)
                {
                    continue;
                }

                cover = cover.JsonClone();
                _coverMapper.ConvertToLocalUrls(book.Id, MediaCoverEntity.Book, new[] { cover });
                if (!string.IsNullOrWhiteSpace(cover.Url) && urls.Add(cover.Url))
                {
                    covers.Add(new SeriesSearchCoverResource { BookTitle = book.Title, Url = cover.Url });
                    if (covers.Count == 2)
                    {
                        break;
                    }
                }
            }

            return covers;
        }

        private (CatalogueEntityKind Kind, int? OwnerId, string SourceIdentity)? GetSearchIdentity(
            object result, out bool hasVerifiedSource)
        {
            var (kind, canonicalId, providerId) = result switch
            {
                NzbDrone.Core.Books.Author author => (CatalogueEntityKind.Author, author.Metadata?.Value?.CanonicalId, author.Metadata?.Value?.ForeignAuthorId),
                NzbDrone.Core.Books.Book book => (CatalogueEntityKind.Work, book.CanonicalId, book.ForeignBookId),
                SeriesSearchSummary series => (CatalogueEntityKind.Series, null, series.ForeignSeriesId),
                _ => default((CatalogueEntityKind, string, string))
            };

            var canonical = Guid.TryParseExact(canonicalId, "N", out var canonicalGuid) ? canonicalGuid.ToString("N") : null;
            var source = CatalogueIdentityRepository.NormalizeProviderSearchId(kind, providerId);
            var canonicalOwner = canonical == null ? null : _catalogueIdentity.FindByRoute(kind, canonical);
            var sourceOwner = source == null ? null : _catalogueIdentity.FindByProviderId(kind, source.Provider, source.ProviderId);

            if (canonicalOwner.HasValue && sourceOwner.HasValue && canonicalOwner != sourceOwner)
            {
                hasVerifiedSource = false;
                return null;
            }

            if (kind == CatalogueEntityKind.Author && sourceOwner is int metadataId &&
                _catalogueIdentity.GetSources(kind, metadataId)?.Any(candidate =>
                    candidate.Provider == source.Provider && candidate.ProviderId == source.ProviderId &&
                    CatalogueIdentityRepository.IsVerifiedAuthorSearchEvidence(candidate.Evidence)) != true)
            {
                sourceOwner = null;
            }

            hasVerifiedSource = sourceOwner.HasValue;
            if ((canonicalOwner ?? sourceOwner) is { } owner)
            {
                return (kind, owner, null);
            }

            return source == null ? null : (kind, null, $"{source.Provider}:{source.ProviderId}");
        }
    }
}
