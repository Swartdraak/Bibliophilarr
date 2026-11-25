"""Synthetic persistence evidence for the governed, case-owned KILL restart."""
import json
import os
import re
import sqlite3
from contextlib import closing
from hmac import compare_digest
from pathlib import Path
from secrets import token_bytes, token_hex

import common
from onyx_http_check import request_json

DOMAIN = b'Bibliophilarr governed Linux recovery v1\x00'
WITNESS_NAME = 'recovery-before-restart.json'


def signed_process_start(api, auth, data_dir):
    status, process, _ = request_json('GET', api + '/system/status', headers=auth, timeout=30)
    if status != 200 or not isinstance(process, dict):
        return None
    app_data, start = process.get('appData'), process.get('startTime')
    if not isinstance(app_data, str) or not app_data or not isinstance(start, str) or not start:
        return None
    if Path(app_data).resolve() != data_dir:
        return None
    return start


def tag_matches(status, resource, tag_id, label, expected_status=200):
    return (status == expected_status and isinstance(resource, dict)
            and type(resource.get('id')) is int and resource['id'] == tag_id
            and resource.get('label') == label)


def signed_tag_matches(api, auth, tag_id, label):
    status, resource, _ = request_json('GET', f'{api}/tag/{tag_id}', headers=auth, timeout=30)
    return tag_matches(status, resource, tag_id, label)


def stored_tag_matches(data_dir, tag_id, label):
    database = data_dir / 'bibliophilarr.db'
    try:
        with closing(sqlite3.connect(database.as_uri() + '?mode=ro', uri=True)) as connection:
            result = connection.execute('SELECT Label FROM Tags WHERE Id = ?', (tag_id,)).fetchone()
    except sqlite3.Error:
        return False
    return result == (label,)


def create(api, auth, api_key, data_dir, start):
    path = data_dir.parent / WITNESS_NAME
    if path.exists():
        return False
    label = 'recovery-witness-' + token_hex(16)
    status, resource, _ = request_json('POST', api + '/tag', payload={'label': label},
                                       headers=auth, timeout=30)
    tag_id = resource.get('id') if isinstance(resource, dict) else None
    if (type(tag_id) is not int or tag_id <= 0
            or not tag_matches(status, resource, tag_id, label, expected_status=201)
            or not signed_tag_matches(api, auth, tag_id, label)
            or not stored_tag_matches(data_dir, tag_id, label)):
        return False

    salt = token_bytes(16)
    key_tag = common.derive_api_key_witness(api_key, DOMAIN + salt)
    witness = {'version': 1, 'startTime': start, 'salt': salt.hex(), 'keyTag': key_tag.hex(),
               'tagId': tag_id, 'tagLabel': label}
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, 'w', encoding='utf-8') as stream:
            json.dump(witness, stream)
            stream.flush()
            os.fsync(stream.fileno())
    except OSError:
        return False
    return True


def read(data_dir):
    try:
        witness = json.loads((data_dir.parent / WITNESS_NAME).read_text(encoding='utf-8'))
    except (OSError, ValueError):
        return None
    if (not isinstance(witness, dict)
            or type(witness.get('version')) is not int or witness['version'] != 1
            or not isinstance(witness.get('startTime'), str) or not witness['startTime']
            or type(witness.get('tagId')) is not int or witness['tagId'] <= 0):
        return None
    patterns = {'salt': r'[0-9a-f]{32}', 'keyTag': r'[0-9a-f]{64}',
                'tagLabel': r'recovery-witness-[0-9a-f]{32}'}
    if not all(isinstance(witness.get(field), str) and re.fullmatch(pattern, witness[field])
               for field, pattern in patterns.items()):
        return None
    return witness


def key_matches(api_key, witness):
    salt = bytes.fromhex(witness['salt'])
    key_tag = common.derive_api_key_witness(api_key, DOMAIN + salt)
    return compare_digest(key_tag, bytes.fromhex(witness['keyTag']))
