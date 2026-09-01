# Bibliophilarr.Api.V1 Directory Contract

## Purpose

This subtree owns the versioned Bibliophilarr API surface: resources, controllers, request/response contracts, validation, and API-side behavior.

## Contract rules

- Preserve API compatibility unless a breaking change is explicitly planned and approved.
- Use explicit request binding where complex payload binding is involved.
- Validate user/external input.
- Do not leak secrets/internal paths.
- Keep side effects explicit.
- Preserve authorization behavior.
- Document user-visible/API-contract changes.

## Validation

Build targeted project/solution, run relevant tests, use `qa-api-contract-validator`, and validate running endpoints in the disposable environment when behavior is stateful/high risk.

## Escalation

Use `repository-architect` before broad contract redesign and security review for auth/binding/sensitive-data changes.

Normal PR target is `develop`.
