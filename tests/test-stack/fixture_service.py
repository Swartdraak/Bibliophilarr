#!/usr/bin/env python3
"""Deterministic local HTTP fixtures for Bibliophilarr integration tests."""

from __future__ import annotations

import argparse
import json
import mimetypes
import os
import re
from datetime import datetime, timezone
from email.utils import format_datetime
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse
from xml.sax.saxutils import escape, quoteattr


class FixtureHandler(BaseHTTPRequestHandler):
    server_version = "BibliophilarrFixture/1.0"

    @property
    def data_root(self) -> Path:
        return self.server.data_root  # type: ignore[attr-defined]

    def log_message(self, fmt: str, *args) -> None:
        print(f"fixture-service {self.address_string()} - {fmt % args}", flush=True)

    def _send_bytes(self, status: int, body: bytes, content_type: str, extra_headers=None) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Fixture-Service", "bibliophilarr")
        for key, value in (extra_headers or {}).items():
            self.send_header(key, value)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _send_json(self, status: int, payload) -> None:
        self._send_bytes(status, (json.dumps(payload, separators=(",", ":")) + "\n").encode(), "application/json")

    def _safe_file(self, relative: str) -> Path | None:
        candidate = (self.data_root / unquote(relative)).resolve()
        try:
            candidate.relative_to(self.data_root.resolve())
        except ValueError:
            return None
        return candidate if candidate.is_file() else None

    def _serve_file(self, file_path: Path, content_type: str | None = None) -> None:
        total = file_path.stat().st_size
        range_header = self.headers.get("Range")
        start, end = 0, total - 1
        status = HTTPStatus.OK

        if range_header:
            match = re.fullmatch(r"bytes=(\d*)-(\d*)", range_header.strip())
            if not match:
                self.send_error(HTTPStatus.REQUESTED_RANGE_NOT_SATISFIABLE)
                return
            first, last = match.groups()
            if first:
                start = int(first)
                end = int(last) if last else total - 1
            elif last:
                length = int(last)
                start = max(total - length, 0)
                end = total - 1
            if start >= total or end < start:
                self.send_error(HTTPStatus.REQUESTED_RANGE_NOT_SATISFIABLE)
                return
            end = min(end, total - 1)
            status = HTTPStatus.PARTIAL_CONTENT

        length = max(0, end - start + 1)
        ctype = content_type or mimetypes.guess_type(file_path.name)[0] or "application/octet-stream"
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(length))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Fixture-Service", "bibliophilarr")
        if status == HTTPStatus.PARTIAL_CONTENT:
            self.send_header("Content-Range", f"bytes {start}-{end}/{total}")
        self.end_headers()
        if self.command == "HEAD":
            return
        with file_path.open("rb") as stream:
            stream.seek(start)
            remaining = length
            while remaining:
                chunk = stream.read(min(64 * 1024, remaining))
                if not chunk:
                    break
                self.wfile.write(chunk)
                remaining -= len(chunk)

    def _caps_xml(self) -> bytes:
        return b'''<?xml version="1.0" encoding="UTF-8"?>
<caps>
  <server version="1.0" title="Bibliophilarr Fixture Indexer" />
  <limits max="100" default="20" />
  <registration available="no" open="no" />
  <searching>
    <search available="yes" supportedParams="q" />
    <book-search available="yes" supportedParams="q,author,title" />
  </searching>
  <categories>
    <category id="7000" name="Books">
      <subcat id="7020" name="EBook" />
    </category>
    <category id="3000" name="Audio">
      <subcat id="3030" name="Audiobook" />
    </category>
  </categories>
</caps>
'''

    def _search_xml(self, params: dict[str, list[str]]) -> bytes:
        query = " ".join(params.get("q", []) or params.get("title", []) or ["fixture"])
        now = format_datetime(datetime.now(timezone.utc), usegmt=True)
        releases = [
            {
                "id": "fixture-ebook",
                "title": "Fixture Author - Torrent Ebook EPUB",
                "torrent": "torrent-ebook.torrent",
                "payload": "Fixture Author - Torrent Ebook.epub",
                "categories": ["7000", "7020"],
            },
            {
                "id": "fixture-audiobook",
                "title": "Fixture Author - Torrent Audiobook WAV",
                "torrent": "torrent-audiobook.torrent",
                "payload": "Fixture Author - Torrent Audiobook.wav",
                "categories": ["3000", "3030", "7000"],
            },
        ]
        items = []
        for release in releases:
            payload_path = self.data_root / release["payload"]
            size = payload_path.stat().st_size if payload_path.exists() else 0
            download = f"http://fixture-service:8080/download/{release['torrent']}"
            attrs = "\n".join(
                f'      <newznab:attr name="category" value={quoteattr(category)} />'
                for category in release["categories"]
            )
            items.append(
                f'''    <item>
      <title>{escape(release["title"])}</title>
      <guid isPermaLink="false">{escape(release["id"])}</guid>
      <link>{escape(download)}</link>
      <comments>Fixture query: {escape(query)}</comments>
      <pubDate>{escape(now)}</pubDate>
      <enclosure url={quoteattr(download)} length={quoteattr(str(size))} type="application/x-bittorrent" />
{attrs}
      <newznab:attr name="size" value={quoteattr(str(size))} />
      <newznab:attr name="seeders" value="1" />
      <newznab:attr name="peers" value="1" />
    </item>'''
            )
        xml = f'''<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0" xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/">
  <channel>
    <title>Bibliophilarr Fixture Indexer</title>
    <description>Deterministic local test releases</description>
    <link>http://fixture-service:8080/</link>
{os.linesep.join(items)}
  </channel>
</rss>
'''
        return xml.encode("utf-8")

    def do_HEAD(self) -> None:
        self.do_GET()

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        path = parsed.path
        params = parse_qs(parsed.query)

        if path == "/health":
            self._send_json(HTTPStatus.OK, {"status": "ok", "service": "bibliophilarr-fixture"})
            return
        if path == "/v1/ping":
            self._send_json(HTTPStatus.OK, {"status": "ok"})
            return
        if path == "/v1/time":
            self._send_json(HTTPStatus.OK, {"utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")})
            return
        if path == "/v1/notification":
            self._send_json(HTTPStatus.OK, [])
            return
        if path == "/announce":
            self._send_bytes(HTTPStatus.OK, b"d8:intervali1800e5:peers0:e", "text/plain")
            return
        if path == "/api":
            action = (params.get("t") or ["search"])[0].lower()
            if action == "caps":
                self._send_bytes(HTTPStatus.OK, self._caps_xml(), "application/xml; charset=utf-8")
            elif action in {"search", "book"}:
                self._send_bytes(HTTPStatus.OK, self._search_xml(params), "application/rss+xml; charset=utf-8")
            else:
                self._send_bytes(HTTPStatus.BAD_REQUEST, b"unsupported fixture query\n", "text/plain; charset=utf-8")
            return
        if path.startswith("/download/"):
            file_path = self._safe_file(path.removeprefix("/download/"))
            if not file_path or file_path.suffix != ".torrent":
                self.send_error(HTTPStatus.NOT_FOUND)
                return
            self._serve_file(file_path, "application/x-bittorrent")
            return
        if path.startswith("/payload/"):
            file_path = self._safe_file(path.removeprefix("/payload/"))
            if not file_path:
                self.send_error(HTTPStatus.NOT_FOUND)
                return
            self._serve_file(file_path)
            return

        self._send_json(
            HTTPStatus.OK,
            {
                "service": "Bibliophilarr deterministic fixture service",
                "endpoints": ["/health", "/api?t=caps", "/api?t=search&q=fixture", "/v1/ping", "/v1/time"],
            },
        )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--data", type=Path, required=True)
    args = parser.parse_args()

    data_root = args.data.resolve()
    data_root.mkdir(parents=True, exist_ok=True)
    server = ThreadingHTTPServer((args.host, args.port), FixtureHandler)
    server.data_root = data_root  # type: ignore[attr-defined]
    print(f"fixture-service listening on {args.host}:{args.port}, data={data_root}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
