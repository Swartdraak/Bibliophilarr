# Hardcover Fallback UI and Timeout Hardening - 2026-03-15

## What Changed

1. Added local Hardcover token injection in development environment configuration.
- Updated `.env` with `HARDCOVER_API_TOKEN` using the provided bearer token for local provider testing.

2. Added metadata settings UI controls for Hardcover fallback management.
- Added fields in metadata settings UI:
  - `EnableHardcoverFallback` (toggle)
  - `HardcoverApiToken` (password field)
  - `HardcoverRequestTimeoutSeconds` (numeric override, 0-120)

3. Added config/API support for Hardcover timeout override.
- Added `HardcoverRequestTimeoutSeconds` to config contract and storage.
- Exposed timeout in metadata provider API resource mapping.
- Added API validation constraint: timeout must be between `0` and `120` seconds.

4. Hardened Hardcover provider request handling.
- Added token normalization to accept either:
  - raw JWT token
  - `Bearer <token>` value
- Added optional per-provider request timeout assignment when timeout override is enabled.

5. Expanded deterministic test coverage.
- Added negative-path test for GraphQL error payloads where `data` is absent.
- Added test for bearer prefix normalization and timeout override behavior.

## Why It Changed

- Local operations requested direct use of a personal Hardcover token in `.env`.
- Hardcover fallback needed UI-level operability so it can be toggled and tuned without direct config API/manual DB edits.
- Provider calls needed explicit timeout control for stricter external API behavior.
- Negative-path safety was required for malformed/GraphQL error responses.

## Validation Performed

Targeted fallback tests:

- `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter "FullyQualifiedName~HardcoverFallbackSearchProviderFixture|FullyQualifiedName~CandidateServiceFallbackOrderingIntegrationFixture" --nologo`
- Result: `Passed: 7, Failed: 0`

Full core test suite:

- `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --nologo`
- Result: `Passed: 2536, Failed: 0, Skipped: 84, Total: 2620`

Frontend build:

- `cd frontend && yarn build`
- Result: `webpack compiled successfully`

## Operational Impact

- Hardcover fallback can now be operated from the metadata settings UI.
- Hardcover bearer tokens entered with or without `Bearer ` prefix are accepted.
- Hardcover requests can use stricter, provider-specific timeout behavior.
- GraphQL error payloads no longer risk candidate mapping failures; provider safely returns no candidates.

## Rollback / Mitigation

1. Disable Hardcover fallback in metadata settings (`EnableHardcoverFallback = false`).
2. Set timeout override to `0` to use global HTTP request timeout behavior.
3. Remove/clear local token in `.env` to disable authenticated provider calls from local environment.
4. Revert provider/UI/config changes if fallback behavior needs to return to Google-only tertiary mode.

## Status: Documented vs Actual

Documented status in `PROJECT_STATUS.md`:

- Phase 2 infrastructure listed as near-complete.
- Fallback ordering and tertiary provider hardening listed as complete.

Actual validated status in this change set:

- In-app second tertiary fallback provider (Hardcover) is implemented and tested.
- UI control surface for Hardcover toggle/token/timeout is implemented and builds successfully.
- Core suite remains green after all changes.
