#!/usr/bin/env python3
"""Enforce release-entry evidence gates from committed operations snapshots."""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from datetime import date, datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional, Tuple

DATE_FILE_RE = re.compile(r"^(\d{4}-\d{2}-\d{2})(?:-.*)?\.md$")


@dataclass
class GateResult:
    name: str
    snapshot_path: Optional[str]
    snapshot_date: Optional[str]
    days_old: Optional[int]
    verdict: str
    passed: bool
    detail: str

    def to_dict(self) -> Dict[str, object]:
        return {
            "name": self.name,
            "snapshot_path": self.snapshot_path,
            "snapshot_date": self.snapshot_date,
            "days_old": self.days_old,
            "verdict": self.verdict,
            "passed": self.passed,
            "detail": self.detail,
        }


def parse_date(value: str) -> date:
    return datetime.strptime(value, "%Y-%m-%d").date()


def latest_snapshot(directory: Path) -> Tuple[Optional[Path], Optional[date]]:
    if not directory.exists():
        return None, None

    best: Tuple[Optional[Path], Optional[date]] = (None, None)
    for entry in directory.iterdir():
        if not entry.is_file():
            continue

        match = DATE_FILE_RE.match(entry.name)
        if not match:
            continue

        snapshot_day = parse_date(match.group(1))
        if best[1] is None or snapshot_day > best[1]:
            best = (entry, snapshot_day)

    return best


def extract_verdict(contents: str, marker: str) -> Optional[str]:
    marker_lower = marker.lower()
    for line in contents.splitlines():
        if marker_lower not in line.lower():
            continue

        _, _, tail = line.partition(":")
        value = tail.strip() if tail else line.strip()
        if value:
            return value

    return None


def evaluate_gate(
    name: str,
    directory: Path,
    marker: str,
    max_age_days: int,
    accepted_terms: List[str],
) -> GateResult:
    snapshot, snapshot_day = latest_snapshot(directory)
    if snapshot is None or snapshot_day is None:
        return GateResult(
            name=name,
            snapshot_path=None,
            snapshot_date=None,
            days_old=None,
            verdict="missing",
            passed=False,
            detail=f"No dated snapshot found in {directory}",
        )

    contents = snapshot.read_text(encoding="utf-8")
    verdict = extract_verdict(contents, marker)
    if verdict is None:
        return GateResult(
            name=name,
            snapshot_path=str(snapshot),
            snapshot_date=snapshot_day.isoformat(),
            days_old=None,
            verdict="missing-verdict",
            passed=False,
            detail=f"Snapshot does not include required marker '{marker}'",
        )

    today = datetime.now(timezone.utc).date()
    days_old = (today - snapshot_day).days
    if days_old > max_age_days:
        return GateResult(
            name=name,
            snapshot_path=str(snapshot),
            snapshot_date=snapshot_day.isoformat(),
            days_old=days_old,
            verdict=verdict,
            passed=False,
            detail=f"Snapshot is stale ({days_old} days old, max {max_age_days})",
        )

    verdict_lower = verdict.lower()
    passed = any(term.lower() in verdict_lower for term in accepted_terms)

    return GateResult(
        name=name,
        snapshot_path=str(snapshot),
        snapshot_date=snapshot_day.isoformat(),
        days_old=days_old,
        verdict=verdict,
        passed=passed,
        detail="pass" if passed else "verdict is not a passing state",
    )


def evaluate_forbidden_symbol_gate(
    name: str,
    directories: List[Path],
    forbidden_symbol: str,
    allowlist_paths: List[str],
) -> GateResult:
    offenders: List[str] = []
    allowlist_normalized = {Path(path).as_posix().lower() for path in allowlist_paths}

    for directory in directories:
        if not directory.exists():
            continue

        for file_path in directory.rglob("*"):
            if not file_path.is_file():
                continue

            if file_path.suffix.lower() not in {".cs", ".js", ".jsx", ".ts", ".tsx", ".json"}:
                continue

            normalized = file_path.as_posix().lower()
            if normalized in allowlist_normalized:
                continue

            try:
                contents = file_path.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                continue

            if forbidden_symbol.lower() in contents.lower():
                offenders.append(str(file_path))

    if offenders:
        return GateResult(
            name=name,
            snapshot_path=None,
            snapshot_date=None,
            days_old=None,
            verdict="forbidden-symbol-detected",
            passed=False,
            detail=f"Found '{forbidden_symbol}' in {len(offenders)} files (first: {offenders[0]})",
        )

    return GateResult(
        name=name,
        snapshot_path=None,
        snapshot_date=None,
        days_old=None,
        verdict="pass",
        passed=True,
        detail=f"No '{forbidden_symbol}' symbols found in guarded source paths",
    )


def write_markdown(path: Path, report: Dict[str, object]) -> None:
    lines = [
        "# Release Entry Gate",
        "",
        f"Generated at: {report['generated_at']}",
        "",
        "| Gate | Snapshot | Date | Age (days) | Verdict | Passed | Detail |",
        "|---|---|---|---:|---|---|---|",
    ]

    for gate in report["gates"]:
        lines.append(
            "| {name} | {snapshot} | {date} | {age} | {verdict} | {passed} | {detail} |".format(
                name=gate["name"],
                snapshot=gate["snapshot_path"] or "n/a",
                date=gate["snapshot_date"] or "n/a",
                age=gate["days_old"] if gate["days_old"] is not None else "n/a",
                verdict=gate["verdict"],
                passed=str(gate["passed"]).lower(),
                detail=gate["detail"],
            )
        )

    lines.extend([
        "",
        f"Overall: {'PASS' if report['ok'] else 'FAIL'}",
        "",
    ])

    path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate release-entry evidence gates")
    parser.add_argument(
        "--dry-run-dir",
        default="docs/operations/metadata-dry-run-snapshots",
        help="Directory containing dated metadata dry-run snapshots",
    )
    parser.add_argument(
        "--telemetry-dir",
        default="docs/operations/metadata-telemetry-checkpoints",
        help="Directory containing dated telemetry checkpoint snapshots",
    )
    parser.add_argument(
        "--install-dir",
        default="docs/operations/install-test-snapshots",
        help="Directory containing dated install matrix snapshots",
    )
    parser.add_argument(
        "--series-dir",
        default="docs/operations/series-persistence-snapshots",
        help="Directory containing dated series persistence snapshots",
    )
    parser.add_argument("--max-age-days", type=int, default=7)
    parser.add_argument(
        "--symbol-scan-dirs",
        nargs="+",
        default=["src/NzbDrone.Core", "src/Bibliophilarr.Api.V1", "frontend/src"],
        help="Source directories scanned for forbidden legacy symbols",
    )
    parser.add_argument(
        "--forbidden-symbol",
        default="goodreads",
        help="Case-insensitive legacy symbol that must not appear in guarded source paths",
    )
    parser.add_argument(
        "--symbol-allowlist",
        nargs="+",
        default=[
            "src/NzbDrone.Core/Parser/Model/ImportListItemInfo.cs",
            "src/NzbDrone.Core/Parser/Model/ParsedTrackInfo.cs",
        ],
        help="Files allowed to contain legacy compatibility symbols",
    )
    parser.add_argument("--md-out", required=True)
    parser.add_argument("--json-out", required=True)
    args = parser.parse_args()

    gates = [
        evaluate_gate(
            name="Metadata dry-run",
            directory=Path(args.dry_run_dir),
            marker="Verdict",
            max_age_days=args.max_age_days,
            accepted_terms=["pass"],
        ),
        evaluate_gate(
            name="Metadata telemetry thresholds",
            directory=Path(args.telemetry_dir),
            marker="Overall threshold verdict",
            max_age_days=args.max_age_days,
            accepted_terms=["pass"],
        ),
        evaluate_gate(
            name="Install matrix",
            directory=Path(args.install_dir),
            marker="Overall matrix verdict",
            max_age_days=args.max_age_days,
            accepted_terms=["pass"],
        ),
        evaluate_gate(
            name="Series persistence",
            directory=Path(args.series_dir),
            marker="Series persistence verdict",
            max_age_days=args.max_age_days,
            accepted_terms=["pass"],
        ),
        evaluate_forbidden_symbol_gate(
            name="Legacy symbol guard",
            directories=[Path(path) for path in args.symbol_scan_dirs],
            forbidden_symbol=args.forbidden_symbol,
            allowlist_paths=args.symbol_allowlist,
        ),
    ]

    report = {
        "generated_at": datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ"),
        "max_age_days": args.max_age_days,
        "gates": [gate.to_dict() for gate in gates],
        "ok": all(gate.passed for gate in gates),
    }

    json_path = Path(args.json_out)
    md_path = Path(args.md_out)
    json_path.parent.mkdir(parents=True, exist_ok=True)
    md_path.parent.mkdir(parents=True, exist_ok=True)

    json_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    write_markdown(md_path, report)

    print(json.dumps(report, indent=2))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
