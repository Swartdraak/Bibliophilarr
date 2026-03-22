# Bibliophilarr Roadmap

**Last Updated**: March 22, 2026

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

Completed in current hardening slice:

- active Goodreads runtime provider implementations were removed from metadata/import-list/notification paths and replaced by OpenLibrary-oriented behavior in active runtime surfaces;
- OpenAPI/API, localization, and frontend active-text surfaces were migrated away from Goodreads naming to OpenLibrary naming;
- ebook extraction now applies confidence-aware metadata merging (container tags plus filename-derived identifiers) for EPUB, PDF, AZW3, and MOBI import paths;
- import acceptance threshold for close-match decisions is now configurable via metadata provider config with a default of 80 percent to preserve current behavior.
- clean-build and targeted verification coverage for author, series, book, and cover identification paths is now recorded in canonical status reporting.

Long-term cloud strategy — decided and implemented (March 2026):

- The hardcoded `services.bibliophilarr.org` dependency has been replaced by an
   optional `BIBLIOPHILARR_SERVICES_URL` environment variable.
- Default mode is **local-only**: all cloud-backed features (update checks,
   system-time drift, proxy health, server-side notifications) degrade gracefully
   to no-op when the env var is not set. No errors are logged, no registry entries
   are required.
- Operators who want those features may point to a self-hosted or community endpoint.
- There is no plan to operate a Bibliophilarr-managed cloud service. The
   local-only default is the permanent production baseline.
- Runbook: `docs/operations/services-endpoint-runbook.md`.

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
| Metadata path parity debt burn-down | add/refresh/import/identification use aligned orchestrated metadata request policy | complete |
| Identifier normalization convergence | canonical external-ID normalization is enforced at persistence boundaries | complete |
| Health-aware provider routing | provider failure streaks influence fallback order with deterministic recovery | complete |
| Conflict explainability telemetry | factor-level score breakdown is exposed for operator diagnostics | complete |

## Near-Term Delivery Sequence

1. Keep `develop` and `staging` green for backend, docs, smoke, and packaging validation.
2. Keep `main` green for readiness reporting and branch-policy audit dispatch.
3. Reduce dependency security drift in small, verifiable lockfile slices.
4. Promote release entry criteria from documentation to routine operator practice.
5. Execute local install testing on `develop` as a first-class lane (native binary, Docker, and npm launcher).
6. Reassess whether packaging validation can safely move onto `main` after installation paths are fully validated.
7. Promote TD-META-001..005 behaviors into release-entry evidence capture, including provider telemetry and conflict-score snapshots.
8. Add end-to-end metadata parity rehearsal on production-shaped datasets with explicit pass/fail thresholds for catalog retention and match-rate drift.
9. Close remaining high-priority failing Core baseline tests that impact metadata import quality signals.

## Local Install Testing Enablement (Develop Branch)

Purpose:

- The primary mission of `develop` is to prove users can install and run Bibliophilarr locally with deterministic results, not only to pass CI.

Project-level recommendations:

1. Define a weekly install validation matrix owned by maintainers.

   - Linux native package install and first start
   - Docker image run and API/UI health check
   - npm launcher install and binary bootstrap

2. Enforce install readiness as a merge expectation for risky slices.

   - packaging path changes
   - startup/config changes
   - update/installer changes

3. Keep a rolling install evidence trail in versioned markdown snapshots.

   - exact commands
   - environment assumptions
   - pass/fail outcome
   - rollback notes

4. Prioritize install blockers ahead of non-critical feature work on `develop`.
5. Promote only install-proven commits from `develop` to `staging`.

Suggested acceptance criteria for local install readiness:

- Clean install starts and serves `/ping` and core API metadata endpoints.
- Existing config/data directories upgrade without manual data surgery.
- Fallback to prior release is documented and verified for the same environment.
- Any known installer/runtime caveats are captured in QUICKSTART and runbooks.

## Operational Notes

- Do not treat `main` packaging absence as a regression. It is an intentional scope boundary until release installation paths are fully validated.
- Do not treat Actions integration-token `403` responses as release blockers when the report artifacts explicitly mark the run as permission-limited and all available checks still succeed.
- Prefer incremental migration slices with rollback clarity over broad architectural rewrites.

## Related Documents

- [MIGRATION_PLAN.md](MIGRATION_PLAN.md)
- [PROJECT_STATUS.md](PROJECT_STATUS.md)
- [QUICKSTART.md](QUICKSTART.md)
- [docs/operations/RELEASE_AUTOMATION.md](docs/operations/RELEASE_AUTOMATION.md)
