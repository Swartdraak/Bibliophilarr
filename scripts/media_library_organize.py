#!/usr/bin/env python3
"""
Full scan + clean organizer for audiobook/ebook libraries.

Canonical structure:
  <root>/<Author>/<Title[ (id)]>/<Title - Author><ext>

Notes:
- Uses metadata.json first, then metadata.opf.
- Preserves Calibre-style numeric folder suffix " (1234)" when present.
- Collision-safe: conflicting folder/file moves are skipped and reported.
- Supports dry-run and apply modes.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional, Tuple

AUDIO_EXTS = {".mp3", ".m4b", ".flac", ".aac", ".ogg"}
EBOOK_EXTS = {".epub", ".mobi", ".azw3", ".pdf"}
MEDIA_EXTS = AUDIO_EXTS | EBOOK_EXTS

INVALID_CHARS = re.compile(r"[\\/:*?\"<>|]")
MULTISPACE = re.compile(r"\s+")
ID_SUFFIX = re.compile(r"\s*\((\d+)\)$")


@dataclass
class BookFolder:
    path: Path
    author: str
    title: str
    id_suffix: Optional[str]
    media_files: List[Path]


def clean_name(value: str) -> str:
    value = value or "Unknown"
    value = INVALID_CHARS.sub(" ", value)
    value = MULTISPACE.sub(" ", value).strip(" .-")
    return value if value else "Unknown"


def read_metadata_json(path: Path) -> Tuple[Optional[str], Optional[str]]:
    if not path.exists():
        return None, None

    try:
        data = json.loads(path.read_text(encoding="utf-8", errors="ignore"))
    except Exception:
        return None, None

    title = data.get("title")
    authors = data.get("authors") or []
    author = authors[0] if isinstance(authors, list) and authors else None
    return title, author


def read_metadata_opf(path: Path) -> Tuple[Optional[str], Optional[str]]:
    if not path.exists():
        return None, None

    try:
        root = ET.parse(path).getroot()
    except Exception:
        return None, None

    ns = {"dc": "http://purl.org/dc/elements/1.1/"}
    title_node = root.find(".//dc:title", ns)
    author_node = root.find(".//dc:creator", ns)
    title = title_node.text if title_node is not None else None
    author = author_node.text if author_node is not None else None
    return title, author


def infer_from_path(folder: Path, root: Path) -> Tuple[str, str, Optional[str]]:
    author = folder.parent.name if folder.parent != root else "Unknown"
    title_folder = folder.name

    match = ID_SUFFIX.search(title_folder)
    suffix = None
    title = title_folder
    if match:
        suffix = match.group(1)
        title = ID_SUFFIX.sub("", title_folder).strip()

    return title, author, suffix


def discover_book_folders(root: Path) -> List[Path]:
    result: List[Path] = []
    quarantine_markers = {"_dupes", "_unidentified"}

    for dirpath, _, filenames in os.walk(root):
        path = Path(dirpath)

        # Keep quarantine areas untouched during normal root scans, but allow
        # users to run directly against a quarantine root for targeted retries.
        rel_parts = path.relative_to(root).parts
        if any(marker in rel_parts for marker in quarantine_markers):
            continue

        media_files = [f for f in filenames if Path(f).suffix.lower() in MEDIA_EXTS]
        if media_files:
            result.append(path)

    return sorted(result)


def collect_book_info(folder: Path, root: Path) -> BookFolder:
    json_title, json_author = read_metadata_json(folder / "metadata.json")

    opf_candidates = [folder / "metadata.opf"]
    opf_candidates.extend(p for p in folder.glob("*.opf") if p.name != "metadata.opf")

    opf_title = None
    opf_author = None
    for opf in opf_candidates:
        t, a = read_metadata_opf(opf)
        if t or a:
            opf_title = t
            opf_author = a
            break

    inferred_title, inferred_author, inferred_suffix = infer_from_path(folder, root)

    title = clean_name(json_title or opf_title or inferred_title)
    author = clean_name(json_author or opf_author or inferred_author)

    folder_name = folder.name
    m = ID_SUFFIX.search(folder_name)
    suffix = m.group(1) if m else inferred_suffix

    media_files = sorted([p for p in folder.iterdir() if p.is_file() and p.suffix.lower() in MEDIA_EXTS])

    return BookFolder(
        path=folder,
        author=author,
        title=title,
        id_suffix=suffix,
        media_files=media_files,
    )


def canonical_book_folder_name(title: str, id_suffix: Optional[str]) -> str:
    if id_suffix:
        return f"{title} ({id_suffix})"
    return title


def media_base_name(title: str, author: str) -> str:
    return f"{title} - {author}"


def media_name_for_index(base: str, ext: str, index: Optional[int]) -> str:
    if index is None:
        return f"{base}{ext}"

    return f"{base} ({index}){ext}"


def extract_existing_index(file_name: str, base: str, ext: str) -> Optional[int]:
    if file_name == f"{base}{ext}":
        return None

    pattern = re.compile(rf"^{re.escape(base)} \((\d+)\){re.escape(ext)}$", re.IGNORECASE)
    match = pattern.match(file_name)
    if not match:
        return None

    return int(match.group(1))


def propose_moves(root: Path, books: List[BookFolder]) -> List[Dict[str, str]]:
    actions: List[Dict[str, str]] = []

    for book in books:
        target_author_dir = root / clean_name(book.author)
        target_book_dir = target_author_dir / canonical_book_folder_name(book.title, book.id_suffix)

        if book.path != target_book_dir:
            actions.append(
                {
                    "type": "move_folder",
                    "src": str(book.path),
                    "dst": str(target_book_dir),
                }
            )

        # Media naming canonicalization
        by_ext: Dict[str, List[Path]] = {}
        for media in book.media_files:
            by_ext.setdefault(media.suffix.lower(), []).append(media)

        for ext, files in by_ext.items():
            files_sorted = sorted(files)
            base = media_base_name(book.title, book.author)
            target_dir = target_book_dir if book.path != target_book_dir else book.path

            if len(files_sorted) == 1:
                desired = media_name_for_index(base, ext, None)
                src = files_sorted[0]
                dst = target_dir / desired

                if src.name != desired:
                    actions.append(
                        {
                            "type": "rename_media",
                            "src": str(src),
                            "dst": str(dst),
                        }
                    )

                continue

            # Keep already canonical indices stable to avoid rename churn.
            used_indices = set()
            undecided: List[Path] = []

            for src in files_sorted:
                existing_index = extract_existing_index(src.name, base, ext)
                if existing_index is None and src.name == media_name_for_index(base, ext, None):
                    used_indices.add(1)
                    continue

                if existing_index is not None:
                    used_indices.add(existing_index)
                    continue

                undecided.append(src)

            next_index = 1
            for src in undecided:
                while next_index in used_indices:
                    next_index += 1

                desired = media_name_for_index(base, ext, next_index)
                dst = target_dir / desired

                if src.name != desired:
                    actions.append(
                        {
                            "type": "rename_media",
                            "src": str(src),
                            "dst": str(dst),
                        }
                    )

                used_indices.add(next_index)

    return actions


def ensure_parent(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)


def apply_actions(actions: List[Dict[str, str]]) -> Dict[str, int]:
    stats = {
        "applied": 0,
        "skipped_conflict": 0,
        "skipped_missing": 0,
        "errors": 0,
    }

    # Folder moves first, shallow paths first.
    folder_moves = [a for a in actions if a["type"] == "move_folder"]
    folder_moves.sort(key=lambda a: len(Path(a["src"]).parts))

    other_moves = [a for a in actions if a["type"] != "move_folder"]

    for action in folder_moves + other_moves:
        src = Path(action["src"])
        dst = Path(action["dst"])

        if not src.exists():
            stats["skipped_missing"] += 1
            continue

        if dst.exists() and src.resolve() != dst.resolve():
            # For file renames, pick a unique destination instead of skipping.
            if action["type"] == "rename_media" and src.is_file():
                stem = dst.stem
                suffix = dst.suffix
                counter = 2
                candidate = dst

                while candidate.exists():
                    candidate = dst.parent / f"{stem} ({counter}){suffix}"
                    counter += 1

                dst = candidate
            else:
                stats["skipped_conflict"] += 1
                continue

        try:
            ensure_parent(dst)
            shutil.move(str(src), str(dst))
            stats["applied"] += 1
        except Exception:
            stats["errors"] += 1

    return stats


def scan_and_organize(root: Path, apply: bool, report_dir: Path) -> Dict[str, object]:
    folders = discover_book_folders(root)
    books = [collect_book_info(folder, root) for folder in folders]
    actions = propose_moves(root, books)

    report_dir.mkdir(parents=True, exist_ok=True)
    summary = {
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "root": str(root),
        "book_folders": len(books),
        "media_files": sum(len(b.media_files) for b in books),
        "actions_proposed": len(actions),
        "mode": "apply" if apply else "dry-run",
    }

    apply_stats = None
    if apply:
        apply_stats = apply_actions(actions)
        summary["apply_stats"] = apply_stats

    stem = root.name
    summary_path = report_dir / f"{stem}_organize_summary.json"
    actions_path = report_dir / f"{stem}_organize_actions.json"

    summary_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    actions_path.write_text(json.dumps(actions, indent=2), encoding="utf-8")

    return {
        "summary": summary,
        "summary_path": str(summary_path),
        "actions_path": str(actions_path),
        "apply_stats": apply_stats,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Scan and organize media library folder")
    parser.add_argument("--root", required=True, help="Root media directory (e.g. /media/audiobooks)")
    parser.add_argument("--report-dir", default="/opt/Bibliophilarr/_artifacts/media-organize", help="Directory for scan/action reports")
    parser.add_argument("--apply", action="store_true", help="Apply proposed actions (default dry-run)")

    args = parser.parse_args()

    root = Path(args.root)
    if not root.exists() or not root.is_dir():
        raise RuntimeError(f"Invalid root directory: {root}")

    result = scan_and_organize(root, args.apply, Path(args.report_dir))
    summary = result["summary"]

    print(f"Root: {summary['root']}")
    print(f"Mode: {summary['mode']}")
    print(f"Book folders: {summary['book_folders']}")
    print(f"Media files: {summary['media_files']}")
    print(f"Actions proposed: {summary['actions_proposed']}")
    print(f"Summary report: {result['summary_path']}")
    print(f"Actions report: {result['actions_path']}")

    if args.apply:
        print(f"Apply stats: {json.dumps(result['apply_stats'])}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
