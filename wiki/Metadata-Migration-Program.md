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

- **Phase 1–3** (complete): Provider abstractions, Open Library integration, Inventaire integration.
- **Phase 4** (complete): Hardcover integration, multi-provider aggregation, fallback chain.
- **Phase 5** (active): Consolidation — identification accuracy, provider resilience hardening, telemetry.
- **Phase 6** (active): Infrastructure hardening — CI/CD, Docker, security, operational tooling.
- **Phase 7** (planned): Frontend modernization, React 18, test infrastructure.

See [ROADMAP.md](../ROADMAP.md) for detailed phase definitions and delivery sequence.
