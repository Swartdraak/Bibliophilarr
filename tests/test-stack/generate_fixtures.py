#!/usr/bin/env python3
"""Generate deterministic, synthetic Bibliophilarr media and torrent fixtures."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import wave
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape

PIECE_LENGTH = 16 * 1024
FIXTURE_AUTHOR = "Fixture Author"


def bencode(value):
    if isinstance(value, int):
        return b"i" + str(value).encode("ascii") + b"e"
    if isinstance(value, bytes):
        return str(len(value)).encode("ascii") + b":" + value
    if isinstance(value, str):
        return bencode(value.encode("utf-8"))
    if isinstance(value, list):
        return b"l" + b"".join(bencode(item) for item in value) + b"e"
    if isinstance(value, dict):
        items = []
        for key in sorted(value, key=lambda item: item.encode("utf-8") if isinstance(item, str) else item):
            items.append(bencode(key))
            items.append(bencode(value[key]))
        return b"d" + b"".join(items) + b"e"
    raise TypeError(f"Unsupported bencode type: {type(value)!r}")


def make_epub(path: Path, title: str, author: str, isbn: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    container_xml = """<?xml version="1.0" encoding="UTF-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>
"""
    content_opf = f"""<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="bookid">urn:isbn:{escape(isbn)}</dc:identifier>
    <dc:title>{escape(title)}</dc:title>
    <dc:creator>{escape(author)}</dc:creator>
    <dc:language>en</dc:language>
  </metadata>
  <manifest>
    <item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/>
  </manifest>
  <spine>
    <itemref idref="chapter"/>
  </spine>
</package>
"""
    chapter = f"""<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml">
  <head><title>{escape(title)}</title></head>
  <body><h1>{escape(title)}</h1><p>Deterministic synthetic Bibliophilarr test fixture.</p></body>
</html>
"""
    with zipfile.ZipFile(path, "w") as zf:
        zf.writestr("mimetype", "application/epub+zip", compress_type=zipfile.ZIP_STORED)
        zf.writestr("META-INF/container.xml", container_xml, compress_type=zipfile.ZIP_DEFLATED)
        zf.writestr("OEBPS/content.opf", content_opf, compress_type=zipfile.ZIP_DEFLATED)
        zf.writestr("OEBPS/chapter.xhtml", chapter, compress_type=zipfile.ZIP_DEFLATED)


def make_wav(path: Path, seconds: float = 1.0, sample_rate: int = 8000) -> None:
    """Create a valid mono PCM WAV; .wav is an accepted Bibliophilarr audio extension."""
    path.parent.mkdir(parents=True, exist_ok=True)
    frames = int(seconds * sample_rate)
    amplitude = 1200
    frequency = 440.0
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        for index in range(frames):
            sample = int(amplitude * math.sin(2 * math.pi * frequency * index / sample_rate))
            wav.writeframesraw(struct.pack("<h", sample))


def make_torrent(payload: Path, torrent_path: Path) -> None:
    data = payload.read_bytes()
    pieces = b"".join(
        hashlib.sha1(data[offset : offset + PIECE_LENGTH]).digest()
        for offset in range(0, len(data), PIECE_LENGTH)
    )
    info = {
        "length": len(data),
        "name": payload.name,
        "piece length": PIECE_LENGTH,
        "pieces": pieces,
    }
    torrent = {
        "announce": "http://fixture-service:8080/announce",
        "created by": "Bibliophilarr deterministic test stack",
        "creation date": 1_700_000_000,
        "info": info,
        "url-list": f"http://fixture-service:8080/payload/{payload.name}",
    }
    torrent_path.parent.mkdir(parents=True, exist_ok=True)
    torrent_path.write_bytes(bencode(torrent))


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def generate(root: Path) -> None:
    root = root.resolve()
    fixture_data = root / "fixture-data"
    downloads = root / "downloads"
    books = root / "books"
    audiobooks = root / "audiobooks"
    evidence = root / "evidence"
    qbit_config = root / "qbittorrent-config"
    config = root / "config"

    for directory in (fixture_data, downloads, books, audiobooks, evidence, qbit_config, config):
        directory.mkdir(parents=True, exist_ok=True)

    torrent_ebook = fixture_data / "Fixture Author - Torrent Ebook.epub"
    torrent_audio = fixture_data / "Fixture Author - Torrent Audiobook.wav"
    make_epub(torrent_ebook, "Torrent Ebook", FIXTURE_AUTHOR, "9780306406157")
    make_wav(torrent_audio)
    make_torrent(torrent_ebook, fixture_data / "torrent-ebook.torrent")
    make_torrent(torrent_audio, fixture_data / "torrent-audiobook.torrent")

    existing_ebook = books / FIXTURE_AUTHOR / "Existing Dual Work" / f"Existing Dual Work - {FIXTURE_AUTHOR}.epub"
    existing_audio = audiobooks / FIXTURE_AUTHOR / "Existing Dual Work" / f"Existing Dual Work - {FIXTURE_AUTHOR}.wav"
    make_epub(existing_ebook, "Existing Dual Work", FIXTURE_AUTHOR, "9783161484100")
    make_wav(existing_audio)

    import_ebook = downloads / "manual" / "ebook" / f"{FIXTURE_AUTHOR} - Import Ebook.epub"
    import_audio = downloads / "manual" / "audiobook" / f"{FIXTURE_AUTHOR} - Import Audiobook.wav"
    dual_ebook = downloads / "manual" / "dual" / f"{FIXTURE_AUTHOR} - Dual Import.epub"
    dual_audio = downloads / "manual" / "dual" / f"{FIXTURE_AUTHOR} - Dual Import.wav"
    wrong_id = downloads / "manual" / "wrong-id" / f"{FIXTURE_AUTHOR} - Expected Filename Match.epub"
    ambiguous = downloads / "manual" / "ambiguous" / "Unknown - Generic Book.epub"
    unsupported = downloads / "manual" / "unsupported" / "ignore-me.txt"

    make_epub(import_ebook, "Import Ebook", FIXTURE_AUTHOR, "9780306406157")
    make_wav(import_audio)
    make_epub(dual_ebook, "Dual Import", FIXTURE_AUTHOR, "9783161484100")
    make_wav(dual_audio)
    make_epub(wrong_id, "Expected Filename Match", FIXTURE_AUTHOR, "9780140328721")
    make_epub(ambiguous, "Generic Book", "Unknown", "9780306406157")
    unsupported.parent.mkdir(parents=True, exist_ok=True)
    unsupported.write_text("This file must not be imported as book media.\n", encoding="utf-8")

    manifest = {
        "schema": 1,
        "author": FIXTURE_AUTHOR,
        "network": {
            "indexer_inside_compose": "http://fixture-service:8080/api",
            "fixture_service_inside_compose": "http://fixture-service:8080",
            "qbittorrent_inside_compose": "http://qbittorrent:8080",
        },
        "roots": {
            "ebook": "/books",
            "audiobook": "/audiobooks",
            "downloads": "/downloads",
        },
        "fixtures": [
            {"path": str(existing_ebook.relative_to(root)), "kind": "ebook", "purpose": "existing dual-format library baseline"},
            {"path": str(existing_audio.relative_to(root)), "kind": "audiobook", "purpose": "existing dual-format library baseline"},
            {"path": str(import_ebook.relative_to(root)), "kind": "ebook", "purpose": "manual import"},
            {"path": str(import_audio.relative_to(root)), "kind": "audiobook", "purpose": "manual import"},
            {"path": str(dual_ebook.relative_to(root)), "kind": "ebook", "purpose": "same-work dual-format import"},
            {"path": str(dual_audio.relative_to(root)), "kind": "audiobook", "purpose": "same-work dual-format import"},
            {"path": str(wrong_id.relative_to(root)), "kind": "ebook", "purpose": "wrong embedded identifier / filename fallback"},
            {"path": str(ambiguous.relative_to(root)), "kind": "ebook", "purpose": "ambiguous-match safety"},
            {"path": str(unsupported.relative_to(root)), "kind": "unsupported", "purpose": "extension rejection"},
            {"path": str(torrent_ebook.relative_to(root)), "kind": "ebook", "purpose": "local qBittorrent web-seed payload"},
            {"path": str(torrent_audio.relative_to(root)), "kind": "audiobook", "purpose": "local qBittorrent web-seed payload"},
        ],
    }
    for item in manifest["fixtures"]:
        path = root / item["path"]
        item["sha256"] = sha256(path)
        item["bytes"] = path.stat().st_size

    (root / "fixture-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path, help="Disposable test runtime root")
    args = parser.parse_args()
    generate(args.root)
