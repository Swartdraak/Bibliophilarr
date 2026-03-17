#!/usr/bin/env python3
"""Collect and compare metadata provenance metrics from a staging instance.

This script captures a provenance snapshot before and after a migration dry-run.
It can optionally trigger the OpenLibrary backfill command when API command
endpoints are available.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from dataclasses import dataclass
from typing import Any, Dict, List, Optional
from urllib import error, parse, request


@dataclass
class Snapshot:
    total_books: int
    books_with_openlibrary_work_id: int
    books_with_source_label: int

    def to_dict(self) -> Dict[str, int]:
        return {
            "total_books": self.total_books,
            "books_with_openlibrary_work_id": self.books_with_openlibrary_work_id,
            "books_with_source_label": self.books_with_source_label,
        }


class ApiClient:
    def __init__(self, base_url: str, api_key: str) -> None:
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key

    def _request(self, method: str, path: str, payload: Optional[Dict[str, Any]] = None) -> Any:
        url = f"{self.base_url}{path}"
        body = None
        headers = {
            "Accept": "application/json",
            "X-Api-Key": self.api_key,
        }

        if payload is not None:
            body = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"

        req = request.Request(url=url, method=method, data=body, headers=headers)

        try:
            with request.urlopen(req, timeout=30) as response:
                raw = response.read().decode("utf-8")
                if not raw:
                    return None
                return json.loads(raw)
        except error.HTTPError as exc:
            text = exc.read().decode("utf-8", errors="ignore")
            raise RuntimeError(f"HTTP {exc.code} {method} {path}: {text}") from exc
        except error.URLError as exc:
            raise RuntimeError(f"Network error {method} {path}: {exc}") from exc

    def fetch_books(self) -> List[Dict[str, Any]]:
        # Keep this robust across endpoint variants: list payload and wrapped payload.
        data = self._request("GET", "/api/v1/book")

        if isinstance(data, list):
            return data

        if isinstance(data, dict):
            for key in ("records", "items", "data"):
                if isinstance(data.get(key), list):
                    return data[key]

        return []

    def trigger_backfill(self, max_lookups: int) -> Optional[int]:
        payload = {
            "name": "BackfillOpenLibraryIds",
            "maxLookups": max_lookups,
        }

        data = self._request("POST", "/api/v1/command", payload)
        if isinstance(data, dict) and isinstance(data.get("id"), int):
            return data["id"]

        return None

    def wait_for_command(self, command_id: int, timeout_seconds: int) -> None:
        deadline = time.time() + timeout_seconds

        while time.time() < deadline:
            status = self._request("GET", f"/api/v1/command/{command_id}")
            state = str(status.get("status", "")).lower() if isinstance(status, dict) else ""

            if state in ("completed", "completedwarning"):
                return

            if state in ("failed", "aborted"):
                raise RuntimeError(f"Backfill command ended in state: {state}")

            time.sleep(2)

        raise RuntimeError("Timed out waiting for backfill command completion")


def summarize_books(books: List[Dict[str, Any]]) -> Snapshot:
    total = len(books)
    with_work_id = 0
    with_source = 0

    for book in books:
        if str(book.get("openLibraryWorkId") or "").strip():
            with_work_id += 1

        if str(book.get("metadataSourceLabel") or "").strip() or str(book.get("sourceProvider") or "").strip():
            with_source += 1

    return Snapshot(
        total_books=total,
        books_with_openlibrary_work_id=with_work_id,
        books_with_source_label=with_source,
    )


def write_json(path: str, payload: Dict[str, Any]) -> None:
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)


def main() -> int:
    parser = argparse.ArgumentParser(description="Run staging metadata migration dry-run and capture provenance metrics.")
    parser.add_argument("--base-url", default=os.getenv("BIBLIOPHILARR_STAGING_BASE_URL"))
    parser.add_argument("--api-key", default=os.getenv("BIBLIOPHILARR_API_KEY"))
    parser.add_argument("--max-lookups", type=int, default=200)
    parser.add_argument("--command-timeout", type=int, default=600)
    parser.add_argument("--before-out", default="_artifacts/metadata-dry-run/before.json")
    parser.add_argument("--after-out", default="_artifacts/metadata-dry-run/after.json")
    parser.add_argument("--summary-out", default="_artifacts/metadata-dry-run/summary.json")
    parser.add_argument("--skip-command", action="store_true")
    parser.add_argument("--min-openlibrary-coverage", type=float, default=0.70)
    parser.add_argument("--min-source-label-coverage", type=float, default=0.85)
    parser.add_argument("--enforce-gates", action="store_true")
    args = parser.parse_args()

    if not args.base_url or not args.api_key:
        print("Missing staging connection details. Set --base-url/--api-key or env vars.")
        return 0

    os.makedirs(os.path.dirname(args.before_out), exist_ok=True)

    client = ApiClient(args.base_url, args.api_key)

    before_snapshot = summarize_books(client.fetch_books())
    write_json(args.before_out, before_snapshot.to_dict())

    command_id = None
    if not args.skip_command:
        command_id = client.trigger_backfill(args.max_lookups)
        if command_id is not None:
            client.wait_for_command(command_id, args.command_timeout)

    after_snapshot = summarize_books(client.fetch_books())
    write_json(args.after_out, after_snapshot.to_dict())

    summary = {
        "before": before_snapshot.to_dict(),
        "after": after_snapshot.to_dict(),
        "delta": {
            "books_with_openlibrary_work_id": after_snapshot.books_with_openlibrary_work_id - before_snapshot.books_with_openlibrary_work_id,
            "books_with_source_label": after_snapshot.books_with_source_label - before_snapshot.books_with_source_label,
        },
        "coverage": {
            "openlibrary_work_id": (after_snapshot.books_with_openlibrary_work_id / after_snapshot.total_books) if after_snapshot.total_books else 0.0,
            "source_label": (after_snapshot.books_with_source_label / after_snapshot.total_books) if after_snapshot.total_books else 0.0,
        },
        "command_id": command_id,
    }

    gates = {
        "delta_openlibrary_non_negative": summary["delta"]["books_with_openlibrary_work_id"] >= 0,
        "delta_source_label_non_negative": summary["delta"]["books_with_source_label"] >= 0,
        "openlibrary_coverage_threshold": summary["coverage"]["openlibrary_work_id"] >= args.min_openlibrary_coverage,
        "source_label_coverage_threshold": summary["coverage"]["source_label"] >= args.min_source_label_coverage,
    }

    summary["acceptance_gates"] = {
        "thresholds": {
            "min_openlibrary_coverage": args.min_openlibrary_coverage,
            "min_source_label_coverage": args.min_source_label_coverage,
        },
        "results": gates,
        "all_passed": all(gates.values()),
    }

    write_json(args.summary_out, summary)
    print(json.dumps(summary, indent=2))

    if args.enforce_gates and not summary["acceptance_gates"]["all_passed"]:
        return 3

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(2)
