"""Synthetic governed recovery checks. Never creates sockets or starts an app."""
import hashlib
import io
import json
import os
import runpy
import sqlite3
import sys
import tempfile
import unittest
import urllib.error
import urllib.request
from contextlib import redirect_stdout
from pathlib import Path
from types import ModuleType
from unittest.mock import patch

SCRIPTS = Path(__file__).resolve().parents[1] / 'recovery'
ROOT = 'http://127.0.0.1:8765/api/v1'


class Fixture:
    def __init__(self, directory):
        self.case = Path(directory)
        self.profile = self.case / 'app-data'
        self.profile.mkdir()
        self.key = 'synthetic-first-profile-key'
        self.start = '2026-11-25T01:00:00Z'
        self.status_profile = self.profile
        self.forged_tag = None
        self.denied_endpoint = None
        self.commands = []
        self.db = self.profile / 'bibliophilarr.db'
        self.empty_db()
        (self.profile / 'config.xml').touch()

    def empty_db(self):
        if self.db.exists():
            self.db.unlink()
        with sqlite3.connect(self.db) as db:
            db.execute('CREATE TABLE Tags (Id INTEGER PRIMARY KEY, Label TEXT UNIQUE)')

    def request(self, method, url, *, headers, timeout, payload=None):
        if not url.startswith(ROOT + '/'):
            raise AssertionError('unexpected endpoint')
        path = url.removeprefix(ROOT)
        if headers != self.auth_headers(self.key) or path == self.denied_endpoint:
            return 401, {}, ''
        self.commands.append((method, path))
        if path == '/config/host':
            return 200, {'id': 1}, ''
        if path == '/system/status':
            return 200, {'startTime': self.start, 'appData': str(self.status_profile)}, ''
        if (method, path) == ('POST', '/tag'):
            with sqlite3.connect(self.db) as db:
                row = db.execute('INSERT INTO Tags (Label) VALUES (?)', (payload['label'],))
                return 201, {'id': row.lastrowid, 'label': payload['label']}, ''
        if method == 'GET' and path.startswith('/tag/'):
            tag_id = int(path.rsplit('/', 1)[1])
            with sqlite3.connect(self.db) as db:
                row = db.execute('SELECT Label FROM Tags WHERE Id = ?', (tag_id,)).fetchone()
            if self.forged_tag:
                return 200, {'id': tag_id, 'label': self.forged_tag}, ''
            return (200, {'id': tag_id, 'label': row[0]}, '') if row else (404, {}, '')
        if path == '/system/backup':
            return 200, [], ''
        if path == '/system/backup/restore/999999':
            return 404, {}, ''
        raise AssertionError('unexpected request')

    @staticmethod
    def auth_headers(key):
        return {'X-Api-Key': key}

    def modules(self):
        common = ModuleType('common')
        common.api_root = lambda: ROOT
        common.data_dir = lambda: str(self.profile)
        common.case_dir = lambda: str(self.case)
        common.fetch_api_key = lambda: self.key
        common.auth_headers = self.auth_headers
        # Keep the witness shape and identity checks without the governed KDF cost.
        common.derive_api_key_witness = lambda key, salt: hashlib.pbkdf2_hmac(
            'sha256', key.encode(), salt, 1
        )
        common.make_reporter = lambda failures: (
            lambda name, ok, *args: failures.append(name) if not ok else None
        )
        common.finish = lambda failures: self.finish(failures)
        http = ModuleType('onyx_http_check')
        http.request_json = self.request
        return {'common': common, 'onyx_http_check': http}

    def finish(self, failures):
        self.failures = list(failures)
        raise SystemExit(int(bool(failures)))

    def execute(self, name, *, exercise_multipart=False):
        self.failures = []
        output = io.StringIO()
        isfile = os.path.isfile

        def runner_common(path):
            return path == '/workspace/linux-program/harness/common.py' or isfile(path)

        with (patch.dict(sys.modules, self.modules()),
              patch.object(sys, 'path', [str(SCRIPTS), *sys.path]),
              patch('os.path.isfile', side_effect=runner_common),
              patch('urllib.request.build_opener', side_effect=RuntimeError('network disabled')) as opener,
              redirect_stdout(output)):
            opener.side_effect = None

            def reject_signed_upload(request, *, timeout):
                headers = {name.lower(): value for name, value in request.header_items()}
                if (request.full_url != ROOT + '/system/backup/restore/upload'
                        or request.method != 'POST'
                        or headers.get('x-api-key') != self.key
                        or not headers.get('content-type', '').startswith('multipart/form-data; boundary=')):
                    raise AssertionError('unexpected synthetic multipart request')
                raise urllib.error.HTTPError(request.full_url, 415, '', {}, None)

            opener.return_value.open.side_effect = reject_signed_upload
            values = runpy.run_path(str(SCRIPTS / name))
            if not exercise_multipart and name == 'check_recovery.py':
                values['main'].__globals__['multipart_post'] = lambda *args, **kwargs: 415
            try:
                values['main']()
            except SystemExit as result:
                return result.code, self.failures, output.getvalue()
        raise AssertionError('check did not seal its result')


class RecoveryWitnessTests(unittest.TestCase):
    def setUp(self):
        case = tempfile.TemporaryDirectory()
        self.addCleanup(case.cleanup)
        self.fixture = Fixture(case.name)

    def prime(self):
        self.assertEqual(0, self.fixture.execute('check_recovery.py')[0])
        self.fixture.start = '2026-11-25T01:01:00Z'

    def test_new_process_same_profile_preserves_key_and_tag(self):
        self.prime()
        code, failures, output = self.fixture.execute('check_integrity.py')
        self.assertEqual((0, []), (code, failures))
        self.assertIn(('POST', '/tag'), self.fixture.commands)
        self.assertIn(('GET', '/tag/1'), self.fixture.commands)
        witness = self.fixture.case / 'recovery-before-restart.json'
        self.assertNotIn(self.fixture.key, witness.read_text())
        self.assertNotIn(self.fixture.key, output)
        self.assertEqual(0o600, witness.stat().st_mode & 0o777)

    def test_replaced_same_path_profile_does_not_pass(self):
        self.prime()
        self.fixture.key = 'synthetic-different-profile-key'
        self.fixture.empty_db()
        code, failures, _ = self.fixture.execute('check_integrity.py')
        self.assertEqual(1, code)
        self.assertIn('R5_db_and_config_survive_kill_restart', failures)
        self.assertIn('R6_api_auth_after_kill_restart', failures)

    def test_recreated_db_with_same_key_does_not_pass(self):
        self.prime()
        self.fixture.empty_db()
        code, failures, _ = self.fixture.execute('check_integrity.py')
        self.assertEqual(1, code)
        self.assertIn('R5_db_and_config_survive_kill_restart', failures)
        self.assertNotIn('R6_api_auth_after_kill_restart', failures)

    def test_readback_alone_cannot_substitute_for_database_record(self):
        self.prime()
        with sqlite3.connect(self.fixture.db) as connection:
            row = connection.execute('SELECT Label FROM Tags').fetchone()
        self.fixture.forged_tag = row[0] if row else 'synthetic-unused-fallback'
        self.fixture.empty_db()
        code, failures, _ = self.fixture.execute('check_integrity.py')
        self.assertEqual(1, code)
        self.assertIn('R5_db_and_config_survive_kill_restart', failures)

    def test_unchanged_process_does_not_count_as_relaunch(self):
        self.assertEqual(0, self.fixture.execute('check_recovery.py')[0])
        code, failures, _ = self.fixture.execute('check_integrity.py')
        self.assertEqual(1, code)
        self.assertIn('R6_api_auth_after_kill_restart', failures)

    def test_signed_readback_must_use_expected_case_profile(self):
        self.prime()
        self.fixture.status_profile = self.fixture.case / 'different-app-data'
        code, failures, _ = self.fixture.execute('check_integrity.py')
        self.assertEqual(1, code)
        self.assertIn('R6_api_auth_after_kill_restart', failures)

    def test_missing_or_malformed_witness_fails_closed(self):
        witness = self.fixture.case / 'recovery-before-restart.json'
        for data in (None, '{broken', json.dumps({'version': True})):
            with self.subTest(data=data):
                if data is not None:
                    witness.write_text(data)
                self.assertEqual(1, self.fixture.execute('check_integrity.py')[0])

    def test_wrong_pre_restart_process_scope_does_not_seed(self):
        self.fixture.status_profile = self.fixture.case / 'different-app-data'
        self.assertEqual(1, self.fixture.execute('check_recovery.py')[0])
        self.assertNotIn(('POST', '/tag'), self.fixture.commands)

    def test_multipart_boundary_uses_stub_transport(self):
        self.assertEqual(0, self.fixture.execute('check_recovery.py', exercise_multipart=True)[0])

    def test_signed_host_auth_still_required(self):
        self.prime()
        self.fixture.denied_endpoint = '/config/host'
        code, failures, _ = self.fixture.execute('check_integrity.py')
        self.assertEqual(1, code)
        self.assertIn('R6_api_auth_after_kill_restart', failures)


if __name__ == '__main__':
    unittest.main()
