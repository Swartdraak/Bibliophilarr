# Metadata Migration Program

## Program goal

Move Bibliophilarr from legacy/proprietary metadata dependencies to sustainable FOSS providers without data loss.

## Program workstreams

1. **Provider Abstractions**
   - Standard capability interfaces.
   - Search and lookup contracts.

2. **Open Library Integration**
   - Search, author/book retrieval, identifier mapping.
   - Rate limit and retry handling.

3. **Inventaire Integration**
   - Supplementary search and metadata enrichment.
   - Data merge and conflict strategies.

4. **Identifier Migration**
   - Goodreads ID continuity.
   - ISBN/OLID/provider ID mapping.

5. **Quality and Validation**
   - Metadata scoring.
   - Regression tests and confidence checks.

## Program milestones

- `v0.1`: Interfaces and scaffolding.
- `v0.2`: Open Library MVP.
- `v0.3`: Inventaire + aggregation.
- `v1.0`: Production-ready FOSS metadata pipeline.
