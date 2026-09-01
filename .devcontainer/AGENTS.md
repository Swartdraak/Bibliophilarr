# .devcontainer Directory Contract

## Purpose

`.devcontainer/` defines the reproducible development-container environment for contributors and agents that use it.

## Rules

- Keep runtime/toolchain versions aligned with repository build requirements.
- Do not silently broaden container privileges.
- Do not embed credentials.
- Keep mounts and forwarded ports explicit.
- Dependency/runtime upgrades are separate scoped changes and require build validation.
- Changes must not make the devcontainer the only way to build/test the repository unless explicitly approved.

## Validation

For changes, validate configuration syntax and perform a representative container build/start when feasible, followed by repository build/test smoke checks.

Normal PR target is `develop`.
