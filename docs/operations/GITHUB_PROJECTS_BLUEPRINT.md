# GitHub Projects Blueprint

This blueprint defines recommended GitHub Projects for Bibliophilarr's current revival stage.

## Project 1: Metadata Migration Program

**Purpose:** Track the Goodreads-to-FOSS migration as a multi-phase delivery program.

**Suggested fields**
- `Status` (Backlog, Ready, In Progress, Blocked, In Review, Done)
- `Phase` (P1..P10 aligned with migration plan)
- `Provider` (Open Library, Inventaire, Google Books, Internal)
- `Priority` (P0..P3)
- `Risk` (Low, Medium, High)
- `Target Release` (v0.x milestones)

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

- `v0.1` Foundation and interface stabilization.
- `v0.2` Open Library provider GA.
- `v0.3` Inventaire integration and quality scoring improvements.
- `v1.0` Multi-provider production readiness.
