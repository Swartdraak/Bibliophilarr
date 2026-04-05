#!/usr/bin/env python3
"""Fail replay validation when explicit regression thresholds are exceeded."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate replay report against thresholds")
    parser.add_argument("--report", required=True, type=Path, help="Replay report JSON path")
    parser.add_argument("--thresholds", required=True, type=Path, help="Threshold JSON path")
    parser.add_argument("--summary-out", required=True, type=Path, help="Summary output JSON path")
    args = parser.parse_args()

    report = load_json(args.report)
    thresholds = load_json(args.thresholds)

    summary = report.get("summary", {}) if isinstance(report.get("summary"), dict) else {}

    discovered = int(summary.get("discovered_targets", report.get("discovered_targets", report.get("targets_total", 0))) or 0)

    accepted_raw = report.get("accepted", summary.get("accepted", 0))
    if isinstance(accepted_raw, list):
        accepted = len(accepted_raw)
    else:
        accepted = int(accepted_raw or 0)

    unresolved_raw = report.get("unresolved", summary.get("unresolved", 0))
    if isinstance(unresolved_raw, list):
        unresolved = len(unresolved_raw)
    else:
        unresolved = int(unresolved_raw or 0)

    reasons = summary.get("unresolved_reasons", {}) or {}
    no_candidates = int(reasons.get("no-candidates", 0) or 0)

    unresolved_ratio = float(unresolved / discovered) if discovered else 0.0
    no_candidate_share = float(no_candidates / unresolved) if unresolved else 0.0

    failures = []

    if discovered < int(thresholds.get("minimumDiscoveredTargets", 0)):
        failures.append(
            f"discovered targets below minimum: {discovered} < {thresholds.get('minimumDiscoveredTargets')}"
        )

    if accepted < int(thresholds.get("minAcceptedCount", 0)):
        failures.append(f"accepted below minimum: {accepted} < {thresholds.get('minAcceptedCount')}")

    max_unresolved_ratio = float(thresholds.get("maxUnresolvedRatio", 1.0))
    if unresolved_ratio > max_unresolved_ratio:
        failures.append(
            f"unresolved ratio too high: {unresolved_ratio:.4f} > {max_unresolved_ratio:.4f}"
        )

    max_no_candidate_share = float(thresholds.get("maxNoCandidateShare", 1.0))
    if no_candidate_share > max_no_candidate_share:
        failures.append(
            f"no-candidates share too high: {no_candidate_share:.4f} > {max_no_candidate_share:.4f}"
        )

    summary = {
        "status": "failed" if failures else "passed",
        "discoveredTargets": discovered,
        "accepted": accepted,
        "unresolved": unresolved,
        "unresolvedRatio": unresolved_ratio,
        "noCandidates": no_candidates,
        "noCandidateShare": no_candidate_share,
        "failures": failures,
    }

    args.summary_out.parent.mkdir(parents=True, exist_ok=True)
    with args.summary_out.open("w", encoding="utf-8") as handle:
        json.dump(summary, handle, indent=2)

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1

    print("Replay thresholds passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
