"""Catalogue checks for the disposable production browser smoke app."""

from contextlib import closing
from dataclasses import dataclass, replace
from hashlib import sha256
import os
from pathlib import Path
import re
import sqlite3
from tempfile import TemporaryDirectory
import time
from urllib.parse import quote, urlsplit
from xml.sax.saxutils import escape, quoteattr
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile

from playwright.sync_api import expect


FIXTURE_AUTHOR = "Browser Fixture Author"
FIXTURE_TITLE = "The Fixture Observatory"
FIXTURE_AUTHOR_ID = "fixture:author:observatory"
FIXTURE_BOOK_ID = "fixture:work:observatory"
FIXTURE_EDITION_ID = "fixture:edition:observatory"
LIVE_PROVIDER_TOKEN_ENVS = {
    "hardcover": "HARDCOVER_API_KEY",
    "googlebooks": "GOOGLE_BOOKS_API_KEY",
}
LIVE_AUTHOR = "Jane Austen"
LIVE_TITLE = "Pride and Prejudice"


@dataclass(frozen=True)
class LiveBookIdentity:
    book_id: int
    foreign_book_id: str
    foreign_edition_id: str
    edition_id: int


@dataclass(frozen=True)
class CatalogueState:
    book_id: int
    foreign_book_id: str
    title: str
    title_slug: str
    file_id: int
    edition_id: int
    path: Path
    payload: dict[str, str]
    live_book: LiveBookIdentity | None = None


def checked(response, expected, label):
    assert response.status == expected, f"{label}: expected {expected}, got {response.status}"
    print(f"{label}: HTTP {expected}", flush=True)
    return response.json()


def verify_provider_config(settings, provider=None, token=""):
    assert settings["enableHardcoverFallback"] is (provider == "hardcover"), "Hardcover enable state"
    assert settings["enableGoogleBooksProvider"] is (provider == "googlebooks"), "Google Books enable state"
    for field in (
        "enableOpenLibraryProvider", "enableGoogleBooksFallback",
        "enableInventaireProvider", "enableInventaireFallback",
    ):
        assert settings[field] is False, f"{field} must be disabled"
    assert not token or provider in LIVE_PROVIDER_TOKEN_ENVS
    expected_hardcover = token if provider == "hardcover" else ""
    expected_google = token if provider == "googlebooks" else ""
    assert (settings.get("hardcoverApiToken") or "") == expected_hardcover, "Hardcover credential state"
    assert (settings.get("googleBooksApiKey") or "") == expected_google, "Google Books credential state"


def configure_providers(api, provider=None, token=""):
    assert provider is None or (provider in LIVE_PROVIDER_TOKEN_ENVS and token)
    route = "/api/v1/config/metadataprovider"
    settings = checked(api.get(route), 200, "read_metadata_provider_config")
    settings.update(
        enableHardcoverFallback=provider == "hardcover",
        enableOpenLibraryProvider=False,
        enableGoogleBooksProvider=provider == "googlebooks",
        enableGoogleBooksFallback=False,
        enableInventaireProvider=False,
        enableInventaireFallback=False,
        hardcoverApiToken=token if provider == "hardcover" else "",
        googleBooksApiKey=token if provider == "googlebooks" else "",
    )

    saved = checked(api.put(route + "/1", data=settings), 202, "save_metadata_provider_config")
    verify_provider_config(saved, provider, token)


def initialize_root(api, work):
    library = work / "catalogue-library"
    library.mkdir()
    quality = checked(api.get("/api/v1/qualityprofile"), 200, "read_catalogue_quality_profiles")
    ebook = next(profile for profile in quality if profile["name"].lower() == "ebook")
    profiles = checked(api.get("/api/v1/metadataprofile"), 200, "read_catalogue_metadata_profiles")
    none = next(profile for profile in profiles if profile["name"] == "None")
    route = "/api/v1/rootfolder"
    payload = {
        "name": "Catalogue fixture",
        "path": str(library),
        "defaultQualityProfileId": ebook["id"],
        "defaultMetadataProfileId": none["id"],
        "defaultMonitorOption": "none",
        "defaultNewItemMonitorOption": "all",
        "defaultTags": [],
        "isCalibreLibrary": False,
    }
    root = checked(api.post(route, data=payload), 201, "create_disposable_ebook_root")
    checked(api.post(route, data=payload), 400, "reject_duplicate_root")
    roots = checked(api.get(route), 200, "read_disposable_ebook_roots")
    persisted, = roots
    assert persisted["id"] == root["id"] and persisted["path"] == str(library)
    assert persisted["defaultNewItemMonitorOption"] == "all"
    return root


def author_at_root(author, root):
    return {
        **author,
        "rootFolderPath": root["path"],
        "qualityProfileId": root["defaultQualityProfileId"],
        "metadataProfileId": root["defaultMetadataProfileId"],
        "monitored": False,
        "monitorNewItems": "none",
        "addOptions": {"monitor": "none", "searchForMissingBooks": False},
    }


def google_result_edition(row):
    work_id = row.get("foreignId") or ""
    volume = re.fullmatch(r"googlebooks:work:([A-Za-z0-9_-]+)", work_id)
    if not volume:
        return None

    book = row.get("book") or {}
    expected = f"googlebooks:edition:{volume.group(1)}"
    editions = [item for item in book.get("editions") or [] if item.get("foreignEditionId") == expected]
    if book.get("foreignBookId") == work_id and len(editions) == 1:
        return editions[0]
    return None


def find_live_book(page, api, base, root, provider):
    page.goto(base + "/add/search", wait_until="domcontentloaded")
    field = page.get_by_role("main").locator('input[placeholder*="War and Peace"]')
    with page.expect_response(
        lambda response: urlsplit(response.url).path == "/api/v1/search"
        and response.request.method == "GET",
        timeout=90000,
    ) as search:
        field.fill(LIVE_TITLE if provider == "googlebooks" else f"{LIVE_TITLE} {LIVE_AUTHOR}")
    items = checked(search.value, 200, "real_provider_search_http")
    result = next((
        row for row in items
        if (row.get("book") or {}).get("title", "").casefold() == LIVE_TITLE.casefold()
        and ((row["book"].get("author") or {}).get("authorName", "").casefold() == LIVE_AUTHOR.casefold())
        and (row.get("foreignId") or "").startswith(f"{provider}:")
        and (provider != "googlebooks" or google_result_edition(row) is not None)
    ), None)
    assert result is not None, f"{provider} did not return the exact work with a usable identity"
    expect(page.get_by_role("main").get_by_text(result["book"]["title"], exact=True).first).to_be_visible()
    author = result["book"]["author"]
    assert author["foreignAuthorId"].startswith(f"{provider}:author:")
    print("real_provider_search: PASS (provider identity and browser result verified)", flush=True)
    payload = {
        **result["book"],
        "foreignBookId": result["foreignId"],
        "author": author_at_root(author, root),
        "monitored": False,
        "addOptions": {"searchForNewBook": False},
    }
    if provider == "googlebooks":
        edition = google_result_edition(result)
        payload["foreignEditionId"] = edition["foreignEditionId"]
        payload["editions"] = [{**edition, "monitored": True}]

    book = checked(api.post("/api/v1/book", data=payload, timeout=90000), 201, "add_book_http")
    assert book["id"] > 0 and book["authorId"] > 0
    assert book["foreignBookId"] == result["foreignId"]
    if provider == "googlebooks":
        assert book["foreignEditionId"] == payload["foreignEditionId"]
        stored, = [item for item in book["editions"] if item["foreignEditionId"] == payload["foreignEditionId"]]
        assert stored["id"] > 0 and stored["monitored"] is True
    return book, LIVE_AUTHOR


def read_author_row(app_data, author_id):
    with closing(sqlite3.connect((app_data / "bibliophilarr.db").as_uri() + "?mode=ro", uri=True)) as db:
        return db.execute("SELECT Id, AuthorMetadataId, Path FROM Authors WHERE Id = ?", (author_id,)).fetchone()


def catalogue_command(api, name, *, expected_status="completed", **body):
    queued = checked(api.post("/api/v1/command", data={"name": name, **body}), 201, f"queue_{name}")
    command_id = queued["id"]
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        command = checked(api.get(f"/api/v1/command/{command_id}"), 200, f"read_{name}_state")
        assert command["id"] == command_id and command["name"] == name
        if command["status"] not in ("queued", "started"):
            assert command["status"] == expected_status, f"{name}: unexpected terminal state {command['status']}"
            return command
        time.sleep(0.25)

    raise TimeoutError(f"{name} did not finish")


def verify_missing_author_rescan(api, app_data, root):
    with closing(sqlite3.connect((app_data / "bibliophilarr.db").as_uri() + "?mode=ro", uri=True)) as db:
        missing_id, = db.execute("SELECT COALESCE(MAX(Id), 0) + 1 FROM Authors").fetchone()
    library = Path(root["path"])
    assert not any(library.iterdir()), "missing-author regression needs an empty registered root"
    missing_folder = library / "missing-author"
    assert read_author_row(app_data, missing_id) is None

    command = catalogue_command(api, "RescanFolders", folders=[str(missing_folder)], filter="known",
                                addNewAuthors=False, authorIds=[missing_id])

    assert not command.get("exception")
    assert read_author_row(app_data, missing_id) is None
    print("missing_author_rescan: PASS (command completed for an absent author)", flush=True)


def verify_author_refresh(api, app_data, author):
    identifier = author["id"]
    original = read_author_row(app_data, identifier)
    assert original is not None and original[0] == identifier and original[1] > 0

    command = catalogue_command(api, "RefreshAuthor", authorId=identifier)
    assert command["result"] == "successful" and not command.get("exception")

    saved = checked(api.get(f"/api/v1/author/{identifier}"), 200, "author_after_refresh")
    assert saved["id"] == identifier and saved["foreignAuthorId"] == author["foreignAuthorId"]
    assert saved["titleSlug"] == author["titleSlug"] and read_author_row(app_data, identifier) == original
    print("author_refresh_retention: PASS (native refresh keeps API identity and SQLite row)", flush=True)


def verify_failed_command(api, http, base):
    command = catalogue_command(api, "DownloadedBooksScan", expected_status="failed")
    assert command["message"] == "Failed" and command["ended"]
    assert command["body"]["name"] == "DownloadedBooksScan"
    assert "A path must be provided" in command["exception"]
    ping = checked(http.get(base + "/ping"), 200, "ping_after_failed_command")
    assert ping["status"] == "OK"
    print("failed_command_json: PASS (failed command remains readable and app stays alive)", flush=True)


def seed_fixture_book(api, app_data, author_id, *, foreign_id, foreign_edition_id, slug, title, clean_title):
    with sqlite3.connect((app_data / "bibliophilarr.db").as_uri() + "?mode=rw", uri=True) as db:
        metadata_id, = db.execute(
            "SELECT AuthorMetadataId FROM Authors WHERE Id = ?", (author_id,)
        ).fetchone()
        inserted = db.execute(
            """
            INSERT INTO Books
                (AuthorMetadataId, ForeignBookId, TitleSlug, Title, CleanTitle, Monitored,
                 AnyEditionOk, Links, Genres, Ratings, AddOptions)
            VALUES (?, ?, ?, ?, ?, 0, 1, '[]', '[]', '{}', '{}')
            """,
            (metadata_id, foreign_id, slug, title, clean_title),
        )
        book_id = inserted.lastrowid
        db.execute(
            """
            INSERT INTO Editions
                (BookId, ForeignEditionId, Title, TitleSlug, Language, Format,
                 IsEbook, Monitored, ManualAdd, Images, Links, Ratings)
            VALUES (?, ?, ?, ?, 'en', 'Ebook', 1, 1, 1, '[]', '[]', '{}')
            """,
            (book_id, foreign_edition_id, title, f"{slug}-edition"),
        )

    book = checked(api.get(f"/api/v1/book/{book_id}"), 200, "read_seeded_fixture")
    assert book["authorId"] == author_id and book["foreignBookId"] == foreign_id
    return book


def seed_local_catalogue(page, api, base, root, app_data, *, existing_author_ids=()):
    route = "/api/v1/author"
    payload = author_at_root({
        "authorName": FIXTURE_AUTHOR,
        "foreignAuthorId": FIXTURE_AUTHOR_ID,
        "titleSlug": "browser-fixture-observatory-author",
    }, root)
    checked(api.post(route, data={**payload, "authorName": ""}), 400, "reject_invalid_author")
    author = checked(api.post(route, data=payload), 201, "create_fixture_author")
    checked(api.post(route, data=payload), 400, "reject_duplicate_author")
    authors = checked(api.get(route), 200, "read_catalogue_authors")
    assert sorted(item["id"] for item in authors) == sorted([author["id"], *existing_author_ids])
    persisted, = [item for item in authors if item["id"] == author["id"]]
    assert persisted["foreignAuthorId"] == FIXTURE_AUTHOR_ID
    verify_author_refresh(api, app_data, author)
    book = seed_fixture_book(api, app_data, author["id"], foreign_id=FIXTURE_BOOK_ID,
                            foreign_edition_id=FIXTURE_EDITION_ID, slug="browser-fixture-observatory",
                            title=FIXTURE_TITLE, clean_title="thefixtureobservatory")
    page.goto(base + "/authors", wait_until="domcontentloaded")
    search = page.get_by_role("textbox", name="Search books and authors")
    search.fill(FIXTURE_TITLE)
    expect(page.get_by_role("option").get_by_text(FIXTURE_TITLE, exact=True)).to_be_visible()
    print("local_catalogue_search: PASS (deterministic fixture)", flush=True)
    return book, FIXTURE_AUTHOR


def write_epub(path, title, author, identifier):
    escaped_title, escaped_author, escaped_id = map(escape, (title, author, identifier))
    with ZipFile(path, "w") as archive:
        archive.writestr("mimetype", "application/epub+zip", compress_type=ZIP_STORED)
        archive.writestr(
            "META-INF/container.xml",
            '<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">'
            '<rootfiles><rootfile full-path="OEBPS/book.opf" media-type="application/oebps-package+xml"/>'
            "</rootfiles></container>",
        )
        archive.writestr(
            "OEBPS/book.opf",
            '<package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="fixture-id">'
            '<metadata xmlns:dc="http://purl.org/dc/elements/1.1/">'
            f'<dc:identifier id="fixture-id">{escaped_id}</dc:identifier><dc:title>{escaped_title}</dc:title>'
            f"<dc:creator>{escaped_author}</dc:creator><dc:language>en</dc:language></metadata>"
            '<manifest><item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/>'
            '<item id="toc" href="toc.ncx" media-type="application/x-dtbncx+xml"/></manifest>'
            '<spine toc="toc"><itemref idref="chapter"/></spine></package>',
        )
        archive.writestr(
            "OEBPS/toc.ncx",
            '<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">'
            f'<head><meta name="dtb:uid" content={quoteattr(identifier)}/>'
            '<meta name="dtb:depth" content="1"/><meta name="dtb:totalPageCount" content="0"/>'
            '<meta name="dtb:maxPageNumber" content="0"/></head>'
            f'<docTitle><text>{escaped_title}</text></docTitle>'
            '<navMap><navPoint id="start" playOrder="1"><navLabel><text>Start</text></navLabel>'
            '<content src="chapter.xhtml"/></navPoint></navMap></ncx>',
        )
        archive.writestr(
            "OEBPS/chapter.xhtml",
            '<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Fixture</title></head>'
            "<body><p>A synthetic chapter for the Bibliophilarr browser workflow.</p></body></html>",
        )


def epub_payload(path):
    """Fingerprint each decompressed EPUB member independently of ZIP packaging."""
    with ZipFile(path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        assert len(names) == len(set(names)), "fixture EPUB has duplicate members"
        assert names and names[0] == "mimetype" and entries[0].compress_type == ZIP_STORED
        assert archive.read("mimetype") == b"application/epub+zip"
        return {name: sha256(archive.read(name)).hexdigest() for name in names}


def reembed_imported_epub(work, state):
    """Repack the tracked fixture while the disposable application is stopped."""
    target = state.path.resolve(strict=True)
    library = work.resolve() / "catalogue-library"
    assert target.is_relative_to(library) and not state.path.is_symlink(), "target must be inside the disposable library"
    assert target.suffix == ".epub"
    original_digest = sha256(target.read_bytes()).hexdigest()

    with TemporaryDirectory(prefix="reembed-", dir=target.parent) as scratch:
        rewritten = Path(scratch) / target.name
        with ZipFile(target) as original, ZipFile(rewritten, "w") as rebuilt:
            for name in original.namelist():
                compression = ZIP_STORED if name == "mimetype" else ZIP_DEFLATED
                rebuilt.writestr(name, original.read(name), compress_type=compression)
            rebuilt.comment = b"Bibliophilarr browser smoke re-embedding"

        assert sha256(rewritten.read_bytes()).hexdigest() != original_digest, "re-embedding must change the ZIP bytes"
        assert epub_payload(rewritten) == state.payload, "re-embedding must preserve the logical EPUB payload"
        rewritten.replace(target)

    print("epub_reembedding: PASS (ZIP bytes changed, all members preserved)", flush=True)


def verify_poor_local_import_selection(api, work, book):
    """An explicitly selected work can have a poor local match without a provider."""
    source = work / "poor-local-match.epub"
    write_epub(source, "The Unrelated Intermediate Spectrometer", "Unrelated Synthetic Writer",
               "urn:test:bibliophilarr-poor-match")
    original_digest = sha256(source.read_bytes()).hexdigest()
    config_route = "/api/v1/config/metadataprovider"
    settings = checked(api.get(config_route), 200, "read_poor_match_threshold")

    def events():
        response = api.get("/api/v1/log", params={
            "page": 1, "pageSize": 100, "sortKey": "time", "sortDirection": "descending"
        })
        assert response.status == 200, "read_manual_import_events"
        return response.json()["records"]

    try:
        saved = checked(api.put(config_route + "/1", data={**settings, "bookImportMatchThresholdPercent": 100}),
                        202, "require_exact_import_match")
        assert saved["bookImportMatchThresholdPercent"] == 100
        before = max((event["id"] for event in events()), default=0)
        result, = checked(api.post("/api/v1/manualimport", data=[{
            "path": str(source), "name": source.stem, "authorId": book["authorId"],
            "bookId": book["id"], "replaceExistingFiles": False,
        }], timeout=90000), 202, "poor_match_preview_without_providers")

        assert result["path"] == str(source) and result["book"]["id"] == book["id"]
        assert any(rejection["reason"].startswith("Book match is not close enough")
                   for rejection in result.get("rejections") or []), "Poor local match was not explicitly rejected"
        assert not files_for_book(api, book["id"])
        unmapped = checked(api.get("/api/v1/bookfile", params={"unmapped": "true"}),
                           200, "read_unmapped_after_rejected_import")
        assert all(Path(row["path"]) != source for row in unmapped), "Rejected file was tracked as unmapped"
        assert sha256(source.read_bytes()).hexdigest() == original_digest

        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            recent = [event for event in events() if event["id"] > before]
            summary = next((event["message"] for event in recent
                            if event["logger"] == "ImportDecisionMaker" and
                            event["message"].startswith("Import run complete: files=1 ")), None)
            if summary is not None:
                break
            time.sleep(0.15)
        else:
            raise AssertionError("Missing one-file import summary for the poor-match fixture")

        assert "poor=1," in summary and "remote_searches=1," in summary, summary
        assert not any((event.get("exceptionType") or "").endswith("ArgumentNullException") or
                       event["message"].startswith("Error identifying release")
                       for event in recent if event["logger"] in ("IdentificationService", "ImportDecisionMaker"))
        print("poor_local_import_without_providers: PASS (remote fallback and explicit per-file rejection)", flush=True)

        queued_import = catalogue_command(api, "ManualImport", files=[{
            "path": str(source), "bookId": book["id"], "authorId": 0,
        }], importMode="copy", replaceExistingFiles=False)
        assert queued_import["result"] == "unsuccessful" and queued_import["ended"], "Unresolved command reported success"
        assert not queued_import.get("exception"), "Unresolved command crashed"
        assert not files_for_book(api, book["id"])
        unmapped = checked(api.get("/api/v1/bookfile", params={"unmapped": "true"}),
                           200, "read_unmapped_after_unsuccessful_command")
        assert all(Path(row["path"]) != source for row in unmapped)
        assert sha256(source.read_bytes()).hexdigest() == original_digest
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            if any(event["id"] > before and event["logger"] == "ManualImportService" and
                   event["message"] == "No files were eligible for manual import." for event in events()):
                break
            time.sleep(0.15)
        else:
            raise AssertionError("Missing the unsuccessful manual import event")
        print("unresolved_manual_import_command: PASS (unsuccessful result, event and unchanged file)", flush=True)
    finally:
        checked(api.put(config_route + "/1", data=settings), 202, "restore_poor_match_threshold")


def render_library_and_detail(page, base, title, slug):
    main = page.get_by_role("main")
    page.goto(base + "/books", wait_until="domcontentloaded")
    expect(main.get_by_text(title, exact=True).first).to_be_visible(timeout=15000)
    page.goto(base + "/book/" + quote(slug, safe=""), wait_until="domcontentloaded")
    expect(main.get_by_text(title, exact=True).first).to_be_visible(timeout=15000)


def files_for_book(api, book_id):
    response = api.get("/api/v1/bookfile", params={"bookId": book_id})
    assert response.status == 200, "read_book_files"
    return response.json()


def verify_file_edition(app_data, file_id, edition_id):
    database = (app_data / "bibliophilarr.db").as_uri() + "?mode=ro"
    with closing(sqlite3.connect(database, uri=True)) as db:
        row = db.execute("SELECT EditionId FROM BookFiles WHERE Id = ?", (file_id,)).fetchone()
    assert row is not None, f"file {file_id} is missing from SQLite"
    assert row[0] == edition_id, f"file {file_id} links to edition {row[0]}, expected {edition_id}"


def import_fixture(api, work, app_data, root, book, author):
    inbox = work / "catalogue-inbox"
    inbox.mkdir()
    edition = next((item for item in book["editions"] if item.get("isEbook")), book["editions"][0])
    source = inbox / "catalogue-fixture.epub"
    write_epub(source, book["title"], author, edition.get("isbn13") or "urn:test:bibliophilarr")

    assert not files_for_book(api, book["id"])
    results = checked(api.get("/api/v1/manualimport", params={
        "folder": str(inbox), "authorId": book["authorId"],
        "filterExistingFiles": "false", "replaceExistingFiles": "false",
    }, timeout=90000), 200, "scan_fixture_epub")
    item = next(row for row in results if Path(row["path"]) == source)
    assert item["formatType"] == "ebook"
    assert item["book"]["id"] == book["id"]
    assert item["foreignEditionId"] == edition["foreignEditionId"]

    unresolved = checked(api.post("/api/v1/manualimport", data=[{
        "id": item["id"], "path": str(source), "name": item["name"],
    }]), 400, "reject_unresolved_manual_import")
    rejection, = unresolved
    assert rejection["path"] == str(source) and rejection["rejections"]
    assert not files_for_book(api, book["id"])

    selection = {
        "id": item["id"], "path": str(source), "name": item["name"],
        "authorId": book["authorId"], "bookId": book["id"],
        "foreignEditionId": item["foreignEditionId"], "quality": item["quality"],
        "indexerFlags": 0, "replaceExistingFiles": False,
    }
    updated, = checked(api.post("/api/v1/manualimport", data=[selection], timeout=90000),
                       202, "validate_manual_import_selection")
    assert updated["path"] == str(source) and updated["book"]["id"] == book["id"]
    assert updated["foreignEditionId"] == edition["foreignEditionId"] and not updated["rejections"]
    cmd = checked(api.post("/api/v1/command", data={
        "name": "ManualImport",
        "files": [selection],
        "importMode": "copy",
        "replaceExistingFiles": False,
    }), 201, "queue_epub_import")
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        response = api.get(f"/api/v1/command/{cmd['id']}")
        assert response.status == 200, "read_import_command"
        status = response.json()["status"]
        if status == "completed":
            break
        assert status not in ("failed", "aborted", "orphaned"), "epub_import_command_failed"
        time.sleep(0.25)
    else:
        raise TimeoutError("epub_import_command_not_completed")

    tracked, = files_for_book(api, book["id"])
    target = Path(tracked["path"])
    verify_file_edition(app_data, tracked["id"], edition["id"])
    assert tracked["id"] > 0 and target.is_file() and target.is_relative_to(Path(root["path"]))
    assert target.suffix == ".epub" and source.is_file()
    payload = epub_payload(target)
    assert payload["OEBPS/chapter.xhtml"] == epub_payload(source)["OEBPS/chapter.xhtml"]
    print("import: PASS (command completed, EPUB and tracking record verified)", flush=True)
    return CatalogueState(
        book["id"], book["foreignBookId"], book["title"], book["titleSlug"], tracked["id"], edition["id"],
        target, payload,
    )


def verify_downloaded_copy(api, work, app_data, root, author_id):
    title = "Fixture Almanac"
    book = seed_fixture_book(api, app_data, author_id, foreign_id="fixture:work:almanac",
                            foreign_edition_id="fixture:edition:almanac", slug="browser-fixture-almanac",
                            title=title, clean_title="fixturealmanac")
    edition, = book["editions"]
    assert not files_for_book(api, book["id"])
    inbox = work / "download-copy-inbox"
    inbox.mkdir()
    source = inbox / f"{FIXTURE_AUTHOR} - {title}.epub"
    write_epub(source, title, FIXTURE_AUTHOR, "urn:test:bibliophilarr-almanac")
    before = sha256(source.read_bytes()).hexdigest()

    command = catalogue_command(api, "DownloadedBooksScan", path=str(source), importMode="copy")
    assert command["result"] == "successful" and not command.get("exception")

    tracked, = files_for_book(api, book["id"])
    target = Path(tracked["path"]).resolve(strict=True)
    assert target.is_relative_to(Path(root["path"]).resolve()) and target.suffix == ".epub"
    verify_file_edition(app_data, tracked["id"], edition["id"])
    with ZipFile(source) as original, ZipFile(target) as copied:
        assert copied.read("OEBPS/chapter.xhtml") == original.read("OEBPS/chapter.xhtml")
    assert sha256(source.read_bytes()).hexdigest() == before
    print("downloaded_scan_copy: PASS (automatic copy kept the source and tracked the EPUB)", flush=True)


def exercise_catalogue(page, api, base, work, app_data, *, live_provider=None):
    """Run while the authenticated browser and first instance are alive."""
    address = urlsplit(base)
    assert address.scheme == "http" and address.hostname == "127.0.0.1" and address.port
    work, app_data = Path(work).resolve(), Path(app_data).resolve()
    assert app_data.parent == work and (app_data / "bibliophilarr.db").is_file()
    page.set_viewport_size({"width": 1366, "height": 900})
    root = initialize_root(api, work)
    token_env = LIVE_PROVIDER_TOKEN_ENVS.get(live_provider)
    token = os.environ.get(token_env, "").strip() if token_env else ""
    live_book = None
    if token:
        try:
            configure_providers(api, live_provider, token)
            book, author = find_live_book(page, api, base, root, live_provider)
        finally:
            configure_providers(api)

        saved = checked(api.get(f"/api/v1/book/{book['id']}"), 200, "read_live_provider_book")
        assert saved["foreignBookId"] == book["foreignBookId"] and saved["titleSlug"] == book["titleSlug"]
        print("add_book: PASS (201 and retrievable matching identity)", flush=True)
        if live_provider == "googlebooks":
            # The provider's volume can be print-only. The EPUB fixture has its own typed edition.
            edition, = [item for item in saved["editions"] if item["foreignEditionId"] == book["foreignEditionId"]]
            assert edition["id"] > 0 and edition["monitored"] is True
            live_book = LiveBookIdentity(saved["id"], saved["foreignBookId"], edition["foreignEditionId"], edition["id"])
            book, author = seed_local_catalogue(page, api, base, root, app_data, existing_author_ids=(saved["authorId"],))
    else:
        reason = f"{token_env} is unset or blank" if token_env else "opt-in live mode required"
        print(f"real_provider_search/add_book: SKIP ({reason})", flush=True)
        configure_providers(api)
        verify_missing_author_rescan(api, app_data, root)
        book, author = seed_local_catalogue(page, api, base, root, app_data)
        verify_failed_command(api, page.request, base)
        verify_poor_local_import_selection(api, work, book)
        verify_downloaded_copy(api, work, app_data, root, book["authorId"])

    readback = checked(api.get(f"/api/v1/book/{book['id']}"), 200, "retrievable_book_identity")
    assert readback["foreignBookId"] == book["foreignBookId"] and readback["titleSlug"] == book["titleSlug"]
    invalid = checked(api.post("/api/v1/book", data={"title": "Invalid fixture"}), 400, "negative_input_http")
    assert isinstance(invalid, list) and any(entry.get("propertyName") == "ForeignBookId" for entry in invalid)
    print("negative_input: PASS (documented 400 and ForeignBookId validation)", flush=True)
    render_library_and_detail(page, base, book["title"], book["titleSlug"])
    print("library_detail: PASS (library row and detail rendered)", flush=True)
    state = import_fixture(api, work, app_data, root, readback, author)
    return replace(state, live_book=live_book) if live_book else state


def verify_catalogue_after_restart(page, api, base, app_data, state):
    """Run after the existing smoke harness SIGTERMs and restarts the app on the same port."""
    if state.live_book:
        identity = state.live_book
        saved = checked(api.get(f"/api/v1/book/{identity.book_id}"), 200, "read_restarted_live_provider_book")
        assert saved["foreignBookId"] == identity.foreign_book_id
        edition, = [item for item in saved["editions"] if item["foreignEditionId"] == identity.foreign_edition_id]
        assert edition["id"] == identity.edition_id and edition["monitored"] is True

    book = checked(api.get(f"/api/v1/book/{state.book_id}"), 200, "read_restarted_catalogue")
    assert book["foreignBookId"] == state.foreign_book_id and book["titleSlug"] == state.title_slug
    tracked, = files_for_book(api, state.book_id)
    assert tracked["id"] == state.file_id and Path(tracked["path"]) == state.path
    verify_file_edition(app_data, state.file_id, state.edition_id)
    assert epub_payload(state.path) == state.payload, "logical EPUB payload changed after restart"
    render_library_and_detail(page, base, state.title, state.title_slug)
    print("persistence_after_restart: PASS (identity, re-embedded EPUB payload and browser detail)", flush=True)
