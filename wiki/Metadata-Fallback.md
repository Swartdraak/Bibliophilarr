# Metadata Source Fallback and Provider-Failure Semantics

Bibliophilarr resolves book and author metadata through primary providers and
uses separate search fallbacks for automatic library import. This page describes
provider routing and refresh safeguards on the development branch.

## Provider chain

The primary metadata providers implement `IMetadataProvider` and are tried in priority
order (lower number = higher priority):

| Priority | Provider | Scope | Enabled by default |
|---|---|---|---|
| 1 | **Hardcover** | `hardcover:work:*`, `hardcover:author:*` | Yes (if token configured) |
| 2 | **OpenLibrary** | `openlibrary:work:*`, `openlibrary:author:*`, bare OLIDs | **No** (`EnableOpenLibraryProvider` defaults to `false`) |
| 3 | **Google Books** | ISBN / title-based | No (`EnableGoogleBooksProvider`) |

**Inventaire** provides a separate title and author search fallback for automatic
library import when `EnableInventaireFallback` is enabled. It does not participate
in primary provider ordering or scoped book and author lookups.

The orchestrator selects the provider that matches the ID scope when the
configured provider supports the requested lookup.

- `hardcover:work:123` → Hardcover.
- `openlibrary:work:OL123W` → Open Library.
- A bare OLID (`OL123W`) → OpenLibrary.

If the matching provider is unavailable in the registry, the application logs
the configuration and may try other enabled providers. After a successful book
lookup, Bibliophilarr can enrich editions from other providers using
corroborating identifiers.

## Failure classification (current behavior)

A scoped lookup distinguishes provider unavailability from missing metadata.
An author lookup that fails reports provider unavailability. A provider can also
report confirmed missing, or return no matching metadata without confirming
removal. A book lookup that encounters a transport outage reports unavailability.

When the series provider is unavailable, Add New can open an exactly matched
saved series with existing members. The result is labelled as saved-series
coverage. An unavailable search with no saved match still reports the outage.
A successful provider search with no matches returns an empty result.

Hardcover series preview checks the reported count of active memberships and
loads subsequent pages until it reaches that count. If a later page fails or
the reported membership changes during the lookup, the preview reports failure
and leaves stored series and files unchanged.

Open Library artwork retries give earlier opportunities to books with tracked
files and reserve opportunities for bibliography-only books and author
portraits. A verified redirect for the same artwork uses one additional request
within the existing limit. On supported Linux systems, a confirmed import or
individual book refresh can recover a declared M4B, EPUB, or supported AZW3 cover
when provider artwork is missing.

## Data-loss risk (issue #204 / #209)

Author refresh keeps the local author, bibliography, and existing metadata
when the provider is unavailable or has no matching author. Confirmed missing
also keeps the author, including when the author has no files.

Book refresh suppresses automatic cleanup during a degraded provider window.
Under healthy conditions, the separate cleanup path for existing provider-backed
books requires a repeated miss and only considers books that were added
automatically and have no files.

## Operator guidance

1. Check provider status and recent operation results in **System → Status** or
   through `GET /api/v1/metadata/providers/health`.
2. During a provider cooldown, automatic import can continue with healthy,
   configured alternatives. Check **Unmapped Files** for the reasons that
   individual files are still held.
3. A refresh for a provider-owned author or work may still need that provider's
   exact record. Use the reported retry time for affected refreshes. Existing
   catalogue and file associations remain available during the wait.
4. If a reachable provider reports missing metadata, verify the exact record
   before changing the catalogue mapping.

## References

1. [Metadata Provider Outage and Fallback Runbook](../docs/operations/METADATA_PROVIDER_RUNBOOK.md)
2. [Troubleshooting Runbook](../docs/operations/TROUBLESHOOTING_RUNBOOK.md)
3. [Architecture Overview](Architecture.md)
4. [Author refresh outage fixture](../src/NzbDrone.Core.Test/MetadataSource/RefreshAuthorProviderOutageFixture.cs)
5. [Book refresh deletion guard fixture](../src/NzbDrone.Core.Test/MusicTests/RefreshBookDeletionGuardFixture.cs)
