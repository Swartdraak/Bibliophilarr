#!/usr/bin/env python3
"""Compare baseline and post-fix replay reports with optional DB-state metrics."""

from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Iterable, Optional


@dataclass
class ReplayMetrics:
    discovered_targets: int
    accepted: int
    unresolved: int
    identify_rate: float
    cover_success_rate: float
    provider_failures: int


@dataclass
class DbMetrics:
    duplicate_normalized_authors: int
    series_count: int
    series_book_link_count: int


def load_json(path: Path) -> Dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def _count_items(value: Any) -> int:
    if isinstance(value, list):
        return len(value)
    if isinstance(value, int):
        return value
    return int(value or 0)


def _iter_attempts(report: Dict[str, Any]) -> Iterable[Dict[str, Any]]:
    for section in ("accepted", "unresolved"):
        entries = report.get(section, [])
        if not isinstance(entries, list):
            continue
        for entry in entries:
            attempts = entry.get("attempts", []) if isinstance(entry, dict) else []
            if isinstance(attempts, list):
                for attempt in attempts:
                    if isinstance(attempt, dict):
                        yield attempt


def extract_replay_metrics(report: Dict[str, Any]) -> ReplayMetrics:
    summary = report.get("summary") if isinstance(report.get("summary"), dict) else {}

    discovered = int(summary.get("discovered_targets", summary.get("targets", report.get("targets_total", 0))) or 0)
    targets = int(summary.get("targets", discovered) or discovered)

    accepted = _count_items(report.get("accepted", summary.get("accepted", 0)))
    unresolved = _count_items(report.get("unresolved", summary.get("unresolved", 0)))

    identify_base = targets if targets > 0 else discovered
    identify_rate = (accepted / identify_base) if identify_base else 0.0

    accepted_items = report.get("accepted", []) if isinstance(report.get("accepted"), list) else []
    covers_with_url = 0
    for item in accepted_items:
        if not isinstance(item, dict):
            continue
        match = item.get("match", {}) if isinstance(item.get("match"), dict) else {}
        cover_url = match.get("selected_cover_url")
        if isinstance(cover_url, str) and cover_url.strip():
            covers_with_url += 1

    cover_success_rate = (covers_with_url / accepted) if accepted else 0.0

    provider_failures = 0
    for attempt in _iter_attempts(report):
        note = attempt.get("note")
        if isinstance(note, str) and note.strip():
            provider_failures += 1

    return ReplayMetrics(
        discovered_targets=discovered,
        accepted=accepted,
        unresolved=unresolved,
        identify_rate=identify_rate,
        cover_success_rate=cover_success_rate,
        provider_failures=provider_failures,
    )


def query_scalar(conn: sqlite3.Connection, sql: str) -> int:
    row = conn.execute(sql).fetchone()
    return int(row[0]) if row and row[0] is not None else 0


def extract_db_metrics(db_path: Path) -> DbMetrics:
    conn = sqlite3.connect(str(db_path))
    try:
        duplicate_count = query_scalar(
            conn,
            """
            SELECT COUNT(*)
            FROM (
              SELECT LOWER(TRIM("Name")) AS normalized_name, COUNT(*) AS cnt
              FROM "AuthorMetadata"
              WHERE "Name" IS NOT NULL AND TRIM("Name") != ''
              GROUP BY LOWER(TRIM("Name"))
              HAVING COUNT(*) > 1
            ) dups
            """,
        )
        series_count = query_scalar(conn, 'SELECT COUNT(*) FROM "Series"')
        series_link_count = query_scalar(conn, 'SELECT COUNT(*) FROM "SeriesBookLink"')
    finally:
        conn.close()

    return DbMetrics(
        duplicate_normalized_authors=duplicate_count,
        series_count=series_count,
        series_book_link_count=series_link_count,
    )


def delta(new_value: float, old_value: float) -> float:
    return new_value - old_value


def as_pct(value: float) -> float:
    return round(value * 100.0, 2)


def main() -> int:
    parser = argparse.ArgumentParser(description="Compare baseline vs post-fix replay metrics")
    parser.add_argument("--baseline-report", required=True, type=Path)
    parser.add_argument("--post-report", required=True, type=Path)
    parser.add_argument("--baseline-db", type=Path)
    parser.add_argument("--post-db", type=Path)
    parser.add_argument("--json-out", required=True, type=Path)
    parser.add_argument("--md-out", required=True, type=Path)
    args = parser.parse_args()

    baseline_report = load_json(args.baseline_report)
    post_report = load_json(args.post_report)

    baseline_replay = extract_replay_metrics(baseline_report)
    post_replay = extract_replay_metrics(post_report)

    baseline_db: Optional[DbMetrics] = None
    post_db: Optional[DbMetrics] = None

    if args.baseline_db and args.post_db and args.baseline_db.exists() and args.post_db.exists():
        baseline_db = extract_db_metrics(args.baseline_db)
        post_db = extract_db_metrics(args.post_db)

    result: Dict[str, Any] = {
        "generated_at": datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ"),
        "baselineReport": str(args.baseline_report),
        "postReport": str(args.post_report),
        "replay": {
            "baseline": {
                "discoveredTargets": baseline_replay.discovered_targets,
                "accepted": baseline_replay.accepted,
                "unresolved": baseline_replay.unresolved,
                "identifyRate": baseline_replay.identify_rate,
                "coverSuccessRate": baseline_replay.cover_success_rate,
                "providerFailures": baseline_replay.provider_failures,
            },
            "post": {
                "discoveredTargets": post_replay.discovered_targets,
                "accepted": post_replay.accepted,
                "unresolved": post_replay.unresolved,
                "identifyRate": post_replay.identify_rate,
                "coverSuccessRate": post_replay.cover_success_rate,
                "providerFailures": post_replay.provider_failures,
            },
            "delta": {
                "discoveredTargets": post_replay.discovered_targets - baseline_replay.discovered_targets,
                "accepted": post_replay.accepted - baseline_replay.accepted,
                "unresolved": post_replay.unresolved - baseline_replay.unresolved,
                "identifyRate": delta(post_replay.identify_rate, baseline_replay.identify_rate),
                "coverSuccessRate": delta(post_replay.cover_success_rate, baseline_replay.cover_success_rate),
                "providerFailures": post_replay.provider_failures - baseline_replay.provider_failures,
            },
        },
    }

    if baseline_db and post_db:
        result["database"] = {
            "baseline": {
                "duplicateNormalizedAuthors": baseline_db.duplicate_normalized_authors,
                "series": baseline_db.series_count,
                "seriesBookLinks": baseline_db.series_book_link_count,
            },
            "post": {
                "duplicateNormalizedAuthors": post_db.duplicate_normalized_authors,
                "series": post_db.series_count,
                "seriesBookLinks": post_db.series_book_link_count,
            },
            "delta": {
                "duplicateNormalizedAuthors": post_db.duplicate_normalized_authors - baseline_db.duplicate_normalized_authors,
                "series": post_db.series_count - baseline_db.series_count,
                "seriesBookLinks": post_db.series_book_link_count - baseline_db.series_book_link_count,
            },
        }

    args.json_out.parent.mkdir(parents=True, exist_ok=True)
    args.md_out.parent.mkdir(parents=True, exist_ok=True)

    args.json_out.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")

    lines = [
        "# Replay Baseline vs Post-Fix Comparison",
        "",
        f"Generated at: {result['generated_at']}",
        f"Baseline report: {args.baseline_report}",
        f"Post report: {args.post_report}",
        "",
        "## Replay Metrics",
        "",
        "| Metric | Baseline | Post | Delta |",
        "|---|---:|---:|---:|",
        f"| discovered targets | {baseline_replay.discovered_targets} | {post_replay.discovered_targets} | {post_replay.discovered_targets - baseline_replay.discovered_targets} |",
        f"| accepted | {baseline_replay.accepted} | {post_replay.accepted} | {post_replay.accepted - baseline_replay.accepted} |",
        f"| unresolved | {baseline_replay.unresolved} | {post_replay.unresolved} | {post_replay.unresolved - baseline_replay.unresolved} |",
        f"| identify rate | {as_pct(baseline_replay.identify_rate)}% | {as_pct(post_replay.identify_rate)}% | {as_pct(delta(post_replay.identify_rate, baseline_replay.identify_rate))}% |",
        f"| cover success rate | {as_pct(baseline_replay.cover_success_rate)}% | {as_pct(post_replay.cover_success_rate)}% | {as_pct(delta(post_replay.cover_success_rate, baseline_replay.cover_success_rate))}% |",
        f"| provider failure count | {baseline_replay.provider_failures} | {post_replay.provider_failures} | {post_replay.provider_failures - baseline_replay.provider_failures} |",
    ]

    if baseline_db and post_db:
        lines.extend(
            [
                "",
                "## Database Metrics",
                "",
                "| Metric | Baseline | Post | Delta |",
                "|---|---:|---:|---:|",
                f"| duplicate normalized authors | {baseline_db.duplicate_normalized_authors} | {post_db.duplicate_normalized_authors} | {post_db.duplicate_normalized_authors - baseline_db.duplicate_normalized_authors} |",
                f"| series rows | {baseline_db.series_count} | {post_db.series_count} | {post_db.series_count - baseline_db.series_count} |",
                f"| series book links | {baseline_db.series_book_link_count} | {post_db.series_book_link_count} | {post_db.series_book_link_count - baseline_db.series_book_link_count} |",
            ]
        )

    lines.append("")
    args.md_out.write_text("\n".join(lines), encoding="utf-8")

    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
