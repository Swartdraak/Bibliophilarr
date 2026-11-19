"""Catalogue checks for the disposable production browser smoke app."""

from dataclasses import dataclass
from hashlib import sha256
import os
from pathlib import Path
import sqlite3
import time
from urllib.parse import quote, urlsplit
from xml.sax.saxutils import escape, quoteattr
from zipfile import ZIP_STORED, ZipFile

from playwright.sync_api import expect


FIXTURE_AUTHOR = "Browser Fixture Author"
FIXTURE_TITLE = "The Fixture Observatory"
FIXTURE_AUTHOR_ID = "fixture:author:observatory"
FIXTURE_BOOK_ID = "fixture:work:observatory"
FIXTURE_EDITION_ID = "fixture:edition:observatory"
LIVE_PROVIDER_TOKEN_ENV = "HARDCOVER_API_KEY"
LIVE_AUTHOR = "Jane Austen"
LIVE_TITLE = "Pride and Prejudice"


@dataclass(frozen=True)
class CatalogueState:
    book_id: int
    foreign_book_id: str
    title: str
    title_slug: str
    file_id: int
    path: Path
    digest: str


def checked(response, expected, label):
    assert response.status == expected, f"{label}: expected {expected}, got {response.status}"
    print(f"{label}: HTTP {expected}", flush=True)
    return response.json()


def configure_providers(api, live, *, clear_token=False):
    route = "/api/v1/config/metadataprovider"
    settings = checked(api.get(route), 200, "read_metadata_provider_config")
    settings.update(
        enableHardcoverFallback=live,
        enableOpenLibraryProvider=False,
        enableGoogleBooksFallback=False,
        enableInventaireFallback=False,
    )
    if live:
        token = os.environ.get(LIVE_PROVIDER_TOKEN_ENV)
        if not token or not token.strip():
            raise RuntimeError("The opt-in live workflow requires HARDCOVER_API_KEY")
        settings["hardcoverApiToken"] = token.strip()
    if clear_token:
        settings["hardcoverApiToken"] = ""

    saved = checked(api.put(route + "/1", data=settings), 202, "save_metadata_provider_config")
    assert saved["enableHardcoverFallback"] is live
    if clear_token:
        assert not saved.get("hardcoverApiToken")


def initialize_root(api, work):
    library = work / "catalogue-library"
    library.mkdir()
    quality = checked(api.get("/api/v1/qualityprofile"), 200, "read_catalogue_quality_profiles")
    ebook = next(profile for profile in quality if profile["name"].lower() == "ebook")
    profiles = checked(api.get("/api/v1/metadataprofile"), 200, "read_catalogue_metadata_profiles")
    none = next(profile for profile in profiles if profile["name"] == "None")
    return checked(
        api.post("/api/v1/rootfolder", data={
            "name": "Catalogue fixture",
            "path": str(library),
            "defaultQualityProfileId": ebook["id"],
            "defaultMetadataProfileId": none["id"],
            "defaultMonitorOption": "none",
            "defaultNewItemMonitorOption": "none",
            "defaultTags": [],
            "isCalibreLibrary": False,
        }),
        201,
        "create_disposable_ebook_root",
    )


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


def find_live_book(page, api, base, root):
    page.goto(base + "/add/search", wait_until="domcontentloaded")
    field = page.get_by_role("main").locator('input[placeholder*="War and Peace"]')
    with page.expect_response(
        lambda response: urlsplit(response.url).path == "/api/v1/search"
        and response.request.method == "GET",
        timeout=90000,
    ) as search:
        field.fill(f"{LIVE_TITLE} {LIVE_AUTHOR}")
    items = checked(search.value, 200, "real_provider_search_http")
    result = next(
        row for row in items
        if (row.get("book") or {}).get("title", "").casefold() == LIVE_TITLE.casefold()
        and ((row["book"].get("author") or {}).get("authorName", "").casefold() == LIVE_AUTHOR.casefold())
        and row.get("foreignId", "").startswith("hardcover:")
    )
    expect(page.get_by_role("main").get_by_text(result["book"]["title"], exact=True).first).to_be_visible()
    author = result["book"]["author"]
    assert author["foreignAuthorId"].startswith("hardcover:")
    print("real_provider_search: PASS (provider identity and browser result verified)", flush=True)
    payload = {
        **result["book"],
        "foreignBookId": result["foreignId"],
        "author": author_at_root(author, root),
        "monitored": False,
        "addOptions": {"searchForNewBook": False},
    }
    book = checked(api.post("/api/v1/book", data=payload, timeout=90000), 201, "add_book_http")
    assert book["id"] > 0 and book["authorId"] > 0
    assert book["foreignBookId"] == result["foreignId"]
    return book, LIVE_AUTHOR


def seed_local_catalogue(page, api, base, root, app_data):
    author = checked(
        api.post("/api/v1/author", data=author_at_root({
            "authorName": FIXTURE_AUTHOR,
            "foreignAuthorId": FIXTURE_AUTHOR_ID,
            "titleSlug": "browser-fixture-observatory-author",
        }, root)),
        201,
        "create_fixture_author",
    )
    with sqlite3.connect((app_data / "bibliophilarr.db").as_uri() + "?mode=rw", uri=True) as db:
        metadata_id, = db.execute(
            "SELECT AuthorMetadataId FROM Authors WHERE Id = ?", (author["id"],)
        ).fetchone()
        inserted = db.execute(
            """
            INSERT INTO Books
                (AuthorMetadataId, ForeignBookId, TitleSlug, Title, CleanTitle, Monitored,
                 AnyEditionOk, Links, Genres, Ratings, AddOptions)
            VALUES (?, ?, ?, ?, ?, 0, 1, '[]', '[]', '{}', '{}')
            """,
            (metadata_id, FIXTURE_BOOK_ID, "browser-fixture-observatory", FIXTURE_TITLE,
             "thefixtureobservatory"),
        )
        book_id = inserted.lastrowid
        db.execute(
            """
            INSERT INTO Editions
                (BookId, ForeignEditionId, Title, TitleSlug, Language, Format,
                 IsEbook, Monitored, ManualAdd, Images, Links, Ratings)
            VALUES (?, ?, ?, ?, 'en', 'Ebook', 1, 1, 1, '[]', '[]', '{}')
            """,
            (book_id, FIXTURE_EDITION_ID, FIXTURE_TITLE, "browser-fixture-observatory-edition"),
        )

    book = checked(api.get(f"/api/v1/book/{book_id}"), 200, "read_seeded_fixture")
    assert book["authorId"] == author["id"] and book["foreignBookId"] == FIXTURE_BOOK_ID
    page.goto(base + "/authors", wait_until="domcontentloaded")
    search = page.get_by_role("textbox", name="Search books and authors")
    search.fill(FIXTURE_TITLE)
    expect(page.get_by_role("option").get_by_text(FIXTURE_TITLE, exact=True)).to_be_visible()
    print("local_catalogue_search: PASS (deterministic fixture)", flush=True)
    print("real_provider_search/add_book: SKIP (opt-in live mode required)", flush=True)
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


def import_fixture(api, work, root, book, author):
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
    cmd = checked(api.post("/api/v1/command", data={
        "name": "ManualImport",
        "files": [{
            "path": str(source), "authorId": book["authorId"], "bookId": book["id"],
            "foreignEditionId": edition["foreignEditionId"], "quality": item["quality"],
            "indexerFlags": 0,
        }],
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
    assert tracked["id"] > 0 and target.is_file() and target.is_relative_to(Path(root["path"]))
    assert target.suffix == ".epub" and source.is_file()
    with ZipFile(target) as archive:
        assert archive.testzip() is None
    print("import: PASS (command completed, EPUB and tracking record verified)", flush=True)
    return CatalogueState(
        book["id"], book["foreignBookId"], book["title"], book["titleSlug"], tracked["id"],
        target, sha256(target.read_bytes()).hexdigest(),
    )


def exercise_catalogue(page, api, base, work, app_data, *, live_provider=False):
    """Run while the authenticated browser and first instance are alive."""
    address = urlsplit(base)
    assert address.scheme == "http" and address.hostname == "127.0.0.1" and address.port
    work, app_data = Path(work).resolve(), Path(app_data).resolve()
    assert app_data.parent == work and (app_data / "bibliophilarr.db").is_file()
    page.set_viewport_size({"width": 1366, "height": 900})
    root = initialize_root(api, work)
    if live_provider:
        try:
            configure_providers(api, True)
            book, author = find_live_book(page, api, base, root)
        finally:
            configure_providers(api, False, clear_token=True)
    else:
        configure_providers(api, False)
        book, author = seed_local_catalogue(page, api, base, root, app_data)
    readback = checked(api.get(f"/api/v1/book/{book['id']}"), 200, "retrievable_book_identity")
    assert readback["foreignBookId"] == book["foreignBookId"] and readback["titleSlug"] == book["titleSlug"]
    if live_provider:
        print("add_book: PASS (201 and retrievable matching identity)", flush=True)
    invalid = checked(api.post("/api/v1/book", data={"title": "Invalid fixture"}), 400, "negative_input_http")
    assert isinstance(invalid, list) and any(entry.get("propertyName") == "ForeignBookId" for entry in invalid)
    print("negative_input: PASS (documented 400 and ForeignBookId validation)", flush=True)
    render_library_and_detail(page, base, book["title"], book["titleSlug"])
    print("library_detail: PASS (library row and detail rendered)", flush=True)
    return import_fixture(api, work, root, readback, author)


def verify_catalogue_after_restart(page, api, base, state):
    """Run after the existing smoke harness SIGTERMs and restarts the app on the same port."""
    book = checked(api.get(f"/api/v1/book/{state.book_id}"), 200, "read_restarted_catalogue")
    assert book["foreignBookId"] == state.foreign_book_id and book["titleSlug"] == state.title_slug
    tracked, = files_for_book(api, state.book_id)
    assert tracked["id"] == state.file_id and Path(tracked["path"]) == state.path
    assert sha256(state.path.read_bytes()).hexdigest() == state.digest
    render_library_and_detail(page, base, state.title, state.title_slug)
    print("persistence_after_restart: PASS (identity, imported file, content and browser detail)", flush=True)
