---
applyTo: "**/*.md"
---
# Documentation Custom Instructions

## Scope

These instructions apply to Markdown documentation.

## Documentation Intent

- Keep docs aligned with Bibliophilarr's migration mission: FOSS metadata providers, reliability, and sustainability.
- Prefer actionable, operationally useful docs over abstract descriptions.

## Writing Standards

- Use clear headings and task-oriented structure.
- Include prerequisites, commands, expected outcomes, and troubleshooting notes.
- Explicitly call out assumptions, risks, and known limitations.
- Link related docs to maintain navigability across roadmap/status/plans.

## Change Management

When updating docs for a behavior change, include:

1. What changed
2. Why it changed
3. How to validate it
4. Operational impact and rollback/mitigation notes

## DevOps/CI-CD Emphasis

- Encourage iterative cycles (plan → implement → verify → document).
- Document test strategy and pipeline expectations for each significant initiative.
- Keep runbooks and checklists current with actual repository workflows.

## No New Tracking Files

The canonical files listed in `copilot-instructions.md` are the **only** authoritative
locations for project status, roadmap, migration plans, and contribution guidance.

**When performing any documentation task:**

- **Update the existing canonical file** — never create a parallel or supplemental file.
- **Direct map:** status/workstream updates → `PROJECT_STATUS.md`; phase and priority
  changes → `ROADMAP.md`; architecture and migration steps → `MIGRATION_PLAN.md`;
  setup or command changes → `QUICKSTART.md`; release notes → `CHANGELOG.md`.
- **No ad-hoc tracking files** such as `PLAN_<date>.md`, `STATUS_<sprint>.md`,
  `NOTES.md`, or similar named variants. These are documentation drift.
- If a genuinely new document is warranted (e.g. a subsystem runbook not covered by
  any canonical file), confirm with the user and record it in `copilot-instructions.md`
  before creating it.
- When asked to consolidate, use the `documentation-maintainer` agent; do not create a
  new summary file alongside the canonical ones.
