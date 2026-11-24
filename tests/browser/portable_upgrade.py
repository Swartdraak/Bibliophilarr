"""Verify a published portable v1.1.0 migration and paired-profile rollback."""

import argparse
from contextlib import closing, contextmanager
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import platform
import re
import secrets
import shutil
import sqlite3
import subprocess
import tarfile
from tempfile import TemporaryDirectory
from threading import Thread
import time
import wave
from urllib.parse import urlsplit
from xml.etree import ElementTree

from playwright.sync_api import expect, sync_playwright

from catalogue_workflow import configure_providers, initialize_root, write_epub
from smoke import check_status, free_port, wait_ready


STABLE_COMMIT = "7be1420764c14791123154327cecf72cdf890cdc"
STABLE = {
    ("Windows", "amd64"): ("win-x64", ".zip", "9fbda21480ee22e95e06d126a314550fcc9a9129d9818a0833c4bb21110dbe2e"),
    ("Darwin", "arm64"): ("osx-arm64", ".tar.gz", "951b6c9d07ee7f86e296379ea28aa5d7958e850630e38afd6d1940d759c6b174"),
    ("Linux", "x86_64"): ("linux-x64", ".tar.gz", "31b8f9f40be00ea0d80a7878bd9b3db91a2b52ad56337503c4a38d6b242f31d6"),
}
TITLE = "The Upgrade Fixture Observatory"
AUTHOR = "Avery Synthetic"
AUTHOR_ID = "hardcover:author:975401"
AUTHOR_SLUG = "hardcover-author-975401"
BOOK_ID = "hardcover:work:975402"
BOOK_SLUG = "upgrade-fixture-observatory"
EDITIONS = {
    ".epub": ("hardcover:edition:975403", "Ebook", 1),
    ".wav": ("hardcover:edition:975404", "Audiobook", 0),
}


def digest(path):
    with path.open("rb") as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest()


def content_manifest(directory):
    return {path.relative_to(directory).as_posix(): digest(path)
            for path in directory.rglob("*") if path.is_file()}


def unpack(asset, directory, rid, runtimes):
    directory.mkdir()
    if asset.name.endswith(".zip"):
        shutil.unpack_archive(asset, directory, "zip")
        executable = directory / "Bibliophilarr.Console.exe"
    else:
        with tarfile.open(asset) as archive:
            archive.extractall(directory, filter="data")
        executable = directory / "Bibliophilarr" / "Bibliophilarr"
    runtimeconfig = executable.with_name(f"{executable.stem}.runtimeconfig.json")
    requirements = json.loads(runtimeconfig.read_text(encoding="utf-8"))["runtimeOptions"]["frameworks"]
    assert {item["name"] for item in requirements} == {"Microsoft.NETCore.App", "Microsoft.AspNetCore.App"}
    for item in requirements:
        required = tuple(int(part) for part in item["version"].split("."))
        assert any(version[:2] == required[:2] and version >= required for version in runtimes[item["name"]]), (
            rid, item["name"], item["version"], "required framework is unavailable"
        )
    return executable, requirements


def framework_inventory():
    output = subprocess.check_output(["dotnet", "--list-runtimes"], text=True)
    runtimes = {}
    for line in output.splitlines():
        item = re.match(r"^(\S+) (\d+)\.(\d+)\.(\d+) \[", line)
        if item:
            runtimes.setdefault(item[1], []).append(tuple(int(item[i]) for i in (2, 3, 4)))
    return runtimes


@contextmanager
def disallow_egress_proxy():
    class RejectingProxy(BaseHTTPRequestHandler):
        def reject(self):
            self.server.attempts += 1
            self.send_error(502, "Offline upgrade fixture")

        do_CONNECT = do_GET = do_POST = do_HEAD = reject

        def log_message(self, *_):
            pass

    with ThreadingHTTPServer(("127.0.0.1", 0), RejectingProxy) as proxy:
        proxy.attempts = 0
        thread = Thread(target=proxy.serve_forever, daemon=True)
        thread.start()
        try:
            yield proxy
        finally:
            proxy.shutdown()
            thread.join()


@contextmanager
def launch(binary, profile, http, base, port, proxy, logs, stage, expected_key=None, *, legacy=False):
    # Retain only host/runtime variables. Do not inherit CI tokens or provider settings.
    allowed = ("PATH", "SYSTEMROOT", "WINDIR", "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432",
               "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "PROGRAMDATA",
               "TMP", "TEMP", "TMPDIR", "LANG", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64")
    env = {name: os.environ[name] for name in allowed if name in os.environ}
    proxy_url = f"http://127.0.0.1:{proxy.server_port}"
    env.update(ASPNETCORE_ENVIRONMENT="Production", HTTP_PROXY=proxy_url, HTTPS_PROXY=proxy_url,
               ALL_PROXY=proxy_url, NO_PROXY="127.0.0.1,localhost")
    if not legacy:
        env.update(BIBLIOPHILARR__SERVER__PORT=str(port), BIBLIOPHILARR__SERVER__BINDADDRESS="127.0.0.1",
                   BIBLIOPHILARR__LOG__ANALYTICSENABLED="false", BIBLIOPHILARR__UPDATE__PUBLICRELEASEDISCOVERY="false")
    print(f"Starting {stage}", flush=True)
    with (logs / (stage + ".log")).open("wb") as log:
        process = subprocess.Popen([str(binary), f"/data={profile}", "/nobrowser", "/nosingleinstancecheck"],
                                   cwd=binary.parent, env=env, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
        try:
            key = wait_ready(process, http, base, profile)
            assert expected_key is None or key == expected_key, "Persisted API identity changed"
            yield key
            check_status(http.post(base + "/api/v1/system/shutdown", headers={"X-Api-Key": key}),
                         200, f"{stage} signed shutdown")
            assert process.wait(timeout=25) == 0, f"{stage} did not exit cleanly"
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=10)


def open_db(profile, mode="ro"):
    return closing(sqlite3.connect((profile / "bibliophilarr.db").as_uri() + f"?mode={mode}", uri=True))


def seed_stable_catalogue(profile, library, root):
    with open_db(profile, "rw") as db, db:
        metadata = db.execute(
            """INSERT INTO AuthorMetadata
                (ForeignAuthorId,TitleSlug,Name,Status,Images,Links,Genres,Ratings,Aliases,SortName,NameLastFirst,SortNameLastFirst)
                VALUES (?,?,?,0,'[]','[]','[]','{}','[]',?,?,?)""",
            (AUTHOR_ID, AUTHOR_SLUG, AUTHOR, AUTHOR, "Synthetic, Avery", "Synthetic, Avery"),
        ).lastrowid
        author = db.execute(
            """INSERT INTO Authors
                (CleanName,Path,Monitored,QualityProfileId,Tags,MetadataProfileId,AuthorMetadataId,MonitorNewItems)
                VALUES (?,?,0,?,'[]',?,?,0)""",
            ("averysynthetic", str(library / AUTHOR), root["defaultQualityProfileId"],
             root["defaultMetadataProfileId"], metadata),
        ).lastrowid
        book = db.execute(
            """INSERT INTO Books
                (AuthorMetadataId,ForeignBookId,TitleSlug,Title,CleanTitle,Monitored,AnyEditionOk,Links,Genres,Ratings,AddOptions)
                VALUES (?,?,?,?,?,0,1,'[]','[]','{}','{}')""",
            (metadata, BOOK_ID, BOOK_SLUG, TITLE, "theupgradefixtureobservatory"),
        ).lastrowid
        for external, media_format, ebook in EDITIONS.values():
            db.execute(
                """INSERT INTO Editions
                    (BookId,ForeignEditionId,Title,TitleSlug,Language,Format,IsEbook,Monitored,ManualAdd,Images,Links,Ratings)
                    VALUES (?,?,?,?, 'en',?,?,1,1,'[]','[]','{}')""",
                (book, external, TITLE, f"{BOOK_SLUG}-{media_format.lower()}", media_format, ebook),
            )
    return author, book


def fixture_import(api, inbox, author, book):
    inbox.mkdir()
    epub, audio = (inbox / ("fixture" + extension) for extension in EDITIONS)
    write_epub(epub, TITLE, AUTHOR, "urn:fixture:upgrade-observatory")
    with wave.open(str(audio), "wb") as pcm:
        pcm.setparams((1, 2, 22050, 0, "NONE", "not compressed"))
        pcm.writeframes(b"\x00\x00" * 22050 * 6)
    for source in (epub, audio):
        options = {"folder": str(inbox), "authorId": author, "filterExistingFiles": "false",
                   "replaceExistingFiles": "false"}
        choices = check_status(api.get("/api/v1/manualimport", params=options), 200, "find fixture file").json()
        entry = next(row for row in choices if Path(row["path"]) == source)
        command = check_status(api.post("/api/v1/command", data={
            "name": "ManualImport", "importMode": "copy", "replaceExistingFiles": False,
            "files": [{"path": str(source), "authorId": author, "bookId": book,
                       "foreignEditionId": EDITIONS[source.suffix][0], "quality": entry["quality"], "indexerFlags": 0}],
        }), 201, "enqueue copy import").json()
        response = check_status(api.get(f"/api/v1/command/{command['id']}"), 200, "import state").json()
        for _ in range(100):
            if response["status"] in ("completed", "failed", "aborted"):
                break
            time.sleep(.2)
            response = api.get(f"/api/v1/command/{command['id']}").json()
        assert response["status"] == "completed" and response["result"] == "successful", "Stable import failed"
    return {path.suffix: digest(path) for path in (epub, audio)}


def schema(profile):
    with open_db(profile) as db:
        assert db.execute("PRAGMA integrity_check").fetchall() == [("ok",)]
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        return sorted(row[0] for row in db.execute("SELECT Version FROM VersionInfo"))


def snapshot(api, profile, library, root, author, book, original):
    signed_root = check_status(api.get(f"/api/v1/rootfolder/{root}"), 200, "root identity").json()
    signed_author = check_status(api.get(f"/api/v1/author/{author}"), 200, "author identity").json()
    work = check_status(api.get(f"/api/v1/book/{book}"), 200, "work identity").json()
    settings = check_status(api.get("/api/v1/config/host"), 200, "persisted authentication settings").json()
    assert settings["username"] == "upgrade-fixture" and settings["instanceName"] == "Bibliophilarr upgrade fixture"
    assert settings["authenticationMethod"] == "forms" and not settings["password"]
    tracked = check_status(api.get("/api/v1/bookfile", params={"bookId": book}), 200, "tracked fixture files").json()
    assert signed_root["id"] == root and Path(signed_root["path"]) == library
    assert signed_author["id"] == author and work["id"] == book and work["authorId"] == author
    assert {row["formatType"]: (row["fileCount"], row["hasFile"]) for row in work["formatStatuses"]} == {
        "ebook": (1, True), "audiobook": (1, True)
    }
    with open_db(profile) as db:
        links = dict(db.execute("SELECT Id, EditionId FROM BookFiles"))
    editions = {row["foreignEditionId"]: row for row in work["editions"]}
    assert len(tracked) == len(links) == len(editions) == 2
    assert {row["id"] for row in tracked} == set(links)
    state = {}
    for row in tracked:
        path = Path(row["path"])
        assert path.is_relative_to(library) and path.is_file()
        external, _, ebook = EDITIONS[path.suffix]
        edition = editions[external]
        assert links[row["id"]] == edition["id"] and edition["isEbook"] is bool(ebook)
        assert digest(path) == original[path.suffix]
        state[path.suffix] = (row["id"], edition["id"], str(path), digest(path))
    canonical = (signed_author.get("canonicalId"), work.get("canonicalId"))
    return state, canonical


def browser_login(browser, base, username, password, evidence, canonical_book=None):
    with browser.new_context(viewport={"width": 1366, "height": 860}, service_workers="block") as context:
        context.route("**/*", lambda route: route.continue_() if route.request.url.startswith(base + "/") else route.abort())
        page = context.new_page()
        page.goto(base + "/books", wait_until="domcontentloaded")
        page.get_by_placeholder("Username").fill(username)
        page.get_by_placeholder("Password").fill(password)
        with page.expect_response(lambda response: urlsplit(response.url).path == "/login" and
                                  response.request.method == "POST") as response:
            page.get_by_placeholder("Password").press("Enter")
        check_status(response.value, 302, "signed Forms browser login")
        page.wait_for_url(base + "/books")
        assert any(cookie["name"].endswith("Auth") for cookie in context.cookies(base))
        book = page.get_by_role("link", name=TITLE, exact=True).first
        expect(book).to_be_visible()
        book.click()
        if canonical_book:
            page.goto(f"{base}/book/{BOOK_SLUG}", wait_until="domcontentloaded")
            page.wait_for_url(f"{base}/book/{canonical_book}")
        files = page.get_by_role("tab", name="Files (2)", exact=True)
        expect(files).to_be_visible()
        files.click()
        expect(files).to_have_attribute("aria-selected", "true")
        page.screenshot(path=str(evidence))


def verify(args, work, baseline_binary, candidate_binary, report):
    profile = work / "profile"
    profile.mkdir()
    port = free_port()
    base = f"http://127.0.0.1:{port}"
    fixture_key = secrets.token_hex(16)
    xml = ElementTree.Element("Config")
    for name, value in (("ApiKey", fixture_key), ("Port", str(port)), ("BindAddress", "127.0.0.1"), ("LaunchBrowser", "False"),
                        ("AnalyticsEnabled", "False")):
        ElementTree.SubElement(xml, name).text = value
    ElementTree.ElementTree(xml).write(profile / "config.xml", encoding="utf-8")
    username = "upgrade-fixture"
    password = "Disposable-" + secrets.token_urlsafe(24)
    library = work / "catalogue-library"

    with disallow_egress_proxy() as proxy, sync_playwright() as pw, pw.chromium.launch() as browser:
        http = pw.request.new_context()

        def app(binary, name, key=None):
            return launch(binary, profile, http, base, port, proxy, work, name, key, legacy=binary == baseline_binary)

        @contextmanager
        def signed(key):
            api = pw.request.new_context(base_url=base, extra_http_headers={"X-Api-Key": key})
            try:
                yield api
            finally:
                api.dispose()

        def version(api):
            status = check_status(api.get("/api/v1/system/status"), 200, "package version").json()
            assert Path(status["appData"]) == profile
            return status["version"]

        with app(baseline_binary, "stable-first-boot", fixture_key) as key, signed(key) as api:
            stable_version = version(api)
            assert stable_version.split(".")[:3] == ["1", "1", "0"]
            settings = check_status(api.get("/api/v1/config/host"), 200, "stable authentication").json()
            settings.update(username=username, password=password, passwordConfirmation=password,
                            authenticationMethod="forms", authenticationRequired="enabled", launchBrowser=False,
                            instanceName="Bibliophilarr upgrade fixture")
            check_status(api.put("/api/v1/config/host/1", data=settings), 202, "save fixture account")
            configure_providers(api)
            metadata = check_status(api.get("/api/v1/config/metadataprovider"), 200, "stable file settings").json()
            metadata.update(writeAudioTags="no", updateCovers=False, embedMetadata=False)
            check_status(api.put("/api/v1/config/metadataprovider/1", data=metadata), 202, "save file settings")
            root = initialize_root(api, work)["id"]
            root_detail = check_status(api.get(f"/api/v1/rootfolder/{root}"), 200, "stable root").json()

        author, book = seed_stable_catalogue(profile, library, root_detail)
        with app(baseline_binary, "stable-populated", key), signed(key) as api:
            original = fixture_import(api, work / "inbox", author, book)
            stable_state, stable_identity = snapshot(api, profile, library, root, author, book, original)
            assert stable_identity == (None, None)
            browser_login(browser, base, username, password, args.evidence / "stable.png")
        stable_schema = schema(profile)
        backup = work / "matching-stable-backup"
        shutil.copytree(profile, backup)
        assert content_manifest(backup) == content_manifest(profile) and schema(backup) == stable_schema

        with app(candidate_binary, "candidate-upgrade", key), signed(key) as api:
            candidate_version = version(api)
            assert candidate_version == args.expected_version, (candidate_version, args.expected_version)
            version_advanced = tuple(map(int, candidate_version.split("."))) > tuple(map(int, stable_version.split(".")))
            if args.require_forward_version:
                assert version_advanced, "Candidate assembly is not newer than stable"
            candidate_state, candidate_ids = snapshot(api, profile, library, root, author, book, original)
            assert candidate_state == stable_state
            assert None not in candidate_ids and len(set(candidate_ids)) == 2
            browser_login(browser, base, username, password, args.evidence / "candidate.png", candidate_ids[1])
        candidate_schema = schema(profile)
        assert set(candidate_schema) > set(stable_schema), "No forward schema migrations were observed"
        with app(candidate_binary, "candidate-restart", key), signed(key) as api:
            assert version(api) == candidate_version
            assert snapshot(api, profile, library, root, author, book, original) == (stable_state, candidate_ids)

        profile.rename(work / "retained-migrated-profile")
        shutil.copytree(backup, profile)
        assert content_manifest(backup) == content_manifest(profile)
        with app(baseline_binary, "stable-rollback", key), signed(key) as api:
            assert version(api) == stable_version
            assert snapshot(api, profile, library, root, author, book, original) == (stable_state, stable_identity)
            browser_login(browser, base, username, password, args.evidence / "restored-stable.png")
        assert schema(profile) == stable_schema
        assert proxy.attempts == 0, "The app attempted HTTP egress through the local rejecting proxy"
        report.update(stable_version=stable_version, candidate_version=candidate_version,
                      newly_applied_migrations=sorted(set(candidate_schema) - set(stable_schema)),
                      restored_stable_schema=True, stable_file_ids={ext: row[:2] for ext, row in stable_state.items()},
                      forms_login_verified=True, preserved_api_key=True, candidate_canonical_ids=candidate_ids,
                      verified_backup_copy=True, managed_http_proxy_attempts=proxy.attempts,
                      forward_assembly_version=version_advanced, upgrade_ui_eligibility_tested=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--stable-assets", type=Path, required=True)
    parser.add_argument("--candidate-assets", type=Path, required=True)
    parser.add_argument("--candidate-sha", required=True)
    parser.add_argument("--expected-version", required=True, help="Exact version reported by System Status")
    parser.add_argument("--require-forward-version", action="store_true")
    parser.add_argument("--evidence", type=Path, required=True, help="New directory for sanitized results")
    args = parser.parse_args()
    args.evidence = args.evidence.resolve()
    host = (platform.system(), platform.machine().lower())
    if host not in STABLE:
        parser.error("No published v1.1.0 portable archive matches this native host")
    rid, suffix, stable_check = STABLE[host]
    assert rid == args.rid, "The job must use a host matching the archive native architecture"
    assert re.fullmatch(r"[0-9a-f]{40}", args.candidate_sha)
    stable = args.stable_assets / f"bibliophilarr-1.1.0-{rid}{suffix}"
    assert digest(stable) == stable_check, "Published stable hash did not match the pinned checksum"
    assets = args.candidate_assets
    manifest_path = assets / "release-manifest.json"
    checksums = {name.removeprefix("./"): value for value, name in
                 (line.split(maxsplit=1) for line in (assets / "SHA256SUMS.txt").read_text().splitlines())}
    assert checksums[manifest_path.name] == digest(manifest_path)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert manifest["source"]["commit"] == args.candidate_sha
    assert manifest["source"]["repository"] == "Swartdraak/Bibliophilarr"
    artifact, = (entry for entry in manifest["artifacts"] if entry["rid"] == rid and entry["kind"] == "portable")
    name = f"bibliophilarr-{manifest['version']}-{rid}{suffix}"
    assert artifact["name"] == name
    candidate = assets / name
    assert digest(candidate) == artifact["sha256"] == checksums[name] and candidate.stat().st_size == artifact["bytes"]
    args.evidence.mkdir(parents=True)
    app_data_root = os.environ.get("RUNNER_TEMP")
    with TemporaryDirectory(prefix="bibliophilarr-paired-", dir=app_data_root) as temp:
        work = Path(temp).resolve()
        runtimes = framework_inventory()
        old, old_requirements = unpack(stable, work / "old-release", rid, runtimes)
        new, new_requirements = unpack(candidate, work / "new-release", rid, runtimes)
        report = dict(rid=rid, stable_sha=STABLE_COMMIT, stable_archive_sha256=stable_check,
                      candidate_sha=args.candidate_sha, candidate_archive_sha256=artifact["sha256"],
                      stable_frameworks=old_requirements, candidate_frameworks=new_requirements)
        verify(args, work, old, new, report)
        (args.evidence / "receipt.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
