"""No-network checks for strict live-provider browser evidence."""

import json
import os
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import Mock, patch

import catalogue_workflow as catalogue
import smoke


class StrictLiveEvidence(unittest.TestCase):
    def test_required_hardcover_credential_fails_before_starting_the_app(self):
        for token in (None, "   "):
            with self.subTest(token_present=token is not None), TemporaryDirectory() as temp:
                evidence = Path(temp) / "evidence"
                env = dict(os.environ)
                env.pop("HARDCOVER_API_KEY", None)
                env.pop("GOOGLE_BOOKS_API_KEY", None)
                if token is not None:
                    env["HARDCOVER_API_KEY"] = token
                # Strict mode must report the missing key before even resolving the binary.
                result = subprocess.run(
                    [sys.executable, "-B", str(Path(__file__).with_name("smoke.py")),
                     "--binary", str(Path(temp) / "missing-binary"), "--evidence", str(evidence),
                     "--live-provider", "hardcover", "--require-live-provider"],
                    env=env, capture_output=True, text=True, timeout=10, check=False,
                )
                self.assertNotEqual(0, result.returncode)
                receipt = evidence / "live-provider-evidence.json"
                self.assertTrue(receipt.is_file(), result.stderr)
                self.assertEqual(json.loads(receipt.read_text()), {
                    "provider": "hardcover", "status": "failed", "reason": "missing_credential",
                    "credentialEnvironmentVariable": "HARDCOVER_API_KEY", "appStarted": False,
                })

    def test_required_live_run_refuses_disabled_assertions(self):
        with TemporaryDirectory() as temp:
            evidence = Path(temp) / "evidence"
            result = subprocess.run(
                [sys.executable, "-O", "-B", str(Path(__file__).with_name("smoke.py")),
                 "--binary", str(Path(temp) / "missing-binary"), "--evidence", str(evidence),
                 "--live-provider", "hardcover", "--require-live-provider"],
                env={**os.environ, "HARDCOVER_API_KEY": "synthetic"}, capture_output=True,
                text=True, timeout=10, check=False,
            )
            self.assertNotEqual(0, result.returncode)
            receipt = json.loads((evidence / "live-provider-evidence.json").read_text())
            self.assertEqual(receipt["reason"], "assertions_disabled")
            self.assertIs(receipt["appStarted"], False)

    def test_missing_binary_is_reported_before_starting_the_app(self):
        with TemporaryDirectory() as temp, patch.dict(os.environ, {"HARDCOVER_API_KEY": "synthetic"}, clear=True):
            evidence = Path(temp) / "evidence"
            result = subprocess.run(
                [sys.executable, "-B", str(Path(__file__).with_name("smoke.py")),
                 "--binary", str(Path(temp) / "missing-binary"), "--evidence", str(evidence),
                 "--live-provider", "hardcover", "--require-live-provider"],
                capture_output=True, text=True, timeout=10, check=False,
            )
            self.assertNotEqual(0, result.returncode)
            receipt = json.loads((evidence / "live-provider-evidence.json").read_text())
            self.assertEqual(receipt["reason"], "browser_smoke_failed")
            self.assertEqual(receipt["errorType"], "FileNotFoundError")
            self.assertIs(receipt["appStarted"], False)

    def test_required_live_receipt_distinguishes_preflight_success_and_unknown_start(self):
        synthetic_token = "restricted-fixture-credential"
        for outcome in (None, RuntimeError(synthetic_token)):
            with self.subTest(run_succeeded=outcome is None), TemporaryDirectory() as temp:
                binary = Path(temp) / "fixture-binary"
                binary.touch()
                evidence = Path(temp) / "evidence"
                receipt_path = evidence / "live-provider-evidence.json"
                args = ["smoke.py", "--binary", str(binary), "--evidence", str(evidence),
                        "--live-provider", "googlebooks", "--require-live-provider"]

                preflights = []

                def synthetic_run(*_args, **_kwargs):
                    preflights.append(json.loads(receipt_path.read_text()))
                    if outcome is not None:
                        raise outcome

                with patch("sys.argv", args), patch.dict(os.environ, {"GOOGLE_BOOKS_API_KEY": synthetic_token}, clear=True):
                    with patch.object(smoke.os, "umask"), patch.object(smoke, "run", side_effect=synthetic_run) as run:
                        if outcome is None:
                            smoke.main()
                        else:
                            with self.assertRaises(SystemExit) as error:
                                smoke.main()
                            self.assertNotIn(synthetic_token, str(error.exception))
                    run.assert_called_once_with(binary.resolve(), evidence, live_provider="googlebooks")

                preflight, = preflights
                self.assertEqual(preflight["status"], "preflight_passed")
                self.assertIs(preflight["appStarted"], False)
                receipt = json.loads(receipt_path.read_text())
                self.assertNotIn(synthetic_token, receipt_path.read_text())
                if outcome is None:
                    self.assertEqual(receipt["status"], "passed")
                    self.assertIs(receipt["appStarted"], True)
                    self.assertIs(receipt["editionPersistedAfterRestart"], True)
                else:
                    self.assertEqual(receipt["status"], "failed")
                    self.assertIn("appStarted", receipt)
                    self.assertIsNone(receipt["appStarted"])

    def test_hardcover_work_and_selected_edition_are_checked_on_signed_restart(self):
        book = {"id": 4, "authorId": 7, "titleSlug": "fixture-work", "title": "Fixture",
                "foreignBookId": "hardcover:work:11", "foreignEditionId": "hardcover:edition:11",
                "editions": [{"id": 13, "foreignEditionId": "hardcover:edition:11", "monitored": True}]}
        restarted = {**book, "editions": [dict(book["editions"][0])]}
        with TemporaryDirectory() as temp, patch.dict(os.environ, {"HARDCOVER_API_KEY": "synthetic"}, clear=True):
            work = Path(temp)
            app_data = work / "profile"
            app_data.mkdir()
            (app_data / "bibliophilarr.db").touch()
            api = Mock()
            api.get.return_value.status = 200
            api.get.return_value.json.return_value = book
            api.post.return_value.status = 400
            api.post.return_value.json.return_value = [{"propertyName": "ForeignBookId"}]
            tracked = catalogue.CatalogueState(4, book["foreignBookId"], "Fixture", book["titleSlug"], 17,
                                               13, work / "fixture.epub", {})
            with patch.multiple(catalogue, initialize_root=Mock(return_value={}), configure_providers=Mock(),
                                find_live_book=Mock(return_value=(book, "Fixture Author")),
                                render_library_and_detail=Mock(), import_fixture=Mock(return_value=tracked),
                                files_for_book=Mock(return_value=[{"id": 17, "path": str(tracked.path)}]),
                                verify_file_edition=Mock(), epub_payload=Mock(return_value={})):
                state = catalogue.exercise_catalogue(Mock(), api, "http://127.0.0.1:8765", work, app_data,
                                                     live_provider="hardcover")
                self.assertIsNotNone(state.live_book)
                api.get.return_value.json.return_value = restarted
                catalogue.verify_catalogue_after_restart(None, api, "", app_data, state)
                restarted["editions"][0]["id"] = 99
                with self.assertRaises(AssertionError):
                    catalogue.verify_catalogue_after_restart(None, api, "", app_data, state)


if __name__ == "__main__":
    unittest.main()
