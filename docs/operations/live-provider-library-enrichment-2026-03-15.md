# Live Provider Library Enrichment

Date: 2026-03-15

## Objective

Use live provider responses during iterative organization to validate how well new files without `metadata.json` or `.opf` can be identified, enriched, and normalized.

## Provider and Method

Providers used:
- Open Library search API
- Inventaire live search API as the next fallback provider

Installed tooling used by the enrichment pass:
- `ffprobe` via `ffmpeg`
- `mutagen` Python library

Query order matched application-style behavior and iterative fallback, then expanded:
1. Open Library ISBN lookup when ISBN is available
2. Open Library primary query: `title + author`
3. Open Library alias-expanded primary query
4. Open Library title-only query using subtitle/series-stripped title variants
5. Open Library author-only query
6. Inventaire primary query
7. Inventaire alias-expanded primary query
8. Inventaire title-only query using subtitle/series-stripped title variants
9. Inventaire author-only query

Local identity source precedence:
1. embedded audiobook tags via `ffprobe`
2. embedded audiobook tags via `mutagen`
3. filename parsing
4. folder/path fallback

Acceptance policy:
- only high-confidence matches were written automatically to `metadata.json`
- lower-confidence or ambiguous matches were reported but not applied

Script used:
- `scripts/live_provider_enrich_missing_metadata.py`

## Scan Scope

After the prior local-metadata organization pass:
- `/media/audiobooks`: 8 folders missing both `metadata.json` and `.opf`
- `/media/ebooks`: 0 folders missing both `metadata.json` and `.opf` outside `_dupes`

## Results

### Audiobooks

Initial live-enrichment pass:
- Targets: 8
- Accepted live matches: 3
- Unresolved: 5

Accepted in initial pass:
- `Kirill Klevanski/Dragon Heart` → `Dragon Heart / Kirill Klevanski`
- `Frank Herbert/Dune` → `Dune / Frank Herbert`
- `Suzanne Collins/The Hunger Games` → `The Hunger Games / Suzanne Collins`

Enhanced pass after adding fallback provider, alias normalization, subtitle stripping, and embedded tag extraction:
- Targets: 5
- Accepted live matches: 2
- Unresolved: 3

Accepted in enhanced pass:
- `McKenzie Hunter/Sky Brooks` → `Moon Cursed / McKenzie Hunter`
- `Terry Mancour/Spellmonger` → `Spellmonger: Book 1 Of The Spellmonger Series (Volume 1) / T. L. Mancour`

Total accepted across both iterative passes:
- 5 accepted
- 3 unresolved

Post-enrichment organizer verification:
- `/media/audiobooks` fully reconverged with `0` remaining proposed actions after the final provider-assisted rename pass

Reports:
- `_artifacts/live-provider-enrich-2026-03-15/audiobooks_live_enrichment_report.json`
- `_artifacts/live-provider-enrich-2026-03-15/audiobooks_live_enrichment_report.md`
- `_artifacts/live-provider-enrich-2026-03-15-v2/audiobooks_live_enrichment_report.json`
- `_artifacts/live-provider-enrich-2026-03-15-v2/audiobooks_live_enrichment_report.md`
- `_artifacts/media-organize-live-provider-2026-03-15-v3-post/audiobooks_organize_summary.json`

### Ebooks

Targets: 0
Accepted: 0
Unresolved: 0

Report:
- `_artifacts/live-provider-enrich-2026-03-15/ebooks_live_enrichment_report.json`
- `_artifacts/live-provider-enrich-2026-03-15/ebooks_live_enrichment_report.md`

## Unresolved Cases and What They Show

### 1. `Eric Vall/Summoner`

Observed behavior:
- no useful Open Library hit for primary query
- title-only fell into generic/incorrect matches

Implication:
- provider coverage gap or weak catalog presence
- Open Library plus Inventaire is still not enough for this title

### 2. `Eric Ugland/The Grim Guys`

Observed behavior:
- primary query produced no result
- author-only found a different Eric Ugland work

Implication:
- provider coverage gap remains even with Inventaire fallback
- additional provider(s) are required for robust audiobook ingestion

### 3. `Robert Blaise/1% Lifesteal`

Observed behavior:
- provider returned effectively unrelated results
- likely too new, niche, or absent from provider catalog

Implication:
- fallback provider + stronger local extraction is still needed
- numeric/symbol-heavy titles need dedicated normalization handling

## Identified System Gaps

### Query construction gaps
- series/world names can still be mistaken for a book title when embedded tags contain collection labels instead of work labels
- numeric/symbol-heavy titles remain hard to match reliably

### Local identification gaps
- current path and filename inference alone is not enough for some LitRPG and bundled audiobook titles
- embedded tag extraction now works and materially improved identification quality
- some embedded tags still reflect collection/world context rather than canonical work title

### Provider/fallback gaps
- Open Library and Inventaire together improved results but are still insufficient for full audiobook coverage
- at least one additional fallback source is still needed for robust ingestion

## Recommended Next Steps

1. Add a third provider fallback for unresolved works such as `Summoner`, `The Grim Guys`, and `1% Lifesteal`.
2. Add a title classifier that distinguishes likely series/world labels from canonical work titles when tags are misleading.
3. Add special normalization rules for numeric/symbol-heavy titles.
4. Persist alias maps and title normalization rules in a reusable config instead of keeping them script-local.
5. Keep blind auto-apply restricted to high-confidence matches only.
