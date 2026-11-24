"""Verify POSIX SIGTERM and post-import SIGKILL preserve API-created dual-format catalogue state."""

import argparse
from contextlib import closing, contextmanager
from hashlib import sha256
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import re
import secrets
import signal
import socket
import sqlite3
import ssl
from tempfile import TemporaryDirectory
from threading import Thread
import time
from urllib.parse import parse_qs, urlsplit

from playwright.sync_api import expect, sync_playwright

from browser_add_book import VOLUME, VOLUME_RESOURCE, run_browser_add_book
from catalogue_workflow import (
    author_at_root, checked, configure_providers, files_for_book, initialize_root,
    wait_catalogue_command, write_epub, write_tagged_wav,
)
from smoke import (
    bootstrap_headers, check_bootstrap_user, check_native_profile, check_status,
    collect_page_errors, free_port, login, running_app, wait_ready,
)

AUTHOR = "Rowan Orchard"
TITLE = "The Orchard Observatory"
AUTHOR_KEY = "OL981401A"
WORK_KEY = "OL981501W"
AUTHOR_SOURCE = f"openlibrary:author:{AUTHOR_KEY}"
WORK_SOURCE = f"openlibrary:work:{WORK_KEY}"
EDITIONS = {
    ".epub": ("OL981601M", "Ebook", "ebook"),
    ".wav": ("OL981602M", "Audiobook", "audiobook"),
}


@contextmanager
def provider_fixture(tls, *, google_key=None):
    hostname = "www.googleapis.com" if google_key else "openlibrary.org"
    assert {item[4][0] for item in socket.getaddrinfo(hostname, 443)} == {"127.0.0.1"}
    calls, unexpected = [], []
    author_path, work_path = f"/authors/{AUTHOR_KEY}", f"/works/{WORK_KEY}"
    entries = [{"key": f"/books/{source}", "title": TITLE, "physical_format": format_name,
                "works": [{"key": work_path}], "languages": [{"key": "/languages/eng"}]}
               for source, format_name, _ in EDITIONS.values()]
    routes = {
        f"{author_path}.json": {"key": author_path, "name": AUTHOR, "bio": "Synthetic recovery author."},
        f"{author_path}/works.json": {"size": 0, "entries": []},
        f"{work_path}.json": {"key": work_path, "title": TITLE, "description": "Synthetic recovery work.",
                              "authors": [{"author": {"key": author_path}, "type": {"key": "/type/author_role"}}]},
        f"{work_path}/editions.json": {"size": len(entries), "entries": entries},
    }

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_GET(self):
            parsed = urlsplit(self.path)
            payload = routes.get(parsed.path)
            if parsed.path == "/search.json" and parse_qs(parsed.query).get("q") == [f"author_key:{AUTHOR_KEY}"]:
                payload = {"numFound": 0, "numFoundExact": True, "start": 0, "docs": []}
            if google_key:
                payload = None
                if parse_qs(parsed.query).get("key") == [google_key]:
                    if parsed.path == "/books/v1/volumes":
                        payload = {"totalItems": 1, "items": [VOLUME_RESOURCE]}
                    elif parsed.path == f"/books/v1/volumes/{VOLUME}":
                        payload = VOLUME_RESOURCE
            if (payload is None or self.headers.get("Host") != hostname or
                    any(self.headers.get(h) for h in ("Authorization", "Cookie", "X-Api-Key"))):
                unexpected.append(True)
                self.send_error(404)
                return
            calls.append(parsed.path)
            data = json.dumps(payload).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

    with ThreadingHTTPServer(("127.0.0.1", 443), Handler) as server:
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.minimum_version = ssl.TLSVersion.TLSv1_2
        context.load_cert_chain(tls / "leaf.crt", tls / "leaf.key")
        server.socket = context.wrap_socket(server.socket, server_side=True)
        thread = Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            yield calls
        finally:
            server.shutdown()
            thread.join(timeout=5)
        assert not thread.is_alive() and not unexpected, "Unexpected metadata fixture traffic"


def setup_catalogue(api, work):
    configure_providers(api)
    route = "/api/v1/config/metadataprovider"
    settings = checked(api.get(route), 200, "Read provider configuration")
    saved = checked(api.put(route + "/1", data={**settings, "enableOpenLibraryProvider": True,
                                                "updateCovers": False, "embedMetadata": False}),
                    202, "Enable only the loopback metadata fixture")
    assert saved["enableOpenLibraryProvider"] and not saved["enableHardcoverFallback"]
    route = "/api/v1/config/mediamanagement"
    settings = checked(api.get(route), 200, "Read media management settings")
    assert settings["enableDualFormatTracking"]
    checked(api.put(route + "/1", data={**settings, "watchLibraryForChanges": False, "rescanAfterRefresh": "never"}),
            202, "Keep the disposable root quiet")

    root = initialize_root(api, work)
    quality = checked(api.get("/api/v1/qualityprofile"), 200, "Read audiobook quality profiles")
    spoken = next(row["id"] for row in quality if row["name"] == "Spoken")
    author = author_at_root({"authorName": AUTHOR, "foreignAuthorId": AUTHOR_SOURCE}, root)
    author["addOptions"].update(ebookQualityProfileId=root["defaultQualityProfileId"], audiobookQualityProfileId=spoken,
                                ebookRootFolderPath=root["path"], audiobookRootFolderPath=root["path"])
    author = checked(api.post("/api/v1/author", data=author), 201, "Create the fixture author via the signed API")
    commands = checked(api.get("/api/v1/command"), 200, "Read initial author command")
    refresh, = [command for command in commands if command["name"] == "RefreshAuthor"]
    assert wait_catalogue_command(api, refresh["id"], "RefreshAuthor", timeout=45)["result"] == "successful"
    assert not checked(api.get("/api/v1/book", params={"authorId": author["id"]}), 200, "Confirm the empty bibliography")

    edition = f"openlibrary:edition:{EDITIONS['.epub'][0]}"
    book = checked(api.post("/api/v1/book", data={
        "title": TITLE, "foreignBookId": WORK_SOURCE, "foreignEditionId": edition, "monitored": False,
        "editions": [{"foreignEditionId": edition, "monitored": True}], "addOptions": {"searchForNewBook": False},
    }), 201, "Create the fixture work via the signed API")
    assert book["authorId"] == author["id"]
    return root, author, book


def copy_fixtures(api, work, author, book):
    inbox = work / "recovery-inbox"
    inbox.mkdir()
    epub, wav = (inbox / (f"{AUTHOR} - {TITLE}" + extension) for extension in EDITIONS)
    write_epub(epub, TITLE, AUTHOR, "urn:test:dual-format-recovery")
    write_tagged_wav(wav, TITLE, AUTHOR)
    digests = {path.suffix: sha256(path.read_bytes()).hexdigest() for path in (epub, wav)}
    for path in (epub, wav):
        params = {"folder": str(inbox), "authorId": author["id"], "filterExistingFiles": "false",
                  "replaceExistingFiles": "false"}
        rows = checked(api.get("/api/v1/manualimport", params=params), 200, f"Find fixture {path.suffix}")
        item, = [row for row in rows if Path(row["path"]) == path]
        source, _, format_type = EDITIONS[path.suffix]
        edition = f"openlibrary:edition:{source}"
        selection = {"id": item["id"], "path": str(path), "name": item["name"], "authorId": author["id"],
                     "bookId": book["id"], "foreignEditionId": edition, "quality": item["quality"],
                     "indexerFlags": 0, "replaceExistingFiles": False}
        validated, = checked(api.post("/api/v1/manualimport", data=[selection]), 202, f"Validate fixture {path.suffix}")
        assert validated["foreignEditionId"] == edition and not validated["rejections"]
        command = checked(api.post("/api/v1/command", data={"name": "ManualImport", "files": [selection],
                          "importMode": "copy", "replaceExistingFiles": False}), 201, f"Queue copy of {format_type}")
        assert wait_catalogue_command(api, command["id"], "ManualImport", timeout=45)["result"] == "successful"
    return {path: digests[path.suffix] for path in (epub, wav)}


def wait_idle(api):
    until = time.monotonic() + 45
    while time.monotonic() < until:
        commands = checked(api.get("/api/v1/command"), 200, "Read running catalogue commands")
        if all(row["status"] not in ("queued", "started") for row in commands):
            return
        time.sleep(0.25)
    raise TimeoutError("Catalogue commands did not settle")


def snapshot(api, profile, root, author, book, originals, migrations):
    base = "/api/v1"
    current_root = checked(api.get(f"{base}/rootfolder/{root['id']}"), 200, "Read root identity")
    current_author = checked(api.get(f"{base}/author/{author['id']}"), 200, "Read author identity")
    current_book = checked(api.get(f"{base}/book/{book['id']}"), 200, "Read work identity")
    for route, expected in (("rootfolder", root), ("author", author), (f"book?authorId={author['id']}", book)):
        records = checked(api.get(f"{base}/{route}"), 200, "Check catalogue count")
        assert [row["id"] for row in records] == [expected["id"]]
    assert current_root["path"] == root["path"] and current_root["id"] == root["id"]
    assert (current_author["id"], current_author["foreignAuthorId"]) == (author["id"], AUTHOR_SOURCE)
    assert (current_book["id"], current_book["authorId"], current_book["foreignBookId"]) == (
        book["id"], author["id"], WORK_SOURCE)
    assert {row["formatType"]: (row["hasFile"], row["fileCount"]) for row in current_book["formatStatuses"]} == {
        "ebook": (True, 1), "audiobook": (True, 1),
    }

    database = (profile / "bibliophilarr.db").as_uri() + "?mode=ro"
    with closing(sqlite3.connect(database, uri=True)) as db:
        db.execute("PRAGMA query_only=ON")
        db.execute("BEGIN")
        assert db.execute("PRAGMA integrity_check").fetchall() == [("ok",)]
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        assert [row[0] for row in db.execute("SELECT Version FROM VersionInfo ORDER BY Version")] == migrations
        links = dict(db.execute("SELECT Id, EditionId FROM BookFiles"))
        owners = dict(db.execute("SELECT Id, BookId FROM Editions"))
        assert owners == {row["id"]: book["id"] for row in current_book["editions"]}

    editions = {row["foreignEditionId"]: row for row in current_book["editions"]}
    files = files_for_book(api, book["id"])
    assert len(editions) == len(files) == len(links) == 2
    original_by_suffix = {path.suffix: digest for path, digest in originals.items()}
    assert {path.suffix: sha256(path.read_bytes()).hexdigest() for path in originals} == original_by_suffix
    state = {}
    for row in files:
        path = Path(row["path"])
        assert row["bookId"] == book["id"] and path.is_relative_to(Path(root["path"]))
        source, format_name, format_type = EDITIONS[path.suffix]
        edition = editions[f"openlibrary:edition:{source}"]
        digest = sha256(path.read_bytes()).hexdigest()
        assert digest == original_by_suffix[path.suffix]
        assert links[row["id"]] == edition["id"] and edition["format"] == format_name
        assert edition["isEbook"] is (format_type == "ebook")
        state[format_type] = (row["id"], edition["id"], path.relative_to(Path(root["path"])).as_posix(), digest)
    assert len(state) == len({item[1] for item in state.values()}) == 2
    return state


def check_forms(browser, base, credentials):
    with browser.new_context(service_workers="block") as context:
        context.route("**/*", lambda route: route.continue_() if route.request.url.startswith(base + "/") else route.abort())
        page = context.new_page()
        errors = collect_page_errors(page)
        page.goto(base + "/authors")
        expect(page.get_by_placeholder("Username")).to_be_visible()
        login(page, **credentials, rejected=False)
        expect(page.get_by_role("heading", name="Authors", exact=True)).to_be_visible()
        assert not errors


def check_bootstrap_conflict(http, base, profile, credentials):
    second = {**credentials, "username": "second-recovery-fixture"}
    check_status(http.post(base + "/bootstrap", headers=bootstrap_headers(http, base), form=second,
                           max_redirects=0), 409, "Reject a second recovery owner")
    check_bootstrap_user(profile, credentials)


def migration_versions(directory):
    versions = [int(version) for path in directory.glob("[0-9][0-9][0-9]_*.cs")
                for version in re.findall(r"^\s*\[Migration\((\d+)\)\]", path.read_text(encoding="utf-8-sig"), re.M)]
    assert (set(range(1, 50)) - {34} | {158}).issubset(versions) and len(versions) == len(set(versions))
    return sorted(versions)


def run(binary, evidence, tls, migrations):
    credentials = {"username": "native-recovery-fixture", "password": "Ci!" + secrets.token_urlsafe(19)}
    with TemporaryDirectory(prefix="bibliophilarr-dual-format-") as work, sync_playwright() as pw, provider_fixture(tls) as calls:
        work = Path(work)
        profile, log = work / "profile", work / "before-term.log"
        port = free_port()
        http = pw.request.new_context()
        with pw.chromium.launch() as browser:
            assert not profile.exists()
            with running_app(binary, profile, log, port, evidence, credentials, tls_ca=tls / "ca.crt") as (app, base):
                before_key = wait_ready(app, http, base, profile)
                assert re.fullmatch(r"[0-9a-fA-F]{32}", before_key) and int(before_key, 16) != 0
                api = pw.request.new_context(base_url=base, extra_http_headers={"X-Api-Key": before_key})
                check_native_profile(http, api, base, profile, before_key, "Before SIGTERM")
                assert not checked(api.get("/api/v1/rootfolder"), 200, "Confirm the fresh root catalogue")
                check_bootstrap_user(profile)
                check_status(http.post(base + "/bootstrap", headers=bootstrap_headers(http, base), form=credentials,
                                       max_redirects=0), 200, "Create the first recovery owner")
                check_bootstrap_user(profile, credentials)
                check_bootstrap_conflict(http, base, profile, credentials)
                check_forms(browser, base, credentials)
                root, author, book = setup_catalogue(api, work)
                originals = copy_fixtures(api, work, author, book)
                wait_idle(api)
                previous = snapshot(api, profile, root, author, book, originals, migrations)
                assert Path(f"/proc/{app.pid}/exe").samefile(binary)
            assert app.returncode == 0 and "Received SIGTERM, initiating graceful shutdown" in log.read_text()
            assert "/search.json" in calls and f"/works/{WORK_KEY}/editions.json" in calls
            previous_call_count = len(calls)

            with running_app(binary, profile, work / "after-term.log", port, evidence, credentials,
                             tls_ca=tls / "ca.crt") as (reopened, base):
                assert reopened.pid != app.pid and wait_ready(reopened, http, base, profile) == before_key
                check_native_profile(http, api, base, profile, before_key, "After SIGTERM")
                check_bootstrap_user(profile, credentials)
                check_forms(browser, base, credentials)
                wait_idle(api)
                assert snapshot(api, profile, root, author, book, originals, migrations) == previous
                assert len(calls) == previous_call_count, "Provider traffic occurred during reopen"
            assert reopened.returncode == 0

            # Keep both graceful exits above independent of the forced-stop case.
            with running_app(binary, profile, work / "before-kill.log", port, evidence, credentials,
                             tls_ca=tls / "ca.crt") as (interrupted, base):
                assert interrupted.pid not in (app.pid, reopened.pid)
                assert wait_ready(interrupted, http, base, profile) == before_key
                check_native_profile(http, api, base, profile, before_key, "Before SIGKILL")
                wait_idle(api)
                assert snapshot(api, profile, root, author, book, originals, migrations) == previous
                assert len(calls) == previous_call_count
                assert interrupted.poll() is None and Path(f"/proc/{interrupted.pid}/exe").samefile(binary)
                interrupted.kill()
                assert interrupted.wait(timeout=10) == -signal.SIGKILL

            with running_app(binary, profile, work / "after-kill.log", port, evidence, credentials,
                             tls_ca=tls / "ca.crt") as (recovered, base):
                assert recovered.pid not in (app.pid, reopened.pid, interrupted.pid)
                assert wait_ready(recovered, http, base, profile) == before_key
                check_native_profile(http, api, base, profile, before_key, "After SIGKILL")
                check_bootstrap_conflict(http, base, profile, credentials)
                check_forms(browser, base, credentials)
                wait_idle(api)
                assert snapshot(api, profile, root, author, book, originals, migrations) == previous
                assert len(calls) == previous_call_count, "Provider traffic occurred during crash recovery"
            assert recovered.returncode == 0

    receipt = {"sigtermCleanExit": True, "generatedKeyRetained": True, "formsOwnerRetained": True,
               "rootId": root["id"], "authorId": author["id"], "workId": book["id"],
               "formats": previous, "migrationCount": len(migrations), "fixtureCalls": previous_call_count,
               "providerCallsDuringReopen": 0}
    (evidence / "dual-format-sigterm.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print("Packaged dual-format SIGTERM recovery: PASS", flush=True)
    crash_receipt = {**receipt, "sigkillExitCode": interrupted.returncode,
                     "catalogueRetainedAfterSigkill": True, "secondBootstrapRejected": True}
    (evidence / "dual-format-sigkill.json").write_text(json.dumps(crash_receipt, indent=2) + "\n")
    print("Packaged dual-format seeded SIGKILL recovery: PASS", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("binary", "evidence", "tls", "migrations"):
        parser.add_argument("--" + option, type=Path, required=True)
    parser.add_argument("--browser-book-add", action="store_true")
    args = parser.parse_args()
    os.umask(0o077)
    args.evidence.mkdir(parents=True, exist_ok=True)
    stage = "Packaged dual-format SIGTERM recovery"
    try:
        binary = args.binary.resolve(strict=True)
        tls = args.tls.resolve(strict=True)
        run(binary, args.evidence, tls, migration_versions(args.migrations.resolve(strict=True)))
        if args.browser_book_add:
            stage = "Browser Add New synthetic source edition"
            run_browser_add_book(binary, args.evidence, tls, provider_fixture)
    except Exception as error:
        raise SystemExit(f"{stage} failed ({type(error).__name__}). See the last checkpoint.") from None
