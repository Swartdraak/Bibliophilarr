using System;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    internal static class InventaireLibraryWorkSummary
    {
        private const string Prefix = "inventaire:work-summary:";

        public static bool IsTransient(Edition edition) => edition?.ForeignEditionId?.StartsWith(Prefix, StringComparison.Ordinal) == true;

        public static string GetSourceId(Edition edition) => edition.ForeignEditionId[Prefix.Length..];

        public static Edition Create(string sourceId, Book owner)
        {
            return new Edition
            {
                ForeignEditionId = Prefix + sourceId,
                Title = owner.Title,
                Book = owner,
                BookId = owner.Id
            };
        }

        public static Book FindOwner(IBookService books, ICatalogueIdentityRepository identities, string sourceId, out bool claimed)
        {
            const CatalogueEntityKind kind = CatalogueEntityKind.Work;
            const string provider = "Inventaire";
            var normalized = CatalogueIdentityRepository.NormalizeSource(kind, provider, sourceId, null);
            var id = normalized != null && normalized.ProviderId == sourceId ? identities.FindByProviderId(kind, provider, sourceId) : null;
            claimed = id.HasValue;
            if (!claimed)
            {
                return null;
            }

            var work = books.FindById($"inventaire:work:{sourceId}");
            var author = work?.Author?.Value;
            if (work is not { Id: > 0, AuthorMetadataId: > 0 } || work.Id != id ||
                !Guid.TryParseExact(work.CanonicalId, "N", out var workCanonical) || workCanonical == Guid.Empty ||
                author is not { Id: > 0, AuthorMetadataId: > 0 } || author.AuthorMetadataId != work.AuthorMetadataId ||
                author.Metadata?.Value?.Id != author.AuthorMetadataId ||
                !Guid.TryParseExact(author.Metadata.Value.CanonicalId, "N", out var authorCanonical) || authorCanonical == Guid.Empty)
            {
                return null;
            }

            var alias = identities.GetSources(kind, work.Id)?.ExclusiveOrDefault(source =>
                source.Provider == provider && source.ProviderId == sourceId);
            return alias?.Evidence is CatalogueIdentityRepository.PrimaryProviderEvidence or CatalogueIdentityRepository.LegacyProviderEvidence
                ? work
                : null;
        }
    }
}
