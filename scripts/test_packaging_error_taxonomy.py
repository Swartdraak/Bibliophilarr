#!/usr/bin/env python3
"""Deterministic fixture test for packaging error taxonomy classification."""

from __future__ import annotations

import importlib.util
import json
import subprocess
from pathlib import Path


def load_module(path: Path):
    spec = importlib.util.spec_from_file_location("packaging_error_taxonomy", path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


def main() -> int:
    repo_root = Path(__file__).resolve().parents[1]
    script_path = repo_root / "scripts" / "packaging_error_taxonomy.py"
    fixture_root = repo_root / "tests" / "fixtures" / "packaging-taxonomy"
    expected = json.loads((fixture_root / "expected-taxonomy-counts.json").read_text(encoding="utf-8"))

    module = load_module(script_path)
    joined = []
    for path in sorted((fixture_root / "logs").glob("*.log")):
        joined.append(path.read_text(encoding="utf-8"))

    summary = module.classify("\n".join(joined))

    for category, expected_count in expected["counts"].items():
        actual = summary["counts"].get(category)
        if actual != expected_count:
            raise AssertionError(f"count mismatch for {category}: {actual} != {expected_count}")

    total_signals = sum(summary["counts"].values())
    unknown_share = summary["counts"]["unknown"] / total_signals
    if unknown_share > expected["maxUnknownShare"]:
        raise AssertionError(
            f"unknown share exceeded: {unknown_share:.4f} > {expected['maxUnknownShare']:.4f}"
        )

    out_dir = repo_root / "_artifacts" / "taxonomy-fixture-test"
    out_dir.mkdir(parents=True, exist_ok=True)
    json_out = out_dir / "taxonomy.json"
    md_out = out_dir / "taxonomy.md"

    subprocess.run(
        [
            "python3",
            str(script_path),
            "--input-dir",
            str(fixture_root / "logs"),
            "--max-unknown-share",
            str(expected["maxUnknownShare"]),
            "--json-out",
            str(json_out),
            "--md-out",
            str(md_out),
        ],
        check=True,
    )

    generated = json.loads(json_out.read_text(encoding="utf-8"))
    for category, expected_count in expected["counts"].items():
        actual = generated["counts"].get(category)
        if actual != expected_count:
            raise AssertionError(f"CLI count mismatch for {category}: {actual} != {expected_count}")

    print("packaging taxonomy fixture test passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())