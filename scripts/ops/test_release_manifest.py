import hashlib
import io
import json
from pathlib import Path
import tarfile
from tempfile import TemporaryDirectory
import unittest
import zipfile

from check_release_assets import package_asset_names
from create_release_manifest import PORTABLE_RUNTIME_CONFIG, WINDOWS_RUNTIME_CONFIG, build_manifest


REPOSITORY = "Swartdraak/Bibliophilarr"
COMMIT = "f" * 40
RUNTIME_CONFIG = json.dumps({"runtimeOptions": {
    "tfm": "net10.0",
    "frameworks": [
        {"name": "Microsoft.NETCore.App", "version": "10.0.15"},
        {"name": "Microsoft.AspNetCore.App", "version": "10.0.15"},
    ],
}}).encode()


def fixtures(directory, version):
    for name, (rid, kind) in package_asset_names(version).items():
        path = directory / name
        if kind == "installer":
            path.write_bytes(b"MZ fixture")
        elif rid == "win-x64":
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr(WINDOWS_RUNTIME_CONFIG, RUNTIME_CONFIG)
        else:
            with tarfile.open(path, "w:gz") as archive:
                info = tarfile.TarInfo(PORTABLE_RUNTIME_CONFIG)
                info.size = len(RUNTIME_CONFIG)
                archive.addfile(info, io.BytesIO(RUNTIME_CONFIG))


class ManifestTests(unittest.TestCase):
    def test_stable_manifest_describes_the_bytes_and_runtime_but_disallows_automatic_install(self):
        with TemporaryDirectory() as temp:
            directory = Path(temp)
            fixtures(directory, "1.2.3")
            manifest = build_manifest(directory, "1.2.3", COMMIT, REPOSITORY)

            self.assertEqual(manifest["schemaVersion"], 1)
            self.assertEqual(manifest["channel"], "stable")
            self.assertEqual(manifest["tag"], "v1.2.3")
            self.assertEqual(manifest["releaseNotesUrl"], f"https://github.com/{REPOSITORY}/releases/tag/v1.2.3")
            self.assertEqual(manifest["verification"], {"algorithm": "SHA-256", "signatureType": "none"})
            self.assertFalse(manifest["updatePolicy"]["automaticInstall"])
            self.assertTrue(manifest["updatePolicy"]["backupRequired"])
            self.assertEqual(len(manifest["artifacts"]), 6)
            for asset in manifest["artifacts"]:
                content = (directory / asset["name"]).read_bytes()
                self.assertEqual(asset["bytes"], len(content))
                self.assertEqual(asset["sha256"], hashlib.sha256(content).hexdigest())
                self.assertEqual(asset["runtime"]["targetFramework"], "net10.0")
                self.assertEqual(asset["runtime"]["sharedFrameworks"][0]["version"], "10.0.15")

    def test_release_candidate_and_rehearsal_are_distinguished(self):
        for version, channel in (("1.2.3-rc.1", "releaseCandidate"), ("0.0.0-rehearsal.1.1", "rehearsal")):
            with self.subTest(channel=channel), TemporaryDirectory() as temp:
                directory = Path(temp)
                fixtures(directory, version)
                manifest = build_manifest(directory, version, COMMIT, REPOSITORY)
                self.assertEqual(manifest["channel"], channel)
                self.assertEqual("releaseNotesUrl" in manifest, channel == "releaseCandidate")

    def test_rejects_invalid_source_revision_and_version(self):
        for version, revision in (("1.02.3", COMMIT), ("1.2.3", "main")):
            with self.subTest(version=version), self.assertRaises(ValueError):
                build_manifest(Path("."), version, revision, REPOSITORY)

    def test_rejects_missing_runtime_configuration_and_invalid_installer(self):
        with TemporaryDirectory() as temp:
            directory = Path(temp)
            version = "1.2.3"
            fixtures(directory, version)
            installer = directory / f"bibliophilarr-{version}-win-x64-setup.exe"
            installer.write_bytes(b"not a Windows executable")
            with self.assertRaisesRegex(ValueError, "Invalid installer"):
                build_manifest(directory, version, COMMIT, REPOSITORY)

            installer.write_bytes(b"MZ fixture")
            with zipfile.ZipFile(directory / f"bibliophilarr-{version}-win-x64.zip", "w"):
                pass
            with self.assertRaises(KeyError):
                build_manifest(directory, version, COMMIT, REPOSITORY)
