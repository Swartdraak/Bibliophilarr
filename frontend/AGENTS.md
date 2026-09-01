# frontend Directory Contract

## Purpose

`frontend/` contains the React/WebUI application, UI state, components, styles, client-side API interaction, and frontend tests.

## Rules

- Preserve the repository's current React/toolchain unless an upgrade is the explicit task.
- Avoid unrelated component rewrites.
- Maintain accessibility and keyboard behavior.
- Keep API assumptions aligned with `src/Bibliophilarr.Api.V1`.
- Do not hide backend errors merely to make the UI appear successful.
- Add regression coverage for fixed interactive behavior.

## Known high-value regression areas

PageJumpBar/alphabet navigation and other measurement/resize-sensitive components should be tested across repeated resize/filter/sort/navigation cycles.

## Validation

Typical:

```bash
yarn lint
yarn test:frontend
yarn build
```

Interactive changes should receive browser-level validation using `qa-webui-e2e-validator` where appropriate.

## Branching

Normal frontend task branches originate from and PR to `develop`.

## Preferred agents

`frontend-webui-engineer`, `qa-webui-e2e-validator`, and `qa-build-validator`.
