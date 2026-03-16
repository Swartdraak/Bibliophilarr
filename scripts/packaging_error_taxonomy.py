#!/usr/bin/env python3
"""Classify packaging validation logs into a simple error taxonomy."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

PATTERNS = {
    "network-timeout": re.compile(r"timeout|timed out|request timeout", re.IGNORECASE),
    "auth-failure": re.compile(r"401|unauthorized|forbidden|api key", re.IGNORECASE),
    "bind-conflict": re.compile(r"address already in use|failed to bind|socketexception", re.IGNORECASE),
    "startup-failure": re.compile(r"fatal|epic fail|hosting failed to start|non-recoverable", re.IGNORECASE),
    "dependency-or-build": re.compile(r"failed|error\s+cs\d+|npm err|yarn error|docker build", re.IGNORECASE),
}


def classify(text: str) -> dict:
    counts = {key: 0 for key in PATTERNS}
    samples = {key: [] for key in PATTERNS}

    for line in text.splitlines():
        for category, pattern in PATTERNS.items():
            if pattern.search(line):
                counts[category] += 1
                if len(samples[category]) < 5:
                    samples[category].append(line.strip())

    return {"counts": counts, "samples": samples}


def main() -> int:
    parser = argparse.ArgumentParser(description="Build packaging log taxonomy summary")
    parser.add_argument("--input-dir", type=Path, required=True, help="Directory containing log files")
    parser.add_argument("--json-out", type=Path, required=True, help="Output JSON summary")
    parser.add_argument("--md-out", type=Path, required=True, help="Output Markdown summary")
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
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
