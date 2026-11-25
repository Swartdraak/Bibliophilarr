"""Click Add New for a synthetic provider work. No SQL seeds or provider egress."""
import json
from pathlib import Path
import re
import secrets
from tempfile import TemporaryDirectory
from urllib.parse import urlsplit

from playwright.sync_api import expect, sync_playwright

from catalogue_workflow import checked, configure_providers, google_result_edition, initialize_root, wait_for_added_author_refresh
from smoke import bootstrap_headers, collect_page_errors, free_port, login, running_app, wait_ready

AUTHOR = 'Marin Fixture'
TITLE = 'Compass of the Fixture Coast'
VOLUME = 'fixtureCompass001'
WORK = f'googlebooks:work:{VOLUME}'
EDITION = f'googlebooks:edition:{VOLUME}'
VOLUME_RESOURCE = {'id': VOLUME, 'volumeInfo': {'title': TITLE, 'authors': [AUTHOR], 'language': 'en',
                   'printType': 'EBOOK', 'publishedDate': '2015-01-02', 'pageCount': 252,
                   'industryIdentifiers': [{'type': 'ISBN_13', 'identifier': '9780140328720'}]}}


def search_select(page, base):
    page.goto(base + '/add/search', wait_until='domcontentloaded')
    main = page.get_by_role('main')
    field = main.locator('input[placeholder*="War and Peace"]')
    with page.expect_response(lambda response: response.request.method == 'GET' and urlsplit(response.url).path == '/api/v1/search') as search:
        field.fill(TITLE)
    response = search.value
    rows = checked(response, 200, 'Browser Add New synthetic provider search')
    result, = [row for row in rows if row.get('book') and row.get('foreignId') == WORK]
    assert result['book']['title'] == TITLE and result['book']['author']['authorName'] == AUTHOR
    edition = google_result_edition(result)
    assert edition and edition['foreignEditionId'] == EDITION, 'The actual search response must carry the exact source edition'
    title = main.get_by_text(TITLE, exact=True)
    expect(title).to_have_count(1)
    title.locator('xpath=ancestor::div[button][1]').get_by_role('button').click()
    return result


def select_root(page, dialog, root):
    label = dialog.get_by_text('Root Folder', exact=True)
    control = label.locator('xpath=..').get_by_role('button')
    expect(control).to_have_count(1)
    control.click()
    page.get_by_text(f"[{root['name']}] {root['path']}", exact=False).last.click()


def run_browser_add_book(binary, evidence, tls, endpoints):
    credential = 'synthetic-google-' + secrets.token_urlsafe(20)
    login_data = {'username': 'book-add-fixture', 'password': 'Ci!' + secrets.token_urlsafe(20)}
    redaction = {**login_data, 'provider_fixture_key': credential}
    with TemporaryDirectory(prefix='bibliophilarr-browser-book-add-') as temp, sync_playwright() as pw, endpoints(tls, google_key=credential) as seen:
        work = Path(temp)
        profile = work / 'profile'
        port = free_port()
        public = pw.request.new_context()
        with pw.chromium.launch() as browser, browser.new_context(service_workers='block') as context:
            context.route('**/*', lambda route: route.continue_() if route.request.url.startswith(f'http://127.0.0.1:{port}/') else route.abort())
            page = context.new_page()
            page.set_default_timeout(10000)
            errors = collect_page_errors(page)
            first_log = work / 'browser-add-first.log'
            with running_app(binary, profile, first_log, port, evidence, redaction, tls_ca=tls / 'ca.crt') as (app, base):
                key = wait_ready(app, public, base, profile)
                checked(public.post(base + '/bootstrap', headers=bootstrap_headers(public, base), form=login_data, max_redirects=0), 200, 'Fresh browser add account')
                api = pw.request.new_context(base_url=base, extra_http_headers={'X-Api-Key': key})
                configure_providers(api, 'googlebooks', credential)
                route = '/api/v1/config/mediamanagement'
                content = checked(api.get(route), 200, 'Read browser add media settings')
                checked(api.put(route + '/1', data={**content, 'enableDualFormatTracking': False, 'watchLibraryForChanges': False,
                                                   'rescanAfterRefresh': 'never'}), 202, 'Configure a single disposable import format')
                root = initialize_root(api, work)
                assert checked(api.get('/api/v1/author'), 200, 'No pre-existing author') == []
                assert checked(api.get('/api/v1/book'), 200, 'No pre-existing work') == []
                page.goto(base + '/login?returnUrl=%2Fauthors')
                login(page, **login_data, rejected=False)
                found = search_select(page, base)
                dialog = page.get_by_role('dialog')
                expect(dialog.get_by_text(TITLE, exact=True)).to_be_visible()
                select_root(page, dialog, root)
                quality = checked(api.get('/api/v1/qualityprofile'), 200, 'Quality profiles')
                ebook = next(item['id'] for item in quality if item['name'].lower() == 'ebook')
                profiles = checked(api.get('/api/v1/metadataprofile'), 200, 'Metadata profiles')
                none = next(item['id'] for item in profiles if item['name'] == 'None')
                dialog.locator('select[name="qualityProfileId"]').select_option(str(ebook))
                dialog.locator('select[name="metadataProfileId"]').select_option(str(none))
                with page.expect_response(lambda response: response.request.method == 'POST' and urlsplit(response.url).path == '/api/v1/book') as submission:
                    dialog.get_by_role('button', name=f'Add {TITLE}', exact=True).click()
                created = checked(submission.value, 201, 'Browser Add New 201')
                payload = submission.value.request.post_data_json
                assert payload['foreignBookId'] == WORK and payload['author']['foreignAuthorId'] == found['book']['author']['foreignAuthorId']
                assert [edition['foreignEditionId'] for edition in payload['editions']] == [EDITION]
                assert payload['author']['rootFolderPath'] == root['path']
                assert payload['author']['qualityProfileId'] == ebook and payload['author']['metadataProfileId'] == none
                assert created['foreignBookId'] == WORK and created['foreignEditionId'] == EDITION
                book_id, author_id = created['id'], created['authorId']
                assert book_id > 0 and author_id > 0
                canonical_author = found['book']['author']['foreignAuthorId']
                refresh = wait_for_added_author_refresh(api, author_id, timeout=40)
                configure_providers(api)
                cases = [book_id, author_id]
                original = assert_library(page, api, base, cases, canonical_author)
            assert app.returncode == 0 and 'Received SIGTERM, initiating graceful shutdown' in first_log.read_text()
            count = len(seen)
            assert '/books/v1/volumes' in seen and f'/books/v1/volumes/{VOLUME}' in seen
            with running_app(binary, profile, work / 'browser-add-reopen.log', port, evidence, redaction, tls_ca=tls / 'ca.crt') as (reopened, base):
                assert reopened.pid != app.pid and wait_ready(reopened, public, base, profile) == key
                assert assert_library(page, api, base, cases, canonical_author) == original
                assert len(seen) == count
                assert not errors
            assert reopened.returncode == 0
    report = {'browserSearch': 200, 'browserAdd': 201, 'exactEdition': True, 'authorCreated': True, 'restart': True,
              'bookId': book_id, 'authorId': author_id, 'editionId': original[2], 'fixtureCalls': count,
              'authorRefreshCommandId': refresh['id'], 'authorRefreshResult': refresh['result'],
              'providerCallsDuringReopen': 0}
    (evidence / 'browser-add-book.json').write_text(json.dumps(report, indent=2) + '\n')
    print('Browser Add New synthetic source edition: PASS', flush=True)


def assert_library(page, api, base, ids, expected_author):
    book_id, author_id = ids
    book = checked(api.get(f'/api/v1/book/{book_id}'), 200, 'Read exact signed book')
    author = checked(api.get(f'/api/v1/author/{author_id}'), 200, 'Read exact signed author')
    assert author['foreignAuthorId'] == expected_author and book['authorId'] == author_id and book['foreignBookId'] == WORK
    edition, = [edition for edition in book['editions'] if edition['foreignEditionId'] == EDITION]
    assert book['foreignEditionId'] == EDITION and edition['id'] > 0 and edition['monitored'] and edition['isEbook']
    page.goto(base + '/books', wait_until='domcontentloaded')
    title = page.get_by_role('main').get_by_role('link', name=TITLE, exact=True)
    expect(title).to_be_visible(timeout=10000)
    title.click()
    expect(page).to_have_url(re.compile(r'/book/'))
    expect(page.get_by_role('main').get_by_text(TITLE, exact=True).first).to_be_visible(timeout=10000)
    return (book_id, author_id, edition['id'])
