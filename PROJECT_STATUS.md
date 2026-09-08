# Project status summary

**Last verified**: 2026-09-08
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

- #192 Documentation drift remediation for this file and cross-doc consistency.
- #193 Naming convergence planning for legacy `NzbDrone.*` paths vs Bibliophilarr identity.
- #194 Local tooling artifact ignore policy and contributor guidance.
- #195 Rider inspection follow-up after promotion and cache refresh.
- #197 Promotion checklist for `develop -> staging` validation cycle.

## Branch and promotion posture

- `develop -> staging` promotion remains the next coordinated gate once open issue remediation and validation slices are complete.
- `staging -> main` remains blocked until migration validation issues are closed (#103 through #107) and release checks are green.

## Release and CI posture

- Backend/frontend/docs/workflow lint validations remain branch-protected required contexts.
- CodeQL and cross-platform build checks are active and required for merge readiness in protected lanes.

## Notes on historical content

Earlier entries in this file that described open Dependabot PR queues and pre-normalization branch states are now superseded by this verified status baseline.

## Related references

- [README.md](README.md)
- [BRANCHING.md](BRANCHING.md)
- [ROADMAP.md](ROADMAP.md)
- [MIGRATION_PLAN.md](MIGRATION_PLAN.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [SECURITY.md](SECURITY.md)
