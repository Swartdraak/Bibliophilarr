"""Verify the production WebUI against a disposable, loopback-only Linux app."""

import argparse
from contextlib import closing, contextmanager
import os
from pathlib import Path
import re
import secrets
import socket
import sqlite3
import subprocess
from tempfile import TemporaryDirectory
import time
from urllib.parse import urlsplit
from xml.etree import ElementTree

from playwright.sync_api import Error as PlaywrightError, expect, sync_playwright

from catalogue_workflow import (
    LIVE_PROVIDER_TOKEN_ENV,
    exercise_catalogue,
    verify_catalogue_after_restart,
)


def check_status(response, expected, label):
    print(f"{label}: HTTP {response.status}, expected {expected}", flush=True)
    assert response.status == expected, label
    return response


def wait_ready(process, http, base, app_data):
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        if process.poll() is not None:
            print(f"Application exited before readiness: {process.returncode}", flush=True)
            raise RuntimeError("application exited")
        try:
            response = http.get(base + "/ping", timeout=1000, max_redirects=0)
            if response.ok and response.json().get("status") == "OK":
                config = ElementTree.parse(app_data / "config.xml").getroot()
                key = config.findtext("ApiKey")
                if config.tag == "Config" and key and (app_data / "bibliophilarr.db").is_file():
                    return key
        except (PlaywrightError, FileNotFoundError, ElementTree.ParseError, ValueError):
            pass
        time.sleep(0.2)

    raise TimeoutError("fresh startup did not produce ping, config and database")


def check_native_profile(http, api, base, app_data, key, stage):
    ping = check_status(http.get(base + "/ping", max_redirects=0), 200, f"{stage} ping")
    assert ping.headers["content-type"].split(";", 1)[0] == "application/json"
    assert ping.json().get("status") == "OK"
    config = ElementTree.parse(app_data / "config.xml").getroot()
    assert config.tag == "Config", f"{stage} config has the wrong root element"
    assert key and config.findtext("ApiKey") == key, f"{stage} config key did not match"
    assert (app_data / "bibliophilarr.db").is_file(), f"{stage} main database missing"
    check_status(http.get(base + "/", max_redirects=0), 401, f"{stage} anonymous root")
    system = check_status(api.get("/api/v1/system/status", max_redirects=0), 200, f"{stage} keyed system status").json()
    assert Path(system["appData"]).resolve() == app_data.resolve(), f"{stage} system profile did not match"
    print(f"{stage} native profile verified (config, API key and database)", flush=True)


def check_bootstrap_user(app_data, credentials=None):
    database = (app_data / "bibliophilarr.db").as_uri() + "?mode=ro"
    with closing(sqlite3.connect(database, uri=True)) as connection:
        users = connection.execute("SELECT Username FROM Users").fetchall()
    expected = [(credentials["username"].lower(),)] if credentials else []
    assert users == expected, "bootstrap user did not match the persisted profile"


def bootstrap_headers(http, base):
    response = check_status(http.get(base + "/bootstrap", max_redirects=0), 200, "Read terminal setup token")
    cache_control = response.headers.get("cache-control", "")
    assert "no-store" in cache_control and "max-age" not in cache_control, "terminal setup token can be cached"
    token = response.json()
    header_name = token.get("headerName")
    request_token = token.get("requestToken")
    assert header_name and request_token, "terminal setup token response is incomplete"
    return {header_name: request_token}


def free_port():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


@contextmanager
def running_app(binary, app_data, log_path, port, evidence, credentials):
    app_data.mkdir(exist_ok=True)
    base = f"http://127.0.0.1:{port}"
    env = {
        name: os.environ[name]
        for name in ("PATH", "LANG", "LC_ALL", "DOTNET_ROOT", "DOTNET_ROOT_X64")
        if name in os.environ
    }
    env.update(
        ASPNETCORE_ENVIRONMENT="Production",
        BIBLIOPHILARR__SERVER__PORT=str(port),
        BIBLIOPHILARR__SERVER__BINDADDRESS="127.0.0.1",
        BIBLIOPHILARR__LOG__ANALYTICSENABLED="false",
    )
    with log_path.open("ab") as log:
        process = subprocess.Popen(
            [str(binary), f"/data={app_data}", "/nobrowser", "/nosingleinstancecheck"],
            cwd=binary.parent,
            env=env,
            stdout=log,
            stderr=subprocess.STDOUT,
        )
        failed = False
        forced_stop = False
        try:
            yield process, base
        except Exception:
            failed = True
            raise
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
                    forced_stop = True
            if failed or forced_stop:
                log.flush()
                safe_log = log_path.read_text(errors="replace")
                secrets_to_remove = list(credentials.values()) + [
                    "deliberately-wrong",
                    os.environ.get(LIVE_PROVIDER_TOKEN_ENV, "").strip(),
                ]
                try:
                    secrets_to_remove.append(ElementTree.parse(app_data / "config.xml").findtext("ApiKey"))
                except (FileNotFoundError, ElementTree.ParseError):
                    pass
                for value in filter(None, secrets_to_remove):
                    safe_log = safe_log.replace(value, "[redacted]")
                (evidence / log_path.name).write_text(safe_log)
            if forced_stop and not failed:
                raise TimeoutError("application did not stop after SIGTERM")


def login(page, username, password, *, rejected, return_path="/authors"):
    print(f"Checking {'rejected' if rejected else 'successful'} Forms submission", flush=True)
    page.get_by_placeholder("Username").fill(username)
    page.get_by_placeholder("Password").fill(password)
    with page.expect_response(
        lambda response: response.request.method == "POST" and urlsplit(response.url).path == "/login"
    ) as submitted:
        page.get_by_placeholder("Password").press("Enter")

    response = check_status(submitted.value, 302, "Forms POST redirects")
    location = response.header_value("location") or ""
    cookie = response.header_value("set-cookie") or ""
    if rejected:
        assert "loginFailed=true" in location and "BibliophilarrAuth=" not in cookie
        page.wait_for_url(re.compile(r"loginFailed=true"))
    else:
        assert urlsplit(location).path == return_path and "BibliophilarrAuth=" in cookie
        page.wait_for_url(re.compile(re.escape(return_path) + r"$"))

    assert any(item["name"] == "BibliophilarrAuth" for item in page.context.cookies()) != rejected


def check_routes(page, base, api, evidence):
    main = page.get_by_role("main")
    nav = page.get_by_role("navigation", name="Primary navigation")

    print("Desktop library navigation and search", flush=True)
    expect(main.get_by_role("heading", name="Authors", exact=True)).to_be_visible()
    expect(main.get_by_role("heading", name="No authors found", exact=True)).to_be_visible()
    page.screenshot(path=str(evidence / "desktop-authors.png"))
    search = page.get_by_role("textbox", name="Search books and authors")
    page.keyboard.press("s")
    expect(search).to_be_focused()
    search.fill("browser fixture")
    expect(page.get_by_role("option", name="Search for browser fixture")).to_be_visible()
    search.press("Tab")
    expect(search).not_to_be_focused()
    expect(page).to_have_url(base + "/authors")
    nav.get_by_role("link", name="Books", exact=True).click()
    expect(page).to_have_url(base + "/books")
    expect(main.get_by_role("heading", name="No books found", exact=True)).to_be_visible()
    expect(nav.get_by_role("link", name="Books", exact=True)).to_have_attribute("aria-current", "page")

    print("Library and Search bookmarks mount distinct content", flush=True)
    check_status(page.goto(base + "/library?view=table#authors"), 200, "Authenticated Library bookmark")
    expect(page).to_have_url(base + "/authors?view=table#authors")
    expect(main.get_by_role("heading", name="Authors", exact=True)).to_be_visible()
    check_status(page.goto(base + "/search?source=bookmark#search"), 200, "Authenticated Search bookmark")
    expect(page).to_have_url(base + "/add/search?source=bookmark#search")
    expect(main.locator('input[placeholder*="War and Peace"]')).to_be_visible()
    page.screenshot(path=str(evidence / "desktop-search.png"))
    assert (evidence / "desktop-authors.png").read_bytes() != (evidence / "desktop-search.png").read_bytes()

    print("Settings mount real controls and save without re-entering the password", flush=True)
    page.goto(base + "/settings", wait_until="domcontentloaded")
    main.get_by_role("link", name="Media Management", exact=False).first.click()
    expect(page).to_have_url(base + "/settings/mediamanagement")
    expect(main.get_by_text("Root Folders", exact=True)).to_be_visible()
    page.goto(base + "/settings/general", wait_until="domcontentloaded")
    expect(main.get_by_text("Authentication", exact=True).first).to_be_visible()
    expect(main.get_by_role("button", name="Forms (Login Page)", exact=True)).to_be_visible()
    page.get_by_text("Show Advanced", exact=True).click()
    name = main.locator('input[name="instanceName"]')
    original = name.input_value()
    for value in ("Bibliophilarr Browser Fixture", original):
        name.fill(value)
        with page.expect_response(
            lambda response: response.request.method == "PUT"
            and urlsplit(response.url).path in {"/api/v1/config/host", "/api/v1/config/host/1"}
        ) as saved:
            main.get_by_role("button", name="Save Changes", exact=True).click()
        check_status(saved.value, 202, "General settings save with existing credentials")
        expect(main.get_by_role("button", name="No Changes", exact=True)).to_be_disabled()
        host = check_status(api.get("/api/v1/config/host"), 200, "Read back instance name").json()
        assert host["instanceName"] == value and host["password"] == ""
    page.screenshot(
        path=str(evidence / "desktop-general-settings.png"),
        mask=[main.locator('input[name="apiKey"]')],
    )
    host_config_route = re.compile(r"/api/v1/config/host(?:/1)?(?:\?.*)?$")

    def fail_host_settings(route):
        if route.request.method == "GET":
            route.fulfill(status=503, json={"message": "Fixture service unavailable"})
        else:
            route.fallback()

    page.route(host_config_route, fail_host_settings)
    try:
        page.reload(wait_until="domcontentloaded")
        expect(main.get_by_text("Unable to load General settings", exact=True)).to_be_visible()
    finally:
        page.unroute(host_config_route, fail_host_settings)
    page.reload(wait_until="domcontentloaded")
    expect(main.get_by_role("button", name="Forms (Login Page)", exact=True)).to_be_visible()
    expect(main.locator('input[name="instanceName"]')).to_be_visible()
    print("General settings recovery: PASS (visible error and restored Forms controls)", flush=True)
    nav.get_by_role("link", name="UI", exact=True).click()
    expect(page).to_have_url(base + "/settings/ui")
    main.get_by_role("button", name="Auto", exact=True).click()
    page.get_by_text("Dark", exact=True).click()
    main.get_by_role("button", name="Save Changes", exact=True).click()
    expect(main.get_by_role("button", name="No Changes", exact=True)).to_be_disabled()

    for width in (390, 320):
        print(f"Keyboard navigation on {width}px viewport", flush=True)
        page.set_viewport_size({"width": width, "height": 844})
        page.goto(base + "/authors", wait_until="domcontentloaded")
        expect(main.get_by_role("heading", name="Authors", exact=True)).to_be_visible()
        toggle = page.get_by_role("button", name="Primary navigation")
        expect(toggle).to_have_attribute("aria-expanded", "false")
        expect(nav).to_have_count(0)
        assert page.evaluate("document.documentElement.scrollWidth === innerWidth"), f"overflow at {width}px"
        toggle.focus()
        page.keyboard.press("Enter")
        expect(toggle).to_have_attribute("aria-expanded", "true")
        expect(nav.get_by_role("link", name="Library", exact=True)).to_be_focused()
        if width == 390:
            page.screenshot(path=str(evidence / "mobile-navigation.png"))
        page.keyboard.press("Escape")
        expect(toggle).to_have_attribute("aria-expanded", "false")
        expect(toggle).to_be_focused()


def check_metadata_settings(page, base, api):
    page.set_viewport_size({"width": 1366, "height": 900})
    check_status(page.goto(base + "/settings"), 200, "Authenticated Settings")
    main = page.get_by_role("main")
    metadata_link = main.get_by_role("link", name="Metadata", exact=True)
    expect(metadata_link).to_have_attribute("href", "/settings/metadata")
    metadata_link.click()
    expect(page).to_have_url(base + "/settings/metadata")

    timeout_input = main.get_by_role("spinbutton", name="Provider Timeout (seconds)")
    expect(timeout_input).to_be_visible()
    timeout = 41 if timeout_input.input_value() != "41" else 43
    timeout_input.fill(str(timeout))
    with page.expect_response(
        lambda response: response.request.method == "PUT"
        and urlsplit(response.url).path.lower() == "/api/v1/config/metadataprovider"
    ) as saved:
        main.get_by_role("button", name="Save Changes", exact=True).click()
    check_status(saved.value, 202, "Metadata settings save from the browser")
    expect(main.get_by_role("button", name="No Changes", exact=True)).to_be_disabled()

    check_status(page.reload(), 200, "Reload authenticated Metadata settings")
    expect(timeout_input).to_have_value(str(timeout))
    config = check_status(api.get("/api/v1/config/metadataprovider"), 200, "Read back browser metadata setting").json()
    assert config["metadataProviderTimeoutSeconds"] == timeout, "browser metadata change did not persist"
    print("Metadata settings persisted after browser reload", flush=True)
    return timeout


def run(binary, evidence, *, live_provider=False):
    username = "browser-fixture"
    password = "Ci!" + secrets.token_urlsafe(16)
    credentials = {"username": username, "password": password}
    with TemporaryDirectory(prefix="bibliophilarr-browser-") as work, sync_playwright() as playwright:
        work = Path(work)
        http = playwright.request.new_context()
        weak_data = work / "weak-password"
        with running_app(binary, weak_data, work / "weak.log", free_port(), evidence, credentials) as (app, base):
            wait_ready(app, http, base, weak_data)
            setup_headers = bootstrap_headers(http, base)
            check_status(
                http.post(base + "/bootstrap", headers=setup_headers,
                          form={"username": username, "password": "weak"}, max_redirects=0),
                400,
                "Fresh user with weak password",
            )
            check_bootstrap_user(weak_data)

        onboarding = work / "onboarding"
        with running_app(binary, onboarding, work / "onboarding.log", free_port(), evidence, credentials) as (app, base):
            wait_ready(app, http, base, onboarding)
            check_status(http.post(base + "/bootstrap", form=credentials, headers={"X-Forwarded-For": "127.0.0.1"}, max_redirects=0),
                         403, "Rejected forwarded first-account request")
            check_status(http.post(base + "/bootstrap", form=credentials, headers={"Origin": "https://untrusted.invalid"}, max_redirects=0),
                         403, "Rejected cross-origin terminal bootstrap")
            check_status(http.get(base + "/bootstrap", headers={"X-Forwarded-For": "127.0.0.1"}, max_redirects=0),
                         403, "Rejected forwarded terminal setup token request")
            check_status(http.post(base + "/login/setup", form=credentials, max_redirects=0),
                         400, "First-account form without its request token")
            with playwright.chromium.launch() as onboarding_browser:
                page = onboarding_browser.new_page()
                page.goto(base + "/")
                expect(page.get_by_role("heading", name="Create your account")).to_be_visible(timeout=5000)
                page.get_by_label("Username").fill(username)
                page.get_by_label("Password", exact=True).fill(password)
                page.get_by_label("Confirm password").fill("Deliberate-Mismatch-01")
                page.get_by_role("button", name="Create account").click()
                expect(page.get_by_role("alert")).to_contain_text("matching passwords")
                page.get_by_label("Username").fill(username)
                page.get_by_label("Password", exact=True).fill(password)
                page.get_by_label("Confirm password").fill(password)
                page.get_by_role("button", name="Create account").click()
                expect(page.get_by_role("main").get_by_role("heading", name="Authors", exact=True)).to_be_visible()
                check_status(page.request.get(base + "/initialize.json"), 200, "First account can access the WebUI")
                page.get_by_role("button", name="Menu Button").click()
                page.get_by_role("button", name="Logout", exact=True).click()
                expect(page.get_by_role("button", name="Login", exact=True)).to_be_visible()
                expect(page.get_by_role("heading", name="Create your account")).to_have_count(0)

        data = work / "main-app"
        log = work / "app.log"
        port = free_port()
        with running_app(binary, data, log, port, evidence, credentials) as (app, base):
            key = wait_ready(app, http, base, data)
            api = playwright.request.new_context(base_url=base, extra_http_headers={"X-Api-Key": key})
            check_native_profile(http, api, base, data, key, "Startup")
            check_status(http.get(base + "/authors", max_redirects=0), 401, "Anonymous library")
            check_status(http.get(base + "/api/v1/search?term=fixture", max_redirects=0), 401, "Anonymous API search")
            denied = check_status(http.get(base + "/initialize.json", max_redirects=0), 401, "Anonymous initializer")
            assert key.encode() not in denied.body()
            check_status(http.post(base + "/bootstrap", form=credentials, max_redirects=0), 400,
                         "Terminal setup without its request token")
            setup_headers = bootstrap_headers(http, base)
            for invalid_username in ("ab", "a" * 33):
                invalid = {**credentials, "username": invalid_username}
                check_status(http.post(base + "/bootstrap", headers=setup_headers, form=invalid, max_redirects=0),
                             400, "Invalid first username")
            check_bootstrap_user(data)
            initial_user = {**credentials, "username": username.upper()}
            check_status(http.post(base + "/bootstrap", headers=setup_headers, form=initial_user, max_redirects=0),
                         200, "First user")
            check_bootstrap_user(data, credentials)
            check_status(http.post(base + "/bootstrap", headers=setup_headers, form=credentials, max_redirects=0),
                         409, "Duplicate bootstrap")
            check_bootstrap_user(data, credentials)

            host = check_status(api.get("/api/v1/config/host"), 200, "Read host config").json()
            assert host["port"] == port
            host.update(
                authenticationRequired="enabled",
                authenticationMethod="forms",
                bindAddress="127.0.0.1",
                port=port,
                passwordConfirmation=password,
                **credentials,
            )
            saved = check_status(api.put("/api/v1/config/host/1", data=host), 202, "Save Forms config").json()
            assert saved["authenticationMethod"] == "forms" and saved["password"] == ""
            metadata_path = "/api/v1/config/metadataprovider"
            metadata = check_status(api.get(metadata_path), 200, "Read metadata settings").json()
            timeout = 37 if metadata["metadataProviderTimeoutSeconds"] != 37 else 43
            metadata["metadataProviderTimeoutSeconds"] = timeout
            saved = check_status(api.put(metadata_path + "/1", data=metadata), 202, "Save metadata settings").json()
            assert saved["metadataProviderTimeoutSeconds"] == timeout

            browser = playwright.chromium.launch(
                env={"PATH": os.environ["PATH"], "LANG": os.environ.get("LANG", "C.UTF-8")}
            )
            context = browser.new_context(viewport={"width": 1366, "height": 900}, service_workers="block")
            context.route("**/*", lambda route: route.continue_() if route.request.url.startswith(base + "/") else route.abort())
            page = context.new_page()
            errors = []
            page.on("pageerror", lambda error: errors.append(type(error).__name__))
            page.goto(base + "/authors")
            expect(page).to_have_url(re.compile(r"/login\?.*returnUrl="))
            login(page, username, "deliberately-wrong", rejected=True)
            login(page, username, password, rejected=False)
            check_status(page.request.get(base + "/", max_redirects=0), 200, "New Forms session opens the root")
            initializer = check_status(page.request.get(base + "/initialize.json"), 200, "Forms initializer")
            assert initializer.json()["apiKey"] == key and "no-store" in initializer.headers["cache-control"]
            check_routes(page, base, api, evidence)
            timeout = check_metadata_settings(page, base, api)
            catalogue = exercise_catalogue(page, api, base, work, data, live_provider=live_provider)
            assert app.poll() is None, "native app exited before the planned restart"

        assert app.returncode == 0, "native app did not exit cleanly after SIGTERM"

        with running_app(binary, data, log, port, evidence, credentials) as (app, base):
            wait_ready(app, http, base, data)
            check_native_profile(http, api, base, data, key, "After restart")
            host = check_status(api.get("/api/v1/config/host"), 200, "Persisted API key").json()
            assert host["username"] == username and host["password"] == "" and host["port"] == port
            ui_settings = check_status(api.get("/api/v1/config/ui"), 200, "Persisted UI settings").json()
            assert ui_settings["theme"] == "dark"
            metadata = check_status(api.get(metadata_path), 200, "Persisted metadata settings").json()
            assert metadata["metadataProviderTimeoutSeconds"] == timeout
            setup_headers = bootstrap_headers(http, base)
            check_status(http.post(base + "/bootstrap", headers=setup_headers, form=credentials, max_redirects=0),
                         409, "Persisted user")
            check_bootstrap_user(data, credentials)
            print("Bootstrap user verified in SQLite after restart", flush=True)
            check_status(page.request.get(base + "/initialize.json"), 200, "Persisted Forms session")
            page.set_viewport_size({"width": 1366, "height": 900})
            check_status(page.goto(base + "/authors"), 200, "Library after restart")
            expect(page.get_by_role("main").get_by_role("heading", name="Authors", exact=True)).to_be_visible()
            verify_catalogue_after_restart(page, api, base, catalogue)
            assert not errors, f"{len(errors)} uncaught browser page errors"
            page.get_by_role("button", name="Menu Button").click()
            page.get_by_role("button", name="Logout", exact=True).click()
            expect(page.get_by_placeholder("Username")).to_be_visible()
            check_status(page.request.get(base + "/initialize.json"), 401, "Logged-out initializer")
            check_status(page.request.get(base + "/settings/metadata", max_redirects=0), 401,
                         "Logged-out Metadata settings")
            page.goto(base + "/settings/metadata")
            expect(page).to_have_url(re.compile(r"/login\?.*returnUrl="))
            login(page, username, password, rejected=False, return_path="/settings/metadata")
            expect(page.get_by_role("spinbutton", name="Provider Timeout (seconds)")).to_have_value(str(timeout))
            print("Signed-in browser returned to the saved Metadata settings", flush=True)
            check_status(page.request.get(base + "/", max_redirects=0), 200, "New Forms session after restart opens the root")
            main = page.get_by_role("main")
            for route, label, locator in (
                ("/library", "Library", main.get_by_role("heading", name="Authors", exact=True)),
                ("/search", "Search", main.locator('input[placeholder*="War and Peace"]')),
                ("/settings", "Settings", main.get_by_role("link", name="Media Management", exact=False).first),
            ):
                check_status(page.goto(base + route), 200, f"New Forms session after restart opens {label}")
                expect(locator).to_be_visible()
            assert not errors, f"{len(errors)} uncaught browser page errors"
            browser.close()

    print("Browser smoke passed", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--live-provider", action="store_true", help="Run live Hardcover checks with HARDCOVER_API_KEY")
    args = parser.parse_args()
    os.umask(0o077)
    args.evidence.mkdir(parents=True, exist_ok=True)
    try:
        run(args.binary.resolve(strict=True), args.evidence, live_provider=args.live_provider)
    except Exception as error:
        # Playwright exceptions can embed request values. Artifacts contain only safe status
        # lines, redacted process logs on failure, and screenshots of the disposable app.
        raise SystemExit(f"Browser smoke failed ({type(error).__name__}). See the last checkpoint.") from None
