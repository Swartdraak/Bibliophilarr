using System.Collections.Generic;
using Bibliophilarr.Api.V1.Series;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;

namespace Bibliophilarr.Api.V1.Books
{
    [V1ApiController]
    public class CatalogueIdentityController : Controller
    {
        private readonly ICatalogueIdentityRepository _identity;
        private readonly IAuthorMetadataRepository _authors;
        private readonly IBookRepository _books;
        private readonly ISeriesRepository _series;

        public CatalogueIdentityController(ICatalogueIdentityRepository identity,
                                           IAuthorMetadataRepository authors,
                                           IBookRepository books,
                                           ISeriesRepository series)
        {
            _identity = identity;
            _authors = authors;
            _books = books;
            _series = series;
        }

        [HttpGet("{entity}/{identifier}")]
        public ActionResult<CatalogueIdentityResource> Resolve(string entity, string identifier)
        {
            var kind = entity switch
            {
                "author" => (CatalogueEntityKind?)CatalogueEntityKind.Author,
                "book" => CatalogueEntityKind.Work,
                "series" => CatalogueEntityKind.Series,
                _ => null
            };

            if (kind == null || string.IsNullOrWhiteSpace(identifier) || identifier.Length > 512)
            {
                return NotFound();
            }

            var ownerId = _identity.FindByRoute(kind.Value, identifier);
            if (ownerId == null)
            {
                return NotFound();
            }

            var id = ownerId.Value;
            var series = kind == CatalogueEntityKind.Series ? _series.Find(id) : null;
            var canonicalId = kind.Value switch
            {
                CatalogueEntityKind.Author => _authors.Find(id)?.CanonicalId,
                CatalogueEntityKind.Work => _books.Find(id)?.CanonicalId,
                _ => series?.CanonicalId
            };

            if (string.IsNullOrWhiteSpace(canonicalId))
            {
                return NotFound();
            }

            return new CatalogueIdentityResource
            {
                CanonicalId = canonicalId,
                Sources = _identity.GetSources(kind.Value, id),
                Series = series.ToResource()
            };
        }
    }

    public class CatalogueIdentityResource
    {
        public string CanonicalId { get; set; }
        public IReadOnlyList<CatalogueSourceAssociation> Sources { get; set; }
        public SeriesResource Series { get; set; }
    }
}
