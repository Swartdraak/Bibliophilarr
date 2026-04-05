#!/usr/bin/env python3
"""Validate series persistence tables for release gating."""

from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List


def table_exists(conn: sqlite3.Connection, name: str) -> bool:
    cursor = conn.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name = ? LIMIT 1",
        (name,),
    )
    return cursor.fetchone() is not None


def scalar(conn: sqlite3.Connection, query: str) -> int:
    cursor = conn.execute(query)
    row = cursor.fetchone()
    return int(row[0]) if row and row[0] is not None else 0


def compute_duplicate_author_count(conn: sqlite3.Connection) -> int:
    if not table_exists(conn, "AuthorMetadata"):
        return 0

    query = """
    SELECT COUNT(*)
    FROM (
      SELECT LOWER(TRIM("Name")) AS normalized_name, COUNT(*) AS cnt
      FROM "AuthorMetadata"
      WHERE "Name" IS NOT NULL AND TRIM("Name") != ''
      GROUP BY LOWER(TRIM("Name"))
      HAVING COUNT(*) > 1
    ) dups
    """
    return scalar(conn, query)


def build_report(conn: sqlite3.Connection, min_series: int, min_series_links: int, max_duplicate_authors: int) -> Dict[str, object]:
    missing_tables: List[str] = []

    for table in ("Series", "SeriesBookLink", "AuthorMetadata"):
        if not table_exists(conn, table):
            missing_tables.append(table)

    if missing_tables:
        return {
            "status": "failed",
            "verdict": "FAIL",
            "detail": f"Missing required tables: {', '.join(missing_tables)}",
            "metrics": {},
            "thresholds": {
                "minSeries": min_series,
                "minSeriesBookLinks": min_series_links,
                "maxDuplicateAuthors": max_duplicate_authors,
            },
            "checks": [],
        }

    series_count = scalar(conn, 'SELECT COUNT(*) FROM "Series"')
    series_link_count = scalar(conn, 'SELECT COUNT(*) FROM "SeriesBookLink"')
    duplicate_author_count = compute_duplicate_author_count(conn)

    checks = [
        {
            "name": "Series rows",
            "value": series_count,
            "threshold": min_series,
            "passed": series_count >= min_series,
            "detail": f"{series_count} >= {min_series}",
        },
        {
            "name": "SeriesBookLink rows",
            "value": series_link_count,
            "threshold": min_series_links,
            "passed": series_link_count >= min_series_links,
            "detail": f"{series_link_count} >= {min_series_links}",
        },
        {
            "name": "Duplicate normalized authors",
            "value": duplicate_author_count,
            "threshold": max_duplicate_authors,
            "passed": duplicate_author_count <= max_duplicate_authors,
            "detail": f"{duplicate_author_count} <= {max_duplicate_authors}",
        },
    ]

    passed = all(check["passed"] for check in checks)

    return {
        "status": "passed" if passed else "failed",
        "verdict": "PASS" if passed else "FAIL",
        "detail": "Series persistence gate passed" if passed else "Series persistence gate failed",
        "metrics": {
            "series": series_count,
            "seriesBookLinks": series_link_count,
            "duplicateNormalizedAuthors": duplicate_author_count,
        },
        "thresholds": {
            "minSeries": min_series,
            "minSeriesBookLinks": min_series_links,
            "maxDuplicateAuthors": max_duplicate_authors,
        },
        "checks": checks,
    }


def write_markdown(path: Path, db_path: Path, report: Dict[str, object]) -> None:
    lines = [
        "# Series Persistence Snapshot",
        "",
        f"Generated at: {datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%SZ')}",
        f"Database: {db_path}",
        f"Series persistence verdict: {report['verdict']}",
        f"Detail: {report['detail']}",
        "",
        "| Check | Value | Threshold | Passed | Detail |",
        "|---|---:|---:|---|---|",
    ]

    for check in report.get("checks", []):
        lines.append(
            f"| {check['name']} | {check['value']} | {check['threshold']} | {str(check['passed']).lower()} | {check['detail']} |"
        )

    lines.append("")
    path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate series persistence for release entry")
    parser.add_argument("--db-path", required=True, type=Path, help="Path to Bibliophilarr SQLite database")
    parser.add_argument("--min-series", type=int, default=1)
    parser.add_argument("--min-series-links", type=int, default=1)
    parser.add_argument("--max-duplicate-authors", type=int, default=0)
    parser.add_argument("--json-out", required=True, type=Path)
    parser.add_argument("--md-out", required=True, type=Path)
    args = parser.parse_args()

    if not args.db_path.exists():
        print(f"FAIL: database path not found: {args.db_path}")
        return 1

    conn = sqlite3.connect(str(args.db_path))
    try:
        report = build_report(conn, args.min_series, args.min_series_links, args.max_duplicate_authors)
    finally:
        conn.close()

    payload = {
        "generated_at": datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ"),
        "database": str(args.db_path),
        **report,
    }

    args.json_out.parent.mkdir(parents=True, exist_ok=True)
    args.md_out.parent.mkdir(parents=True, exist_ok=True)

    args.json_out.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    write_markdown(args.md_out, args.db_path, report)

    print(json.dumps(payload, indent=2))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
