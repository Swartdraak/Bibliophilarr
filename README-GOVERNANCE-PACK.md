# Bibliophilarr Repository Governance Pack

This package adds hierarchical agent instructions, an explicit repository architecture map, and a controlled branch-promotion workflow.

## Files intended to copy into the repository

- `AGENTS.md`
- `ARCHITECTURE.md`
- `BRANCHING.md`
- `CONTRIBUTING.md`
- `.github/copilot-instructions.md`
- `.github/prompts/orchestrator-session-start.prompt.md`
- `.github/AGENTS.md`
- `.github/agents/AGENTS.md`
- `.github/workflows/AGENTS.md`
- `.vscode/AGENTS.md`
- `.devcontainer/AGENTS.md`
- `.claude/AGENTS.md`
- `src/AGENTS.md`
- `src/Bibliophilarr.Api.V1/AGENTS.md`
- `src/NzbDrone.Core/MetadataSource/AGENTS.md`
- `src/NzbDrone.Core/MediaFiles/AGENTS.md`
- `src/NzbDrone.Core/Download/AGENTS.md`
- `frontend/AGENTS.md`
- `tests/AGENTS.md`
- `tests/test-stack/AGENTS.md`
- `scripts/AGENTS.md`
- `docs/AGENTS.md`
- `npm/AGENTS.md`

`CONTRIBUTING.md`, `.github/copilot-instructions.md`, and the orchestrator prompt are intended as replacements for the current versions because the old versions do not fully encode the branch-promotion and hierarchical instruction policy.

## VS Code setting required for nested AGENTS.md

Do not replace your existing `.vscode/settings.json`.

Merge the two keys from `VSCODE-SETTINGS-MERGE.json` into the existing JSON object:

```json
{
  "chat.useAgentsMdFile": true,
  "chat.useNestedAgentsMdFiles": true
}
```

Then use VS Code Chat customization diagnostics to verify discovery of the root and nested `AGENTS.md` files.

## Branch model

```text
feat/*, fix/*, chore/*, docs/*, test/*, ...
                    |
                    v
                 develop
                    |
             promotion PR
                    v
                 staging
                    |
            release validation
                    v
                  main
                    |
             stable SemVer tag
                    |
             release packaging
```

Key rules:

- Normal task branches branch from `develop`.
- Normal task PRs target `develop`.
- `develop -> staging` is controlled release-candidate promotion.
- `staging -> main` is controlled production promotion.
- `main` represents every stable patch/minor/major release.
- Stable release artifacts originate from a stable tag on `main`.
- Emergency `hotfix/*` is the only routine `main`-based exception and requires explicit human approval plus reconciliation into development lines.
- Routine Dependabot PRs should target `develop`.

## Recommended repository settings

Protect `develop`, `staging`, and `main` with PR requirements, applicable required checks, resolved conversations, blocked force pushes/deletions, and restricted bypasses.

Keep `badge-data` automation-owned if the badge workflow depends on it.

## First orchestrator run

Use `.github/prompts/orchestrator-session-start.prompt.md` with a blank objective.

It should load the root/scoped instruction hierarchy, verify branch/promotion state, inventory live GitHub work, and route normal task development toward `develop` rather than `main`.
