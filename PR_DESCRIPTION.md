# Docs: record issue #98 FluentMigrator 3.3.2 migration-chain posture, risks, and regression lane

**References #98** (closes #98 as a documentation/audit deliverable; related epic #96 — migration train)

## Summary

Audit and contract documentation for the FluentMigrator migration train (issue #98, epic #96). No application code, no dependency bump, no migration behavior change. This PR documents the current FluentMigrator **3.3.2** version posture in the Onyx project contract, enumerates four compatibility risks (R1-R4) for a future 4.x major upgrade, and pins the migration chain with a dedicated regression test lane.

### Current posture (verified in-container at the exact head)

- FluentMigrator.Runner / FluentMigrator.Runner.SQLite / FluentMigrator.Runner.Postgres all pinned to **3.3.2** via central CPM in `src/Directory.Packages.props`.
- 49 migration files in `src/NzbDrone.Core/Datastore/Migration/`: migrations 000-048 plus framework classes in `Framework/`.
- `MigrationController` DI wiring: `AddFluentMigratorCore()` + `ConfigureRunner()` with `AddPostgres()` + `AddNzbDroneSQLite()` + `ScanIn(assembly)` + `TypeFilterOptions` namespace filter on `NzbDrone.Core.Datastore.Migration`.
- `NzbDroneMigrationBase` extends `FluentMigrator.Migration` and routes per database type (Main/Log/Cache) via `MigrationContext.Current`.
- .NET 10 (net10.0) compatible: restore exit 0, full solution Release build 0 warn / 0 err.

### Compatibility risks for a future 4.x major upgrade (assigned to follow-up under epic #96)

- **R1**: FluentMigrator 3.3.2 is a 2021 release; a 4.x line would be a MAJOR upgrade requiring migration-attribute review, processor/generator accessor re-validation, and a full migration-chain re-run on both SQLite and Postgres.
- **R2**: `NzbDroneSQLiteProcessor` (custom processor wrapping the FluentMigrator SQLite processor) must be re-validated against the new processor interface.
- **R3**: Migration discovery is scoped by the `TypeFilterOptions.Namespace` filter on `NzbDrone.Core.Datastore.Migration`; a namespace rename would silently break discovery.
- **R4**: Postgres-specific migrations (023, 032) use dialect-specific raw SQL (timestamp-with-timezone semantics); any processor change must preserve the raw-SQL escape hatch.

## Changes

| File | Change |
|------|--------|
| `.onyx/project.yaml` | Contract header documenting the FluentMigrator 3.3.2 posture + risks R1-R4; new `issue-98-regression-fluentmigrator-migration-chain` test command in the contract's test section; baseline_evidence updated to this run. |
| `src/NzbDrone.Core.Test/Datastore/Migration/issue98FluentMigratorMigrationChainFixture.cs` | New regression fixture (5 tests): FluentMigrator 3.3.2 version pin, migration-namespace stability for discovery, unique/increasing migration versions, `Down()` throws `NotImplementedException` (no rollback support), and `NzbDroneSQLiteProcessor` existence. |

## Verification (at head on work branch `fix/issue-98-fluentmigrator-coordinated-upgrade`, base `develop` a30b83ac)

- `dotnet restore src/Bibliophilarr.sln -p:Platform=Posix` — exit 0.
- `dotnet build src/Bibliophilarr.sln --configuration Release -p:Platform=Posix` — 0 Warning(s), 0 Error(s).
- Common tests (`NzbDrone.Common.Test`) — **624 passed, 35 skipped, 0 failed**.
- Migration-test lane — **5/5 passed**: full 001-048 migration chain applied on SQLite 3.46.1 in ~1s (MigrationTest fixture + clamp_monitor_new_items fixture).
- Issue-98 fixture (`issue98FluentMigratorMigrationChainFixture`) — **5/5 passed**.
- Frontend (`yarn test:frontend`) — **68 passed, 0 failed** (17 suites).
- No application code, no dependency bump, no JS/TS runtime files under `src/frontend/lib/app` changed (no `production_coverage` binding required).

## Labels

- `type:docs`
- `area:documentation`
- `priority:low`
- `risk:low`

## Rollback

Revert the single contract commit on the work branch (or squash-revert the PR). No data migration, config change, or API contract change — safe to revert at any time.
