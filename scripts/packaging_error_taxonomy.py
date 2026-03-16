#!/usr/bin/env python3
"""Classify packaging validation logs into a simple error taxonomy."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

SIGNAL_PATTERN = re.compile(
    r"error|failed|failure|exception|timeout|timed out|fatal|forbidden|unauthorized|bind",
    re.IGNORECASE,
)

PATTERNS = {
    "network-timeout": re.compile(r"timeout|timed out|request timeout", re.IGNORECASE),
    "auth-failure": re.compile(r"401|unauthorized|forbidden|api key", re.IGNORECASE),
    "bind-conflict": re.compile(r"address already in use|failed to bind|socketexception", re.IGNORECASE),
    "startup-failure": re.compile(r"fatal|epic fail|hosting failed to start|non-recoverable", re.IGNORECASE),
    "dependency-or-build": re.compile(r"failed|error\s+cs\d+|npm err|yarn error|docker build", re.IGNORECASE),
}


def classify(text: str) -> dict:
    counts = {key: 0 for key in PATTERNS}
    counts["unknown"] = 0
    samples = {key: [] for key in PATTERNS}
    samples["unknown"] = []

    for line in text.splitlines():
        normalized = line.strip()
        if normalized.startswith("--- FILE:"):
            continue

        matched = False

        for category, pattern in PATTERNS.items():
            if pattern.search(normalized):
                counts[category] += 1
                if len(samples[category]) < 5:
                    samples[category].append(normalized)
                matched = True
                break

        if not matched and SIGNAL_PATTERN.search(normalized):
            counts["unknown"] += 1
            if len(samples["unknown"]) < 5:
                samples["unknown"].append(normalized)

    return {"counts": counts, "samples": samples}


def main() -> int:
    parser = argparse.ArgumentParser(description="Build packaging log taxonomy summary")
    parser.add_argument("--input-dir", type=Path, required=True, help="Directory containing log files")
    parser.add_argument("--json-out", type=Path, required=True, help="Output JSON summary")
    parser.add_argument("--md-out", type=Path, required=True, help="Output Markdown summary")
    parser.add_argument(
        "--max-unknown-share",
        type=float,
        default=1.0,
        help="Fail when unknown signal share exceeds this threshold (0-1)",
    )
    args = parser.parse_args()

    logs = []
    for path in sorted(args.input_dir.rglob("*")):
        if path.is_file() and path.suffix.lower() in {".log", ".txt"}:
            logs.append(path)

    joined = []
    for log in logs:
        joined.append(f"--- FILE: {log} ---")
        joined.append(log.read_text(encoding="utf-8", errors="replace"))

    summary = classify("\n".join(joined))
    summary["logFiles"] = [str(p) for p in logs]

    total_signals = sum(summary["counts"].values())
    unknown_count = summary["counts"]["unknown"]
    unknown_share = (unknown_count / total_signals) if total_signals else 0.0
    threshold_exceeded = unknown_share > args.max_unknown_share

    summary["totalSignals"] = total_signals
    summary["unknownShare"] = unknown_share
    summary["maxUnknownShare"] = args.max_unknown_share
    summary["unknownShareThresholdExceeded"] = threshold_exceeded

    args.json_out.parent.mkdir(parents=True, exist_ok=True)
    args.md_out.parent.mkdir(parents=True, exist_ok=True)

    args.json_out.write_text(json.dumps(summary, indent=2), encoding="utf-8")

    md_lines = [
        "# Packaging Validation Error Taxonomy",
        "",
        "## Counts",
    ]

    for category, count in summary["counts"].items():
        md_lines.append(f"- {category}: {count}")

    md_lines.append("")
    md_lines.append("## Quality")
    md_lines.append("")
    md_lines.append(f"- total-signals: {total_signals}")
    md_lines.append(f"- unknown-share: {unknown_share:.4f}")
    md_lines.append(f"- max-unknown-share: {args.max_unknown_share:.4f}")
    md_lines.append(f"- threshold-exceeded: {str(threshold_exceeded).lower()}")
    md_lines.append("")
    md_lines.append("## Samples")

    for category, lines in summary["samples"].items():
        md_lines.append("")
        md_lines.append(f"### {category}")
        if not lines:
            md_lines.append("- none")
            continue
        for line in lines:
            md_lines.append(f"- {line}")

    args.md_out.write_text("\n".join(md_lines) + "\n", encoding="utf-8")

    if threshold_exceeded:
        print(
            "FAIL: unknown taxonomy share %.4f exceeds max %.4f"
            % (unknown_share, args.max_unknown_share)
        )
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
