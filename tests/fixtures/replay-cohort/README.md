# Curated Replay Cohort

Purpose:
- Provide a deterministic non-empty replay root for CI trend checks.
- Keep fixture lightweight by using extension-valid placeholder media files.

Usage:
- Run `scripts/live_provider_enrich_missing_metadata.py` with `--root tests/fixtures/replay-cohort`.
- Compare resulting report metrics against `expected-thresholds.json` using `scripts/replay_regression_guard.py`.

Notes:
- This cohort is intentionally small and stable.
- It is designed to ensure replay metrics are non-empty for accepted/unresolved trend guards.
