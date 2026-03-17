#!/usr/bin/env python3
"""
Provider metadata pull test harness.

Purpose:
- Randomly sample ebook/audiobook files from /media.
- Build query strings using the same q-format pattern as BookInfoProxy.SearchForNewBook:
  q = title.ToLower().Trim(); if author != null: q += " " + author
- Exercise Open Library pull behavior with fallback strategy and identifier probing.
- Produce machine + human-readable reports under _artifacts/provider-pull-test/.
"""

from __future__ import annotations

import argparse
import json
import os
import random
import re
import time
from collections import Counter, defaultdict
from dataclasses import dataclass, asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple
from urllib.parse import quote_plus
from urllib.request import urlopen
from urllib.error import URLError, HTTPError

MEDIA_EXTENSIONS = {
    ".epub",
    ".mobi",
    ".azw3",
    ".pdf",
    ".mp3",
    ".m4b",
    ".flac",
    ".aac",
    ".ogg",
}

NOISE_TOKENS = {
    "unabridged",
    "abridged",
    "audiobook",
    "audio",
    "ebook",
    "retail",
    "scan",
    "drmfree",
    "drm-free",
    "vbr",
    "kbps",
    "mp3",
    "m4b",
    "epub",
    "azw3",
    "pdf",
}

ISBN13_RE = re.compile(r"(?<!\d)(97[89][\d-]{10,16})(?!\d)", re.IGNORECASE)
ISBN10_RE = re.compile(r"(?<![\dXx])([\d-]{9}[\dXx])(?![\dXx])", re.IGNORECASE)
ASIN_RE = re.compile(r"\b(B0[0-9A-Z]{8})\b", re.IGNORECASE)
YEAR_RE = re.compile(r"\b(19\d{2}|20\d{2}|2100)\b")
BRACKETED_RE = re.compile(r"\[[^\]]*\]|\([^\)]*\)")


@dataclass
class Candidate:
    path: str
    file_name: str
    ext: str
    title_guess: Optional[str]
    author_guess: Optional[str]
    isbn_guess: Optional[str]
    asin_guess: Optional[str]
    app_query_primary: Optional[str]
    app_query_title_only: Optional[str]
    app_query_author_only: Optional[str]


@dataclass
class AttemptResult:
    strategy: str
    success: bool
    num_found: Optional[int]
    latency_ms: int
    error: Optional[str] = None


def clean_component(value: str) -> str:
    value = value.replace("_", " ").replace(".", " ").replace("-", " ")
    value = BRACKETED_RE.sub(" ", value)
    value = YEAR_RE.sub(" ", value)

    tokens = []
    for token in value.split():
        lowered = token.strip().lower()
        if lowered in NOISE_TOKENS:
            continue
        if lowered.isdigit() and len(lowered) <= 2:
            continue
        tokens.append(token)

    value = " ".join(tokens)
    value = re.sub(r"\s+", " ", value).strip(" -")
    return value


def normalize_isbn(raw: str) -> Optional[str]:
    digits = re.sub(r"[^0-9Xx]", "", raw).upper()
    if len(digits) == 13 and digits.isdigit():
        return digits
    if len(digits) == 10 and re.match(r"^[0-9]{9}[0-9X]$", digits):
        return digits
    return None


def extract_identifiers(text: str) -> Tuple[Optional[str], Optional[str]]:
    isbn = None
    asin = None

    m13 = ISBN13_RE.search(text)
    if m13:
        isbn = normalize_isbn(m13.group(1))

    if not isbn:
        m10 = ISBN10_RE.search(text)
        if m10:
            isbn = normalize_isbn(m10.group(1))

    ma = ASIN_RE.search(text)
    if ma:
        asin = ma.group(1).upper()

    return isbn, asin


def parse_title_author(file_path: str) -> Tuple[Optional[str], Optional[str], Optional[str], Optional[str]]:
    file_name = os.path.basename(file_path)
    stem = os.path.splitext(file_name)[0]
    isbn, asin = extract_identifiers(stem)

    cleaned = clean_component(stem)

    if not cleaned:
        return None, None, isbn, asin

    # Prefer Author - Title if present.
    if " - " in stem:
        parts = [clean_component(p) for p in stem.split(" - ") if clean_component(p)]
        if len(parts) >= 2:
            author = parts[0]
            title = " - ".join(parts[1:])
            return title or None, author or None, isbn, asin

    # Handle "Title by Author"
    by_match = re.search(r"\bby\b", cleaned, flags=re.IGNORECASE)
    if by_match:
        idx = by_match.start()
        title = cleaned[:idx].strip(" -")
        author = cleaned[by_match.end():].strip(" -")
        if title and author:
            return title, author, isbn, asin

    # If no delimiter, treat entire stem as title.
    return cleaned, None, isbn, asin


def build_app_query(title: str, author: Optional[str]) -> str:
    # Matches BookInfoProxy.SearchForNewBook q-construction pattern.
    q = title.lower().strip()
    if author is not None and author.strip():
        q += " " + author.strip()
    return q


def http_json(url: str, timeout: int = 15) -> Dict[str, Any]:
    with urlopen(url, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8", errors="replace"))


def ol_search_q(query: str) -> AttemptResult:
    encoded = quote_plus(query)
    url = f"https://openlibrary.org/search.json?q={encoded}&limit=5"

    start = time.time()
    try:
        data = http_json(url)
        latency_ms = int((time.time() - start) * 1000)
        num_found = int(data.get("numFound") or 0)
        return AttemptResult("q", num_found > 0, num_found, latency_ms)
    except (HTTPError, URLError, TimeoutError, ValueError) as exc:
        latency_ms = int((time.time() - start) * 1000)
        return AttemptResult("q", False, None, latency_ms, str(exc))


def ol_search_isbn(isbn: str) -> AttemptResult:
    url = f"https://openlibrary.org/isbn/{quote_plus(isbn)}.json"

    start = time.time()
    try:
        data = http_json(url)
        latency_ms = int((time.time() - start) * 1000)
        success = isinstance(data, dict) and bool(data)
        return AttemptResult("isbn", success, 1 if success else 0, latency_ms)
    except HTTPError as exc:
        latency_ms = int((time.time() - start) * 1000)
        if exc.code == 404:
            return AttemptResult("isbn", False, 0, latency_ms)
        return AttemptResult("isbn", False, None, latency_ms, f"HTTP {exc.code}")
    except (URLError, TimeoutError, ValueError) as exc:
        latency_ms = int((time.time() - start) * 1000)
        return AttemptResult("isbn", False, None, latency_ms, str(exc))


def sample_media_files(media_root: str, sample_size: int, seed: int) -> List[str]:
    all_files: List[str] = []

    for root, _, files in os.walk(media_root):
        for file_name in files:
            ext = os.path.splitext(file_name)[1].lower()
            if ext in MEDIA_EXTENSIONS:
                all_files.append(os.path.join(root, file_name))

    if len(all_files) <= sample_size:
        return sorted(all_files)

    rng = random.Random(seed)
    return sorted(rng.sample(all_files, sample_size))


def run(args: argparse.Namespace) -> int:
    sampled = sample_media_files(args.media_root, args.sample_size, args.seed)

    if not sampled:
        raise RuntimeError(f"No supported media files found under {args.media_root}")

    cases: List[Dict[str, Any]] = []
    strategy_success = Counter()
    strategy_attempts = Counter()
    gap_counter = Counter()
    extension_counter = Counter()

    for path in sampled:
        ext = os.path.splitext(path)[1].lower()
        extension_counter[ext] += 1

        title, author, isbn, asin = parse_title_author(path)

        primary = build_app_query(title, author) if title else None
        title_only = build_app_query(title, None) if title else None
        author_only = build_app_query(author, None) if author else None

        candidate = Candidate(
            path=path,
            file_name=os.path.basename(path),
            ext=ext,
            title_guess=title,
            author_guess=author,
            isbn_guess=isbn,
            asin_guess=asin,
            app_query_primary=primary,
            app_query_title_only=title_only,
            app_query_author_only=author_only,
        )

        attempts: List[AttemptResult] = []

        # Stage 0: identifier probing.
        if isbn:
            strategy_attempts["isbn"] += 1
            result = ol_search_isbn(isbn)
            attempts.append(result)
            if result.success:
                strategy_success["isbn"] += 1

        # Open Library does not support ASIN direct endpoint equivalent.
        if asin:
            strategy_attempts["asin"] += 1
            attempts.append(AttemptResult("asin", False, None, 0, "No direct ASIN endpoint on Open Library"))

        # Stage 1: app-style primary q (title + author).
        if primary:
            strategy_attempts["q_primary"] += 1
            result = ol_search_q(primary)
            attempts.append(result)
            if result.success:
                strategy_success["q_primary"] += 1

        # Stage 2: title-only fallback.
        if title_only:
            strategy_attempts["q_title_only"] += 1
            result = ol_search_q(title_only)
            attempts.append(result)
            if result.success:
                strategy_success["q_title_only"] += 1

        # Stage 3: author-only fallback.
        if author_only:
            strategy_attempts["q_author_only"] += 1
            result = ol_search_q(author_only)
            attempts.append(result)
            if result.success:
                strategy_success["q_author_only"] += 1

        resolved = any(a.success for a in attempts)

        if not title:
            gap_counter["no_title_detected"] += 1
        if not author:
            gap_counter["no_author_detected"] += 1
        if asin and not isbn:
            gap_counter["asin_no_direct_provider_lookup"] += 1
        if not resolved:
            gap_counter["unresolved_after_all_fallbacks"] += 1

        cases.append(
            {
                "candidate": asdict(candidate),
                "attempts": [asdict(x) for x in attempts],
                "resolved": resolved,
            }
        )

    total = len(cases)
    resolved_count = sum(1 for c in cases if c["resolved"])

    summary: Dict[str, Any] = {
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "media_root": args.media_root,
        "sample_size": total,
        "seed": args.seed,
        "resolved_count": resolved_count,
        "resolved_rate": round((resolved_count / total) * 100.0, 2),
        "strategy": {
            key: {
                "attempts": strategy_attempts[key],
                "successes": strategy_success[key],
                "success_rate": round((strategy_success[key] / strategy_attempts[key]) * 100.0, 2)
                if strategy_attempts[key]
                else 0.0,
            }
            for key in sorted(set(strategy_attempts.keys()) | set(strategy_success.keys()))
        },
        "gap_counts": dict(gap_counter),
        "extension_distribution": dict(extension_counter),
    }

    artifacts_dir = Path(args.artifacts_dir)
    artifacts_dir.mkdir(parents=True, exist_ok=True)

    json_path = artifacts_dir / "provider_pull_test_results.json"
    md_path = artifacts_dir / "provider_pull_test_report.md"

    with open(json_path, "w", encoding="utf-8") as f:
        json.dump({"summary": summary, "cases": cases}, f, indent=2)

    unresolved_examples = [
        c for c in cases if not c["resolved"]
    ][:10]

    lines: List[str] = []
    lines.append("# Provider Metadata Pull Test Report")
    lines.append("")
    lines.append(f"- Timestamp (UTC): {summary['timestamp_utc']}")
    lines.append(f"- Media root: {args.media_root}")
    lines.append(f"- Sample size: {summary['sample_size']}")
    lines.append(f"- Random seed: {args.seed}")
    lines.append(f"- Resolved: {summary['resolved_count']}/{summary['sample_size']} ({summary['resolved_rate']}%)")
    lines.append("")
    lines.append("## Strategy Metrics")
    lines.append("")
    lines.append("| Strategy | Attempts | Successes | Success Rate |")
    lines.append("|---|---:|---:|---:|")
    for key, value in summary["strategy"].items():
        lines.append(f"| {key} | {value['attempts']} | {value['successes']} | {value['success_rate']}% |")

    lines.append("")
    lines.append("## Gap Counts")
    lines.append("")
    if summary["gap_counts"]:
        for key, value in sorted(summary["gap_counts"].items()):
            lines.append(f"- {key}: {value}")
    else:
        lines.append("- No gaps detected in sampled set")

    lines.append("")
    lines.append("## Extension Distribution")
    lines.append("")
    for key, value in sorted(summary["extension_distribution"].items()):
        lines.append(f"- {key}: {value}")

    lines.append("")
    lines.append("## Top Unresolved Examples (up to 10)")
    lines.append("")
    if not unresolved_examples:
        lines.append("- None")
    else:
        for c in unresolved_examples:
            cand = c["candidate"]
            lines.append(f"- {cand['file_name']}")
            lines.append(f"  - path: {cand['path']}")
            lines.append(f"  - title_guess: {cand['title_guess']}")
            lines.append(f"  - author_guess: {cand['author_guess']}")
            lines.append(f"  - isbn_guess: {cand['isbn_guess']}")
            lines.append(f"  - asin_guess: {cand['asin_guess']}")

    with open(md_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    print(f"Wrote: {json_path}")
    print(f"Wrote: {md_path}")
    print(f"Resolved rate: {summary['resolved_rate']}% ({summary['resolved_count']}/{summary['sample_size']})")

    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Run provider metadata pull tests from /media sample")
    parser.add_argument("--media-root", default="/media", help="Root folder to sample files from")
    parser.add_argument("--sample-size", type=int, default=75, help="Number of random files to sample (50-100 recommended)")
    parser.add_argument("--seed", type=int, default=20260315, help="Random seed for reproducible sample")
    parser.add_argument("--artifacts-dir", default="/opt/Bibliophilarr/_artifacts/provider-pull-test-2026-03-15", help="Where to write reports")

    args = parser.parse_args()

    if args.sample_size < 1:
        raise ValueError("sample-size must be >= 1")

    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
