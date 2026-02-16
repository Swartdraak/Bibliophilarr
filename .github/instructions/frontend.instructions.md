---
applyTo: "frontend/**/*.{js,jsx,ts,tsx,css}"
---
# Frontend (React/UI) Custom Instructions

## Scope
These instructions apply to UI code in `frontend/`.

## UX and Product Alignment
- Preserve existing interaction patterns and visual consistency unless explicitly changing UX.
- For metadata migration features, keep provider complexity understandable to end users.
- Favor progressive disclosure: show essential info first, advanced provider details when needed.

## Implementation Rules
- Keep components focused and reusable; avoid coupling view logic to unrelated global concerns.
- Maintain predictable state transitions and clear loading/empty/error states.
- Surface provider errors in user-friendly language and include actionable recovery hints.
- Keep accessibility in mind (labels, keyboard navigation, focus states, contrast).

## Reliability and Performance
- Avoid unnecessary re-renders and expensive synchronous work in render paths.
- Use memoization/selectors when beneficial and justified.
- Do not block core library workflows when metadata enhancement features fail.

## Testing Expectations
- Add or update tests for changed component behavior and edge states.
- Validate loading, success, partial-data, and error/fallback UI paths.
- Prefer stable test selectors and deterministic fixtures.

## CI/CD Quality Gate
- Ensure frontend build/tests for impacted areas pass before proposing changes.
