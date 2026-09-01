# .vscode Directory Contract

## Purpose

`.vscode/` contains shared VS Code workspace settings, tasks, launch config, and extension recommendations.

## Rules

- Keep shared workspace settings aligned with root `AGENTS.md` and the repository's documented build/test commands (`QUICKSTART.md`).
- Do not store secrets, tokens, or personal/editor-specific credentials here; shared settings only.
- Do not make VS Code tasks the sole or authoritative build/test path; the canonical commands remain those in `QUICKSTART.md` and `CONTRIBUTING.md`.
- When a setting or task changes behavior that contributors rely on, update `README-GOVERNANCE-PACK.md` (for the AGENTS.md discovery keys `chat.useAgentsMdFile` / `chat.useNestedAgentsMdFiles`) or `CONTRIBUTING.md` in the same change if applicable.
- Do not weaken global safety, branch-promotion, validation, or human-gate rules defined in root `AGENTS.md`; this directory contract may only add editor-specific constraints.

Normal PR target is `develop`.
