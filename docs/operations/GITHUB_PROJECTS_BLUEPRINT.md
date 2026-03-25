# GitHub Projects Blueprint

This blueprint defines recommended GitHub Projects for Bibliophilarr's current revival stage.

## Project 1: Metadata Migration Program

**Purpose:** Track the legacy-metadata-to-FOSS migration as a multi-phase delivery program.

**Suggested fields**

- `Status` (Backlog, Ready, In Progress, Blocked, In Review, Done)
- `Phase` (P1..P10 aligned with migration plan)
- `Provider` (Open Library, Inventaire, Google Books, Internal)
- `Priority` (P0..P3)
- `Risk` (Low, Medium, High)
- `Target Phase` (Phase 4..7 aligned with ROADMAP.md)

**Views**

- Kanban by Status
- Table grouped by Phase
- Board grouped by Provider
- "High Risk" filtered view

## Project 2: Platform Reliability & CI/CD

**Purpose:** Improve test confidence, build speed, and deployment hygiene.

**Suggested fields**

- `Pipeline` (Backend, Frontend, Docs, Release)
- `Status`
- `Priority`
- `Owner`

**Views**

- Kanban by Status
- Table by Pipeline + cycle time

## Project 3: Community & Documentation

**Purpose:** Organize onboarding docs, migration guides, and contributor enablement.

**Suggested fields**

- `Doc Type` (Wiki, How-to, Reference, Tutorial)
- `Audience` (User, Contributor, Maintainer)
- `Status`

**Views**

- Kanban by Status
- Table by Audience

---

## Automation Suggestions

- Auto-add issues/PRs to relevant project by `area:*` labels.
- Set `Status=In Progress` when PR is linked/opened.
- Set `Status=In Review` when review requested.
- Set `Status=Done` when PR merges/closes linked issue.

## Milestone Mapping

Milestones align with the phase-based delivery model in `ROADMAP.md`:

- **Phase 4** — Multi-provider consolidation and quality scoring.
- **Phase 5** — Provider reliability hardening and performance optimization.
- **Phase 6** — Packaging, infrastructure, and supply-chain hardening.
- **Phase 7** — Platform modernization (React 18, .NET 10, Node 22).

## References

- [ROADMAP.md](../../ROADMAP.md) — Phased delivery milestones and current priorities.
- [PROJECT_STATUS.md](../../PROJECT_STATUS.md) — Remediation queue and operational state.
- [CONTRIBUTING.md](../../CONTRIBUTING.md) — Contribution guidelines and issue labels.
