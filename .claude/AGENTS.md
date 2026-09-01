# .claude Directory Contract

## Purpose

`.claude/` contains Claude-compatible workspace configuration and agent/customization settings.

## Rules

- Keep cross-agent repository policy aligned with root `AGENTS.md`.
- Do not duplicate or weaken global safety/branching rules here.
- Do not store secrets or personal credentials.
- Claude-specific configuration may add tool/runtime preferences but must not override protected invariants, branch promotion, independent validation, or human gates.
- When behavior is intended to apply to all agents, place it in root/nested `AGENTS.md` rather than only in Claude-specific configuration.

Normal PR target is `develop`.
