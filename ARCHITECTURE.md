# Bibliophilarr Repository Architecture and Directory Map

This document is the high-level navigation contract for the repository. It is not a replacement for implementation-specific documentation or nested `AGENTS.md` files.

## Repository-level responsibilities

| Path | Purpose |
|---|---|
| `.github/` | GitHub automation, workflows, custom agents, prompts, skills, issue/PR templates, repository governance |
| `src/` | .NET application source, API, domain logic, integrations, services, and .NET tests/projects |
| `frontend/` | React/WebUI source, styles, UI state, client-side behavior, frontend tests |
| `tests/` | Cross-cutting fixtures, profiles, disposable stack assets, integration/evidence helpers not owned by a single .NET project |
| `scripts/` | Developer, CI, audit, migration, diagnostics, and operational automation |
| `docs/` | Approved project documentation, operational runbooks, audit/evidence material |
| `npm/` | npm launcher/package-specific content |
| `.devcontainer/` | Reproducible development-container configuration |
| `.vscode/` | Workspace settings and developer/editor integration |
| `.claude/` | Claude-compatible workspace/agent configuration |
| `.test-env/` | Disposable runtime state only; generated contents must not become source-of-truth project data |

Each maintained area must have an applicable `AGENTS.md`.

## System layers

### Common/infrastructure

`src/NzbDrone.Common` contains common runtime/infrastructure primitives shared across application layers.

Changes here can have broad impact. Avoid application-specific shortcuts in common abstractions.

### Core/domain

`src/NzbDrone.Core` owns much of the inherited *arr domain behavior and Bibliophilarr's library automation logic.

High-risk subdomains include metadata providers and search, author/book/edition identity, file/media handling, download completion, indexer/download-client interactions, persistence/migrations, and dual ebook/audiobook handling.

Nearest nested `AGENTS.md` files define stricter rules for these areas.

### API

`src/Bibliophilarr.Api.V1` exposes API resources/controllers for external and WebUI consumers.

Preserve API contracts unless a breaking change is explicitly planned, versioned, documented, and validated.

### WebUI

`frontend/` contains the React-based application UI.

UI changes must preserve API assumptions and should receive browser-level validation for interactive/regression-prone behavior.

## Protected cross-layer flows

### Metadata flow

```text
provider response
  -> provider client
  -> normalization/mapping
  -> canonical identity/search/scoring/dedupe
  -> persisted domain model
  -> API
  -> WebUI
```

Changes at any stage can alter downstream identity and import behavior.

### Library/import flow

```text
disk/download result
  -> discovery
  -> media type / parsing / identification
  -> candidate matching
  -> import decision
  -> move/rename/organization
  -> persisted tracking
  -> restart/rescan
```

Ebook and audiobook behavior must be validated independently and together.

### Download flow

```text
indexer result
  -> grab
  -> download client
  -> completed download
  -> path mapping
  -> file discovery
  -> import
  -> tracking
```

A completed download with zero importable files must fail safely and must not create an uncontrolled retry loop.

## Repository knowledge model

Use root `AGENTS.md` for global agent behavior, nested `AGENTS.md` for directory-specific behavior, `BRANCHING.md` for code movement/release promotion, `CONTRIBUTING.md` for contributor mechanics, `QUICKSTART.md` for commands, and ROADMAP/PROJECT_STATUS/MIGRATION_PLAN for strategic/current project state.

Do not turn `AGENTS.md` into an encyclopedia. It should point agents toward authoritative deeper documents and define boundaries they cannot safely infer.

## Adding or restructuring directories

When introducing a maintained top-level directory or a major subsystem:

1. define its architectural purpose;
2. add/update an applicable `AGENTS.md`;
3. identify allowed and prohibited dependencies;
4. define test/validation entry points;
5. update this architecture map if repository-level structure changes;
6. update contributor/setup docs when workflow changes.

Do not create miscellaneous permanent `utility`, `misc`, or `temp` directories for unclear responsibilities.
