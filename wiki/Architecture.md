# Architecture Overview

## High-level Stack

- **Backend:** .NET 8 / C# — REST API via ASP.NET Core, SignalR for real-time push.
- **Frontend:** React 17 + TypeScript/JavaScript — Redux state management, Webpack bundling.
- **Database:** SQLite (default) or PostgreSQL — EF-style migrations via FluentMigrator.
- **Domain focus:** Book/audiobook metadata ingestion, search, library management, and download automation.

## Solution Structure

> **Note:** The `NzbDrone.*` project naming is a historical artifact from the
> Sonarr/Radarr/Readarr codebase lineage and does not reflect current product
> identity. The Bibliophilarr fork preserves these names for upstream
> compatibility while new API assemblies use `Bibliophilarr.*` naming.

| Project | Purpose |
|---|---|
| `NzbDrone.Host` | Application entry point, Kestrel server, DI bootstrap |
| `NzbDrone.Core` | Domain services, metadata providers, commands, indexers |
| `NzbDrone.Common` | Shared utilities, HTTP client, disk operations, environment info |
| `Bibliophilarr.Api.V1` | REST API controllers and resource models |
| `Bibliophilarr.Http` | HTTP pipeline, middleware, authentication |
| `NzbDrone.SignalR` | Real-time event broadcasting |
| `NzbDrone.Update` | Self-update mechanism |
| `NzbDrone.Mono` / `NzbDrone.Windows` | Platform-specific implementations |

## Metadata Architecture (Current State)

- **Provider abstraction layer** — `IMetadataProvider` interface with multiple implementations.
- **Fallback chain:** Hardcover (primary) → OpenLibrary → Inventaire → Google Books.
- **`BookSearchFallbackExecutionService`** — orchestrates provider failover with rate-limit dampening.
- **`MetadataAggregator`** — merges results across providers with quality scoring.
- **Identifier mapping** — ISBN, OLID, Hardcover ID, ASIN normalized through `IdentifierService`.

## Key Technical Concerns

- External API rate limits and transient failures — handled via provider-level timeouts and retry.
- Variable metadata quality — quality scoring ranks provider results.
- Identifier normalization — multiple ID systems mapped through a unified interface.
- Schema migration safety — FluentMigrator-based migrations with rollback support.

## References

- [MIGRATION_PLAN.md](../MIGRATION_PLAN.md) — Provider migration strategy and work packages.
- [ROADMAP.md](../ROADMAP.md) — Phased delivery milestones.
- [PROVIDER_IMPLEMENTATION_GUIDE.md](../docs/operations/PROVIDER_IMPLEMENTATION_GUIDE.md) — Provider development reference.
