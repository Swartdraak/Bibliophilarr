> [!WARNING]
> **DEPRECATED** — This document has been superseded.
> Canonical replacement: [Changelog](../../../CHANGELOG.md)
> Reason: This one-time maintenance summary was incorporated into canonical documentation and changelog tracking.
> Deprecation date: 2026-03-17

# Documentation Maintenance Report — 2026-03-17

## Files modified

- README.md: reduced to a single H1 and re-centered on the canonical doc set.
- QUICKSTART.md: removed stale setup steps and aligned commands with the root toolchain.
- CONTRIBUTING.md: tightened contribution workflow, validation expectations, and scoped-commit guidance.
- SECURITY.md: replaced stale Servarr-specific reporting guidance with repository-appropriate handling guidance.
- PROJECT_STATUS.md: repaired broken internal doc links and expanded related docs.
- MIGRATION_PLAN.md: removed duplicate references structure and normalized provider citations.
- docs/operations/METADATA_PROVIDER_RUNBOOK.md: added required source citations for provider and SLO claims.
- docs/operations/RELEASE_AUTOMATION.md: aligned workflow inventory and release-entry criteria with actual repository files.
- docs/operations/BRANCH_PROTECTION_RUNBOOK.md: added references for policy and audit claims.
- .github/PULL_REQUEST_TEMPLATE.md: replaced stale wiki and translation checklist items with current validation and rollback fields.
- docs/operations/release-readiness-snapshot-2026-03-17.md: clarified that historical packaging-workflow labels are archived evidence, not active workflow files.
- docs/operations/metadata-dry-run-snapshots/2026-03-17-blocked.md: fixed the canonical dry-run runbook reference.

## Files added

- CHANGELOG.md: introduced canonical change tracking.
- docs/operations/METADATA_MIGRATION_DRY_RUN.md: added the missing dry-run runbook referenced by status docs.
- docs/operations/SCOPED_COMMIT_PROCESS.md: added the missing scoped-commit runbook referenced by contributor docs.

## Files archived

- wiki/Home.md -> docs/archive/Home.md: superseded by README.md and the canonical doc index.
- wiki/Architecture.md -> docs/archive/Architecture.md: superseded by MIGRATION_PLAN.md.
- wiki/Contributor-Onboarding.md -> docs/archive/Contributor-Onboarding.md: superseded by QUICKSTART.md.
- wiki/Metadata-Migration-Program.md -> docs/archive/Metadata-Migration-Program.md: superseded by MIGRATION_PLAN.md.

## Citations added

- MIGRATION_PLAN.md: provider API and migration-planning claims.
- docs/operations/METADATA_PROVIDER_RUNBOOK.md: provider endpoint and telemetry-threshold claims.
- docs/operations/RELEASE_AUTOMATION.md: workflow and release-entry authority.
- docs/operations/BRANCH_PROTECTION_RUNBOOK.md: policy and audit workflow authority.
- SECURITY.md: security-handling policy authority.

## Inventory classification

- Canonical: README.md, QUICKSTART.md, ROADMAP.md, MIGRATION_PLAN.md, PROJECT_STATUS.md, CONTRIBUTING.md, SECURITY.md, CHANGELOG.md.
- Active reference: .github agent and prompt docs, issue templates, and docs/operations runbooks and dated evidence.
- Archived: former wiki summaries now stored under docs/archive.
- Needs future review: legal and policy documents outside the canonical set, including CLA.md, CODE_OF_CONDUCT.md, and LICENSE.md, were retained unchanged.

## Issues deferred

- Packaging validation is described in roadmap and status docs, but no dedicated packaging workflow file exists in .github/workflows; keep using dated install evidence until that workflow is added.
- Security reporting still lacks a repository-specific private contact address; maintainers should document one if they want a non-GitHub path.
- The operational docs were not exhaustively re-cited line by line; future passes should extend citation coverage to dated security and readiness snapshots if they become long-lived references.

## Risk notes

- Release-entry language now matches repository reality more closely, but maintainers should treat any future workflow additions as documentation-sensitive changes.
- Archived wiki content remains preserved under docs/archive, so no historical planning context was deleted.
