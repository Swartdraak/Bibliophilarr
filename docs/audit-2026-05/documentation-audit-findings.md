# Documentation Audit Findings — 2026-05-24

> [!NOTE]
> This file is a verbatim preservation of the `documentation-auditor-readonly` agent
> output from the May 24, 2026 clean-slate audit. Do not edit — update the parent
> [report.md](report.md) §8 summary instead.

See [report.md §8](report.md#8-documentation-audit) for the actionable summary.

## Source

Agent: `documentation-auditor-readonly`  
Scope: root canonical docs, `docs/`, `wiki/`, `.github/`  
Files scanned: ~65  
Findings: 13 (1 Critical, 5 High, 4 Medium, 3 Low)

## Critical

**CRIT-01:** `v1.2.0` forward-referenced in three files but does not exist in CHANGELOG.

- ROADMAP.md (lines 555, 562)
- wiki/Updates-and-Branches.md (line 9)
- docs/operations/RELEASE_AUTOMATION.md (line 152)

## High

**HIGH-01:** wiki/Metadata-Migration-Program.md Phase 7 lists "test infrastructure" as
planned; ROADMAP.md milestone table marks it complete.

**HIGH-02:** MIGRATION_PLAN.md March 24 audit snapshot has three findings
(RQ-007, RQ-006, RQ-066) never annotated as FIXED.

**HIGH-03:** docs/operations/ZERO_LEGACY_BRAND_CHANGEOVER_PLAN.md missing `## References`
section. RQ-148 applied it to four files but not this one.

**HIGH-04:** docs/operations/DOTNET_MODERNIZATION.md is a completed historical doc in an
active path without the formal `> [!WARNING]` archive banner (docs-style Rule D1).

**HIGH-05:** `.github/workflows/docs-validation.yml` Markdown lint scope excludes
`wiki/`, `.github/instructions/`, and most `docs/operations/` files.

## Medium

**MED-01:** docs/operations/DOTNET_MODERNIZATION.md — `### References` should be
`## References` (docs-style Rule H2).

**MED-02:** wiki/Architecture.md lists React 17 without cross-referencing the React 18
upgrade path assessment in ROADMAP.md.

**MED-03:** docs/proposals/unmapped-files-upgrade.md — 60+ days stale, no ROADMAP entry.
Status is ambiguous: active, deferred, or abandoned?

**MED-04:** PROJECT_STATUS.md RQ-164 — ".NET 10 LTS expected late 2025" is stale;
.NET 10 LTS shipped November 2025. ROADMAP.md already updated.

## Low

**LOW-01:** npm/bibliophilarr-launcher/README.md — absolute GitHub URLs for internal
links (docs-style Rule L1). Pragmatic justification exists (npm publish context).

**LOW-02:** .github/ISSUE_TEMPLATE/bug_report.yml — example version `0.1.0.432`
(Readarr lineage; current stable is v1.1.0).

**LOW-03:** docs/operations/GITHUB_PROJECTS_BLUEPRINT.md and REPOSITORY_TAGS.md —
orphaned advisory docs not linked from wiki or any workflow runbook.

## References

1. [docs-style.instructions.md](../../.github/instructions/docs-style.instructions.md)
2. [report.md](report.md)
