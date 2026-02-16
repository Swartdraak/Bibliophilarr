---
applyTo: "src/**/*.cs"
---
# Backend (C#/.NET) Custom Instructions

## Scope
These instructions apply to backend C# code under `src/`.

## Architecture and Boundaries
- Keep domain logic in core/domain services; avoid leaking API/transport concerns into core models.
- Preserve existing interface-driven patterns (dependency injection, service abstractions, provider contracts).
- For metadata-provider changes, implement behavior behind explicit interfaces and keep provider-specific code isolated.

## Implementation Rules
- Prefer small, composable services over monolithic classes.
- Use async APIs for I/O-bound work and respect cancellation tokens where available.
- Validate and normalize external provider payloads defensively.
- Add structured logging around external calls, fallback decisions, and error states.
- Preserve backward compatibility unless a breaking change is intentional and documented.

## Resilience Requirements
- Add/maintain timeout, retry, and backoff for external metadata endpoints.
- Handle partial provider outages gracefully with fallback behavior.
- Avoid global failure for single-provider errors when alternate providers can satisfy requests.

## Testing Expectations
- Add/adjust unit tests for business logic, mappings, and provider selection/fallback.
- Prefer deterministic fixtures over live network calls in automated tests.
- Include edge-case tests for null/empty/malformed responses and identifier mismatches.

## CI/CD Quality Gate
- Ensure `dotnet build` and impacted tests pass locally before proposing changes.
- Keep migrations safe and idempotent; include rollback notes for schema-impacting changes.
