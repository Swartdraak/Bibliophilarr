# Deferred migration recovery ledger

This ledger tracks the historical dependency backlog that was closed or deferred without durable successor engineering work.

## Objective

Keep the migration backlog visible, actionable, and tied to concrete implementation work instead of treating a quiet issue list as evidence that engineering is complete.

## Recovery principle

Closing a PR does not complete the underlying engineering requirement. A deferred migration remains active until a successor issue, implementation branch, or validated replacement is in place.

## Current upstream evidence

- NuGet shows the current stable Ical.Net release series is `5.2.x` and the latest stable package is `5.2.3`.
- The active repository migration branch is `migration/ical-net-5`.
- The migration target is now `Ical.Net 5.2.3` unless stronger compatibility evidence indicates a narrower, repo-specific pin.

## Historical backlog mapping

| Historical PR | Package / area | Historical version | Current live version / repo state | Historical target | Current researched target | Status | Successor issue | Migration train | Dependencies | Priority |
|---|---|---:|---|---|---|---|---|---|---|---|
| #70 | SignalR.Client / .NET 10 | 8.0.11 | 8.0.11 in [src/Directory.Packages.props](../../src/Directory.Packages.props) | .NET 10 / ASP.NET alignment | .NET 10 compatibility spike | Active | open .NET 10 epic + child issues | .NET 10 | TFM, Azure/App host, SignalR, CI | P0 |
| #69 | coverlet.collector | 8.0.1 | 8.0.1 in [src/Directory.Packages.props](../../src/Directory.Packages.props) | test coverage tooling review | current toolchain is acceptable unless .NET 10 requires a new pin | Deferred, currently low-risk | open test-toolchain issue | test/toolchain | .NET 10, coverage reporting | P2 |
| #68 | label-actions | workflow action | repo workflow state still active | workflow modernization | maintain current GitHub workflow semantics | Active review | workflow modernization issue | workflow modernization | GitHub Actions rules | P2 |
| #67 | cosign-installer | workflow action | repo workflow state still active | signing workflow modernization | keep current action or replace when needed | Pending | workflow modernization issue | workflow modernization | workflow permissions | P2 |
| #65 | upload-artifact | workflow action | repo workflow state still active | artifact action update | current version review | Pending | workflow modernization issue | workflow modernization | artifact naming and retention | P2 |
| #64 | markdownlint | workflow action | repo workflow state still active | markdown lint modernization | current repo configuration review | Pending | workflow modernization issue | workflow modernization | docs linting | P2 |
| #59 | Ical.Net | 4.3.1 | 5.2.3 target in [src/Directory.Packages.props](../../src/Directory.Packages.props) | 5.2.x stable | 5.2.3 stable | Active implementation | open Ical.Net migration issue | Ical.Net migration | calendar serialization, DateTimeKind, DST, recurrence | P1 |
| #57 | FluentMigrator.Runner | 3.3.2 | 3.3.2 in [src/Directory.Packages.props](../../src/Directory.Packages.props) | migration runtime upgrade | coordinate after .NET 10 / provider review | Deferred | FluentMigrator migration issue | FluentMigrator train | DB migrations, providers | P2 |
| #56 | Prettier | frontend toolchain | frontend package state pending review | prettier modernization | current frontend package audit | Deferred | frontend tooling issue | frontend modernization | frontend build pipeline | P3 |
| #55 | rimraf | frontend toolchain | frontend package state pending review | tooling modernization | current frontend package audit | Deferred | frontend tooling issue | frontend modernization | package scripts | P3 |
| #54 | webpack-cli | frontend toolchain | frontend package state pending review | tooling modernization | current frontend package audit | Deferred | frontend tooling issue | frontend modernization | build tooling | P3 |
| #53 | postcss-mixins | frontend toolchain | frontend package state pending review | tooling modernization | current frontend package audit | Deferred | frontend tooling issue | frontend modernization | CSS pipeline | P3 |
| #52 | Font Awesome | frontend package | frontend package state pending review | package modernization | current frontend package audit | Deferred | frontend tooling issue | frontend modernization | UI icon stack | P3 |

## Active migration work

### Ical.Net migration

- Status: active on branch `migration/ical-net-5`
- Current target: Ical.Net `5.2.3`
- Primary files: [src/Directory.Packages.props](../../src/Directory.Packages.props), [src/Bibliophilarr.Api.V1/Calendar/CalendarFeedController.cs](../../src/Bibliophilarr.Api.V1/Calendar/CalendarFeedController.cs), [src/NzbDrone.Api.Test/Books/BookControllerEventGuardFixture.cs](../../src/NzbDrone.Api.Test/Books/BookControllerEventGuardFixture.cs)
- Risk: low-to-medium; directly affects ICS calendar feed generation and all-day date semantics

### .NET 10 compatibility spike

- Status: active branch to be created from `develop`
- Goal: inventory the TFM/runtime/toolchain surface that currently prevents a clean .NET 10 migration
- Scope: SDK pin, package compatibility, Docker/runtime, CI and packaging compatibility

## Current repo evidence

- Repo branch: `migration/ical-net-5`
- Base SHA: `ad926d6d6af034992890d4725f5cebc1270fd9a4`
- Current Ical.Net repo state: updated to `5.2.3` in the central package management file.
- Recovery standard: the migration queue must remain visible until successor engineering work and validation are complete.

## Exit criteria

- All 13 historical deferred PRs are mapped to either active successor work or a documented final state.
- At least one bounded migration is implemented and validated.
- The .NET 10 migration path has an active compatibility inventory and spike branch.
- No migration remains in a silent "done" state without durable successor evidence.
