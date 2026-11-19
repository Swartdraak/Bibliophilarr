"""Describe verified Bibliophilarr release assets without granting install authority."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import tarfile
import zipfile

from check_release_assets import package_asset_names


MANIFEST_NAME = "release-manifest.json"
VERSION = r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"
RC_VERSION = VERSION + r"-rc\.(?:0|[1-9][0-9]*)"
REHEARSAL_VERSION = VERSION + r"-rehearsal\.[0-9]+\.[0-9]+"
PORTABLE_RUNTIME_CONFIG = "Bibliophilarr/Bibliophilarr.runtimeconfig.json"
WINDOWS_RUNTIME_CONFIG = "Bibliophilarr.Console.runtimeconfig.json"
MAX_CONFIG_BYTES = 64 * 1024
FRAMEWORK_NAMES = {"Microsoft.NETCore.App", "Microsoft.AspNetCore.App"}


def release_channel(version):
    for pattern, channel in ((VERSION, "stable"), (RC_VERSION, "releaseCandidate"), (REHEARSAL_VERSION, "rehearsal")):
        if re.fullmatch(pattern, version):
            return channel
    raise ValueError("Version must be stable, a numbered release candidate, or a numbered rehearsal")


def runtime_requirements(path, rid):
    if rid == "win-x64":
        with zipfile.ZipFile(path) as archive:
            info = archive.getinfo(WINDOWS_RUNTIME_CONFIG)
            if info.file_size > MAX_CONFIG_BYTES:
                raise ValueError(f"Unexpected runtime configuration size in {path.name}")
            body = archive.read(info)
    else:
        with tarfile.open(path, mode="r:gz") as archive:
            info = archive.getmember(PORTABLE_RUNTIME_CONFIG)
            if not info.isfile() or info.size > MAX_CONFIG_BYTES:
                raise ValueError(f"Invalid runtime configuration entry in {path.name}")
            with archive.extractfile(info) as config:
                body = config.read()

    runtime = json.loads(body)["runtimeOptions"]
    frameworks = runtime["frameworks"]
    if runtime["tfm"] != "net10.0" or len(frameworks) != len(FRAMEWORK_NAMES) or {f["name"] for f in frameworks} != FRAMEWORK_NAMES:
        raise ValueError(f"Unexpected shared frameworks in {path.name}")
    if any(not re.fullmatch(r"10\.0\.[0-9]+", f["version"]) for f in frameworks):
        raise ValueError(f"Unexpected shared-framework version in {path.name}")

    return {
        "targetFramework": runtime["tfm"],
        "bundled": False,
        "sharedFrameworks": [{"name": f["name"], "version": f["version"]} for f in frameworks],
    }


def build_manifest(directory, version, commit, repository):
    channel = release_channel(version)
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("Use the exact 40-character source commit")
    if not re.fullmatch(r"[A-Za-z0-9-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Expected the GitHub owner and repository name")

    expected = package_asset_names(version)
    for name in expected:
        path = directory / name
        if not path.is_file() or path.is_symlink() or path.stat().st_size == 0:
            raise ValueError(f"Missing or invalid asset: {name}")

    runtime_by_rid = {
        rid: runtime_requirements(directory / name, rid)
        for name, (rid, kind) in expected.items()
        if kind == "portable"
    }
    artifacts = []
    for name, (rid, kind) in sorted(expected.items()):
        path = directory / name
        with path.open("rb") as asset:
            if kind == "installer":
                if asset.read(2) != b"MZ":
                    raise ValueError(f"Invalid installer: {name}")
                asset.seek(0)
            digest = hashlib.file_digest(asset, "sha256").hexdigest()
        artifacts.append({
            "name": name,
            "rid": rid,
            "kind": kind,
            "bytes": path.stat().st_size,
            "sha256": digest,
            "runtime": runtime_by_rid[rid],
        })

    manifest = {
        "schemaVersion": 1,
        "version": version,
        "channel": channel,
        "source": {"repository": repository, "commit": commit},
        "verification": {"algorithm": "SHA-256", "signatureType": "none"},
        "updatePolicy": {
            "automaticInstall": False,
            "backupRequired": True,
            "migrationAdvice": "Review the release notes before upgrading. Restore the matching configuration and database backup when rolling back.",
        },
        "artifacts": artifacts,
    }
    if channel != "rehearsal":
        manifest["tag"] = f"v{version}"
        manifest["releaseNotesUrl"] = f"https://github.com/{repository}/releases/tag/v{version}"
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("version")
    parser.add_argument("commit")
    parser.add_argument("repository")
    args = parser.parse_args()

    try:
        manifest = build_manifest(args.directory, args.version, args.commit, args.repository)
    except (OSError, ValueError, TypeError, KeyError, tarfile.TarError, zipfile.BadZipFile) as error:
        parser.error(str(error))

    path = args.directory / MANIFEST_NAME
    path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
