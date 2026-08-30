# src Directory Contract

## Purpose

`src/` contains the .NET application, shared libraries, domain logic, API projects, integrations, updater/runtime components, and .NET test projects.

## General rules

- Preserve the existing solution/project architecture.
- Prefer local, bounded changes over cross-solution refactors.
- Maintain runtime compatibility defined by repository configuration.
- Follow `.editorconfig`.
- Do not introduce unrelated package upgrades.
- Do not move code between projects without architectural justification.
- Public API/contract changes require explicit review and documentation.

## High-risk domains

Treat metadata/search/dedupe, ebook/audiobook dual-format behavior, media/file handling, imports, completed downloads, database/migrations, authentication, and updater/release behavior as R3.

Read deeper `AGENTS.md` files when touching governed subtrees.

## Validation

Typical baseline:

```bash
dotnet build src/Bibliophilarr.sln -p:Configuration=Debug -p:Platform=Posix
dotnet test src/Bibliophilarr.sln
```

Use targeted project/filter tests first for local feedback, then broaden according to impact.

## Branching

Normal `src/` work branches from `develop` and PRs to `develop`.

Do not open ordinary backend changes directly to `staging` or `main`.

## Preferred agents

`backend-api-engineer`, `metadata-search-engineer`, `import-file-lifecycle-engineer`, `integration-engineer`, and applicable independent QA validators.
