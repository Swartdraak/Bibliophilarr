> [!WARNING]
> **DEPRECATED** — This document has been superseded.
> Canonical replacement: [MIGRATION_PLAN.md](../../MIGRATION_PLAN.md)
> Reason: Wiki architecture summary duplicated the canonical migration architecture.
> Deprecation date: 2026-03-17

# Architecture overview

## High-level stack

- **Backend:** .NET / C# services and API.
- **Frontend:** React + TypeScript application.
- **Domain focus:** Book and audiobook metadata ingestion, search, and automation.

## Metadata architecture (target state)

- Provider abstraction layer.
- Multi-provider fallback strategy.
- Metadata quality scoring.
- Identifier mapping and backward compatibility.

## Core migration decisions

- Open Library as primary source.
- Inventaire as secondary source.
- Optional tertiary fallback where needed.
- Preserve legacy IDs during transition.

## Key technical concerns

- External API rate limits.
- Variable metadata quality.
- Identifier normalization.
- Schema migration safety.