# Bibliophilarr Roadmap

**Last Updated**: March 17, 2026

This roadmap reflects the repository's actual delivery posture. Bibliophilarr is no longer in a planning-only state. The project is operating in Phase 5 consolidation with Phase 6 hardening active, while provider migration work continues incrementally on the active delivery lanes.

## Current Delivery Posture

- Current phase: Phase 5 consolidation with Phase 6 hardening active.
- Active delivery lanes: `develop` and `staging`.
- Default branch posture: `main` is the operator-facing readiness and release-entry branch, not the primary packaging-validation lane.
- Packaging scope: binary, Docker, and npm packaging validation remain intentionally scoped to `develop` and `staging` until installation paths are fully proven end to end for release tagging from `main`.
- Actions token posture: branch-policy and readiness workflows remain report-only when GitHub Actions integration tokens cannot read admin or Dependabot APIs.

## Delivery Lanes

| Branch | Purpose | Required outcomes |
|---|---|---|
| `develop` | Active integration lane for metadata migration and operational hardening slices | `build-test`, `Markdown lint`, `triage`, smoke telemetry, packaging validation |
| `staging` | Pre-release validation lane mirroring `develop` with tighter release-readiness scrutiny | same required contexts as `develop`, plus green packaging validation |
| `main` | Default branch for operator runbooks, readiness reporting, branch-policy auditing, and release-entry confirmation | `build-test`, `Markdown lint`, `triage`, smoke telemetry, successful readiness and branch-policy report runs |

## Phase Summary

### Phase 1 to Phase 4 foundation

These phases are no longer the best way to describe day-to-day execution. The core outcomes from the foundation phases now exist as ongoing program constraints rather than isolated milestones:

- migration and architecture planning are documented in [MIGRATION_PLAN.md](MIGRATION_PLAN.md);
- contributor and operator workflows are documented in [QUICKSTART.md](QUICKSTART.md) and [PROJECT_STATUS.md](PROJECT_STATUS.md);
- provider abstraction and metadata-correctness work continue as incremental slices inside `develop` and `staging` rather than a separate big-bang implementation phase;
- release safety, rollback, and observability now drive prioritization for migration work.

### Phase 5 consolidation

Phase 5 is the current program baseline.

Completed or stable:

- protected branch parity across `develop`, `staging`, and `main`;
- required check emission hardened so protected branches consistently receive required contexts;
- release-readiness reporting and branch-policy auditing available for scheduled and manual execution;
- main-compatible smoke validation added so the required smoke context can execute against legacy and current branch layouts;
- operator runbooks refreshed for branch protection, readiness, and merge reliability.

In progress:

- continue migration-safe provider work through `develop` and `staging` without destabilizing release lanes;
- keep branch drift observable and bounded as operational automation expands;
- reduce security drift with lockfile-backed remediation slices.

Phase 5 exit criteria:

- `develop` and `staging` remain operationally aligned with low drift;
- readiness and branch-policy reporting remain green on `main`;
- security remediation backlog is reduced to a small, explicitly tracked set of exceptions.

### Phase 6 hardening

Phase 6 is active and focused on release confidence rather than feature breadth.

Completed or validated:

- packaging validation is green on both `develop` and `staging` for binary, Docker, and npm lanes;
- manual workflow dispatch from `main` has been validated for release readiness and branch-policy audit workflows;
- permission-limited reporting mode preserves useful artifacts when Actions integration tokens receive `403 Resource not accessible by integration`.

In progress:

- release entry criteria for `main`;
- scheduled operational drift detection;
- security-drift cleanup sequencing with lockfile evidence;
- operator-facing readiness snapshots for release decisions.

Phase 6 exit criteria:

- release entry criteria are documented and repeatable;
- branch drift is automatically surfaced before release work stalls;
- packaging scope can be promoted from `develop` and `staging` to `main` without compatibility exceptions.

### Phase 7 release preparation

Phase 7 starts only after Phase 6 hardening gates are consistently met.

Planned entry conditions:

- latest packaging validation successful on both `develop` and `staging`;
- latest readiness and branch-policy report runs successful on `main`;
- remaining open dependency alerts either remediated or explicitly accepted with documented rationale;
- release workflows, runbooks, and rollback steps verified against current repository reality.

## Current Milestones

| Milestone | Target state | Current status |
|---|---|---|
| Branch protection parity | `develop`, `staging`, and `main` share required contexts and review policy | complete |
| Main dispatch validation | manual readiness and branch-policy workflows succeed on `main` | complete |
| Packaging lane validation | binary, Docker, and npm packaging green on `develop` and `staging` | complete |
| Operational drift checks | scheduled drift signal exists with actionable artifacts | in progress |
| Release entry criteria | `main` release gate documented and enforced operationally | in progress |
| Security drift cleanup | open Dependabot set reduced via lockfile-backed remediation slices | in progress |

## Near-Term Delivery Sequence

1. Keep `develop` and `staging` green for backend, docs, smoke, and packaging validation.
2. Keep `main` green for readiness reporting and branch-policy audit dispatch.
3. Reduce dependency security drift in small, verifiable lockfile slices.
4. Promote release entry criteria from documentation to routine operator practice.
5. Reassess whether packaging validation can safely move onto `main` after installation paths are fully validated.

## Operational Notes

- Do not treat `main` packaging absence as a regression. It is an intentional scope boundary until release installation paths are fully validated.
- Do not treat Actions integration-token `403` responses as release blockers when the report artifacts explicitly mark the run as permission-limited and all available checks still succeed.
- Prefer incremental migration slices with rollback clarity over broad architectural rewrites.

## Related Documents

- [MIGRATION_PLAN.md](MIGRATION_PLAN.md)
- [PROJECT_STATUS.md](PROJECT_STATUS.md)
- [QUICKSTART.md](QUICKSTART.md)
- [docs/operations/RELEASE_AUTOMATION.md](docs/operations/RELEASE_AUTOMATION.md)
- [docs/operations/release-readiness-report-2026-03-16.md](docs/operations/release-readiness-report-2026-03-16.md)