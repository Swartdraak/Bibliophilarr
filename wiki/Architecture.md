# Architecture Overview

## High-level stack

- **Backend:** .NET / C# services and API.
- **Frontend:** React + TypeScript application.
- **Domain focus:** Book/audiobook metadata ingestion, search, and automation.

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
