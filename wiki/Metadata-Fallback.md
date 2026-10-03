# Metadata Source Fallback and Provider-Failure Semantics

Bibliophilarr resolves book and author metadata through a **provider fallback chain**.
This page documents how the chain works, how provider failures are classified, and the
data-loss risk that arises when a transient network error is indistinguishable from a
genuine not-found.

## Provider chain

The primary metadata providers implement `IMetadataProvider` and are tried in priority
order (lower number = higher priority):

| Priority | Provider | Scope | Enabled by default |
|---|---|---|---|
| 1 | **Hardcover** | `hardcover:work:*`, `hardcover:author:*` | Yes (if token configured) |
| 2 | **OpenLibrary** | `openlibrary:work:*`, `openlibrary:author:*`, bare OLIDs | **No** (`EnableOpenLibraryProvider` defaults to `false`) |
| 3 | **Google Books** | ISBN / title-based | No (`EnableGoogleBooksProvider`) |

**Inventaire** is a separate **search fallback** (`IBookSearchFallbackProvider`, not part
of the priority-ordered `IMetadataProvider` chain). It is used for title/author search
when enabled (`EnableInventaireFallback`) and is not consulted for `get-book-info` /
`get-author-info` by scoped id.

The orchestrator (`MetadataProviderOrchestrator`) routes a request to the provider that
matches the **id scope** of the foreign id:

- `hardcover:work:123` → routed **exclusively** to the Hardcover provider.
- `openlibrary:work:OL123W` → routed **exclusively** to the OpenLibrary provider.
- A bare OLID (`OL123W`) → OpenLibrary.

This id-scope routing means a book added via Hardcover is **never** re-resolved through
OpenLibrary on refresh — the provider is pinned by the id prefix.

## Failure classification (current behavior)

`MetadataProviderOrchestrator.ExecuteFirst` catches **all** exceptions from a provider
and returns `null`:

```csharp
catch (Exception ex)
{
    _telemetry.Record(...);
    _logger.Warn(ex, "Metadata provider '{0}' failed during {1}", ...);
    lastError = ex;
}
...
return null;   // network error AND genuine not-found both become null
```

**Consequence:** a transient network error (`HttpRequestException`, `SocketException`)
is indistinguishable from a genuine 404 ("provider removed this id"). Both produce a
`null` result, which `GetAuthorInfo`/`GetBookInfo` convert to
`AuthorNotFoundException`/`BookNotFoundException`.

## Data-loss risk (issue #204 / #209)

When `get-author-info` returns `null` (for **any** reason, including a network blip),
`RefreshAuthorService` treats the author as "removed from metadata" and **deletes the
author** if it has no files:

```
Warn|MetadataProviderOrchestrator|Metadata provider 'Hardcover' failed during get-author-info
     System.Net.Http.HttpRequestException: No route to host (api.hardcover.app:443)
Warn|RefreshAuthorService|Author [hardcover:author:171873][Frank Herbert] not found in metadata and is being deleted
```

A brief connectivity outage during an author refresh can therefore delete authors.

> **Fix in progress (issue #209, PR #211):** the orchestrator will propagate a distinct
> `MetadataProviderUnavailableException` for transport errors, and
> `RefreshAuthorService` will **not** delete an author when the failure is a
> transport/unavailable error. Only an explicit provider 404/not-found will trigger
> deletion (and only when the author has no files).

## Operator guidance

Until the #209 fix is merged:

1. **Do not run author refreshes during a known network outage.** A transient
   `No route to host` / `Connection reset` can delete authors.
2. **Check provider health first:** `GET /api/v1/metadata/providers/health/basic`.
3. **If authors are deleted unexpectedly**, check the logs for
   `Metadata provider 'X' failed during get-author-info` immediately before the
   deletion. If the failure was a transport error (not a 404), the deletion was a
   false positive.
4. **Restore deleted authors** by re-adding them (the provider still has the data).

## References

- [Metadata Provider Outage and Fallback Runbook](../docs/operations/METADATA_PROVIDER_RUNBOOK.md)
- [Troubleshooting Runbook](../docs/operations/TROUBLESHOOTING_RUNBOOK.md)
- [Architecture Overview](Architecture.md)
