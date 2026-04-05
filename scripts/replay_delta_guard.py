#!/usr/bin/env python3
"""Fail CI when baseline-vs-post replay deltas regress beyond allowed thresholds."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List


def load_json(path: Path) -> Dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate replay comparison deltas")
    parser.add_argument("--comparison", required=True, type=Path, help="Path to replay_comparison JSON output")
    parser.add_argument("--thresholds", required=True, type=Path, help="Delta threshold JSON path")
    parser.add_argument("--summary-out", required=True, type=Path, help="Summary output JSON path")
    args = parser.parse_args()

    comparison = load_json(args.comparison)
    thresholds = load_json(args.thresholds)

    replay_delta = comparison.get("replay", {}).get("delta", {})
    db_delta = comparison.get("database", {}).get("delta", {})

    failures: List[str] = []

    min_identify_rate_delta = float(thresholds.get("minIdentifyRateDelta", 0.0))
    identify_rate_delta = float(replay_delta.get("identifyRate", 0.0))
    if identify_rate_delta < min_identify_rate_delta:
        failures.append(
            f"identify rate delta below minimum: {identify_rate_delta:.4f} < {min_identify_rate_delta:.4f}"
        )

    min_cover_success_delta = float(thresholds.get("minCoverSuccessRateDelta", 0.0))
    cover_success_delta = float(replay_delta.get("coverSuccessRate", 0.0))
    if cover_success_delta < min_cover_success_delta:
        failures.append(
            f"cover success delta below minimum: {cover_success_delta:.4f} < {min_cover_success_delta:.4f}"
        )

    max_provider_failure_delta = int(thresholds.get("maxProviderFailuresDelta", 0))
    provider_failure_delta = int(replay_delta.get("providerFailures", 0))
    if provider_failure_delta > max_provider_failure_delta:
        failures.append(
            f"provider failures delta exceeded: {provider_failure_delta} > {max_provider_failure_delta}"
        )

    max_unresolved_delta = int(thresholds.get("maxUnresolvedDelta", 0))
    unresolved_delta = int(replay_delta.get("unresolved", 0))
    if unresolved_delta > max_unresolved_delta:
        failures.append(
            f"unresolved delta exceeded: {unresolved_delta} > {max_unresolved_delta}"
        )

    min_series_delta = int(thresholds.get("minSeriesDelta", 0))
    series_delta = int(db_delta.get("series", 0)) if isinstance(db_delta, dict) else 0
    if series_delta < min_series_delta:
        failures.append(f"series delta below minimum: {series_delta} < {min_series_delta}")

    min_series_links_delta = int(thresholds.get("minSeriesBookLinksDelta", 0))
    series_links_delta = int(db_delta.get("seriesBookLinks", 0)) if isinstance(db_delta, dict) else 0
    if series_links_delta < min_series_links_delta:
        failures.append(
            f"series book links delta below minimum: {series_links_delta} < {min_series_links_delta}"
        )

    max_duplicate_authors_delta = int(thresholds.get("maxDuplicateAuthorsDelta", 0))
    duplicate_authors_delta = int(db_delta.get("duplicateNormalizedAuthors", 0)) if isinstance(db_delta, dict) else 0
    if duplicate_authors_delta > max_duplicate_authors_delta:
        failures.append(
            f"duplicate normalized authors delta exceeded: {duplicate_authors_delta} > {max_duplicate_authors_delta}"
        )

    summary = {
        "status": "failed" if failures else "passed",
        "replayDelta": replay_delta,
        "databaseDelta": db_delta,
        "failures": failures,
    }

    args.summary_out.parent.mkdir(parents=True, exist_ok=True)
    with args.summary_out.open("w", encoding="utf-8") as handle:
        json.dump(summary, handle, indent=2)
        handle.write("\n")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1

    print("Replay delta thresholds passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
