# Live Provider Replay Comparison (Pre/Post Runtime Wiring)

Date: 2026-03-16

## Objective

Run sampled live media enrichment replay and compare provider winner and cover outcomes before and after runtime aggregation conflict-policy wiring.

## Inputs

Pre-reference artifact:
- `_artifacts/live-provider-enrich-2026-03-15-v2/audiobooks_live_enrichment_report.json`

Post-replay sampled artifact:
- `_artifacts/live-provider-enrich-2026-03-16-runtime-policy-sample/bibliophilarr-live-sample-2026-03-16_live_enrichment_report.json`

Sample root used:
- `/tmp/bibliophilarr-live-sample-2026-03-16`
- 8 audiobook folders + 8 ebook folders (symlinked sample)

## Replay Command

```bash
python3 scripts/live_provider_enrich_missing_metadata.py \
  --root /tmp/bibliophilarr-live-sample-2026-03-16 \
  --sample-size 16 \
  --sample-seed 20260316 \
  --report-dir _artifacts/live-provider-enrich-2026-03-16-runtime-policy-sample
```

## Results Summary

Pre (2026-03-15 v2, audiobooks):
- targets: 5
- accepted: 2
- unresolved: 3
- provider winners among accepted: `openlibrary=2`
- cover proxy (accepted folders with discoverable `metadata.json` image entries): `0/2`

Post (2026-03-16 sampled replay):
- targets: 0
- accepted: 0
- unresolved: 0
- provider winners among accepted: none (no pending targets)
- cover proxy: not applicable (no accepted matches)

## Interpretation

- Sampled replay confirms no remaining missing-metadata targets in sampled set after prior enrichment and organizer convergence.
- Provider winner distribution cannot be newly re-measured in post sample because no unresolved targets remained.
- Runtime conflict-policy changes are validated by integration tests for tie-break and transient behavior; replay confirms no regression surfaced in sampled operational state.

## Known Limitation

- Current enrichment report schema now stores `selected_cover_provider` and `selected_cover_url` for accepted matches.
- Cover outcome can be analyzed directly from the replay JSON without inferring from local metadata file image entries.

## Follow-up

1. Add explicit cover winner fields to enrichment report payload (`selected_cover_provider`, `selected_cover_url`).
2. Use replay mode flags `--sample-size` and `--sample-seed` for bounded random sampling directly in script.
3. Add periodic replay report diff in CI against curated fixture data.
