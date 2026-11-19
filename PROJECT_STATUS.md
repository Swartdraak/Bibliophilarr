# Project status summary

**Last verified**: 2026-11-19
**Verification scope**: branch topology, open issue queue, PR checks, release channels, dependency posture
**Current phase**: Phase 5 consolidation with Phase 6 hardening

## Current operational state

- Protected promotion lanes are active: `develop`, `staging`, `main`.
- Current release channels remain GitHub Releases, GHCR Docker image, and npm launcher.
- `develop` is the integration lane, `staging` is release-candidate validation, and `main` is production/stable.
- PR metadata enforcement is active (`label-policy`) with required type/area/priority/risk labels, assignee, and structured PR body sections.

## Active migration and modernization work

The migration program remains open and intentionally tracked through these active issues:

- #96 Migration Program: .NET 8 -> .NET 10 LTS
- #97 Workflow and action modernization train
- #98 FluentMigrator coordinated upgrade train
- #99 Frontend toolchain modernization train
- #103 NuGet compatibility review
- #104 ASP.NET / SignalR runtime alignment
- #105 Docker SDK/runtime migration validation
- #106 Tests/RID/platform validation
- #107 Release/package validation

## Governance and documentation tracks

- #193 Naming convergence planning for legacy `NzbDrone.*` paths vs Bibliophilarr identity.
- #194 Local tooling artifact ignore policy and contributor guidance.

## Branch and promotion posture

- `develop -> staging` promotion remains the next coordinated gate once open issue remediation and validation slices are complete.
- `staging -> main` remains blocked until migration validation issues are closed (#103 through #107) and release checks are green.

## Release and CI posture

- The latest stable release is [v1.1.0](https://github.com/Swartdraak/Bibliophilarr/releases/tag/v1.1.0).
- Protected branches require backend CI, Markdown lint, PR metadata, Linux, Windows, macOS and RID portability, and metadata telemetry smoke. Frontend, workflow lint and CodeQL results also inform release readiness.
- Native macOS startup remains open in [PR #413](https://github.com/Swartdraak/Bibliophilarr/pull/413). The Windows smoke job passed and the macOS job failed.
- Use the live [issues](https://github.com/Swartdraak/Bibliophilarr/issues) and [pull requests](https://github.com/Swartdraak/Bibliophilarr/pulls) for the current queues.

## Branch reconciliation

The integration, staging, and stable branches each contain commits absent from the next or preceding lane. Reconcile the branch-only changes before promotion, verify required checks on the promoted commit, and retain history for any outstanding work.

## Related references

- [README.md](README.md)
- [BRANCHING.md](BRANCHING.md)
- [ROADMAP.md](ROADMAP.md)
- [MIGRATION_PLAN.md](MIGRATION_PLAN.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [SECURITY.md](SECURITY.md)
