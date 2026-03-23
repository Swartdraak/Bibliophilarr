> [!WARNING]
> **ARCHIVED** — This document has been superseded and moved to the archive.
>
> Canonical replacement: [MIGRATION_PLAN.md](../../../MIGRATION_PLAN.md)
> Reason: .NET 8 migration completed; historical record superseded by MIGRATION_PLAN.md.
> Deprecation date: 2026-03-23

# .NET 8 and Library Finalization - 2026-03-15

## What Changed

1. Completed a repository-wide .NET target audit and migration cleanup.
- Verified all backend projects in `src/` target `.NET 8` (`net8.0` or `net8.0-windows`).
- Updated `docs.sh` framework default from `net6.0` to `net8.0`.

2. Updated workspace publish task for deterministic .NET 8 publish behavior.
- Updated `.vscode/tasks.json` `publish` task to include:
  - `-f`
  - `net8.0`

3. Executed requested broad test pass.
- Ran full core test project:
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj -f net8.0 -v minimal`

4. Finalized active media libraries and isolated unresolved content.
- Re-verified organization convergence with `scripts/media_library_organize.py`:
  - `/media/audiobooks`: `0` proposed actions after quarantine relocation
  - `/media/ebooks`: `0` proposed actions
- Re-ran live enrichment with `scripts/live_provider_enrich_missing_metadata.py`:
  - `/media/audiobooks`: `0` targets in active library paths after relocation
  - `/media/ebooks`: `0` targets
- Moved unresolved audiobook folders into excluded quarantine subtree:
  - `/media/audiobooks/_dupes/unidentified`
- Created corresponding ebook quarantine subtree:
  - `/media/ebooks/_dupes/unidentified`

## Why It Changed

- The publish task had framework ambiguity for multi-target project graphs until `-f net8.0` was specified.
- A stale script default (`docs.sh`) still referenced `net6.0`.
- Final media operations required a clean active library while preserving unresolved assets for later manual/provider-assisted remediation.

## Validation Performed

### .NET Audit

Command:
- `rg -n "TargetFrameworks?|net[0-9]+\.|net4|netcoreapp|netstandard" src --glob '*.csproj' --glob '*.props' --glob '*.targets'`

Outcome:
- All project targets are on .NET 8 (`net8.0` / `net8.0-windows`).

### Publish Task Validation

Command:
- Workspace task `publish` now executes with `-f net8.0`.

Outcome:
- The previous NETSDK1129 framework-selection blocker is removed by task definition.

### Full Core Test Pass

Command:
- `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj -f net8.0 -v minimal`

Outcome:
- `Failed: 28, Passed: 2529, Skipped: 57, Total: 2614`
- Representative failed tests observed in output:
  - `finds_update_when_version_lower`
  - `no_update_when_version_higher`
  - `should_get_recent_updates`
- These failures are in pre-existing update/versioning test areas and were surfaced by the requested full-suite run.

### Media Finalization

Commands:
- `python3 scripts/media_library_organize.py --root /media/audiobooks --report-dir ...`
- `python3 scripts/media_library_organize.py --root /media/ebooks --report-dir ...`
- `python3 scripts/live_provider_enrich_missing_metadata.py --root /media/audiobooks --report-dir ...`
- `python3 scripts/live_provider_enrich_missing_metadata.py --root /media/ebooks --report-dir ...`

Final outcomes:
- Organizer dry-run:
  - Audiobooks: `Actions proposed: 0`
  - Ebooks: `Actions proposed: 0`
- Enrichment dry-run:
  - Audiobooks: `Targets: 0`, `Unresolved: 0` (in active paths)
  - Ebooks: `Targets: 0`, `Unresolved: 0`

## Quarantine Paths and Contents

### Audiobooks

Quarantine directory:
- `/media/audiobooks/_dupes/unidentified`

Moved folders:
- `Eric Vall__Summoner`
- `Eric Ugland__The Grim Guys`
- `Robert Blaise__1% Lifesteal`

Reason summary (from final unresolved enrichment report before relocation):
- All 3 classified as `low_confidence_or_mismatched_results`.
- Best candidate diagnostics:
  - `Summoner`: best match `Black Summoner / Volume 8` (confidence `0.7721`) below acceptance threshold and author mismatch.
  - `The Grim Guys`: best match `The Grim Grotto / Lemony Snicket` (confidence `0.5604`) with wrong author/title family.
  - `1% Lifesteal`: best match `Alice's Adventures in Wonderland / Lewis Carroll` (confidence `0.411`) unrelated content.

### Ebooks

Quarantine directory:
- `/media/ebooks/_dupes/unidentified`

Contents:
- Empty after final pass.

## Operational Impact

- Active audiobook and ebook libraries are now converged and clean for organization/import flows.
- Unresolved content is isolated under tool-excluded quarantine paths, preventing reprocessing churn in normal runs.
- Workspace publish task is now aligned with the repository’s .NET 8-only targeting.

## Rollback and Mitigation

1. Restore previous publish task behavior by removing `-f net8.0` from `.vscode/tasks.json` (not recommended).
2. Move quarantined folders from `_dupes/unidentified` back into active trees for manual triage or future provider retries.
3. If needed, rerun organizer/enrichment scripts using generated report artifacts to reconstruct before/after states.
