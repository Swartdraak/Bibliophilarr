# Release manifest

The package workflow generates `release-manifest.json` and includes its SHA-256
digest in `SHA256SUMS.txt`. The [version 1 schema](release-manifest-v1.schema.json)
defines the manifest. Releases record the version, channel, tag, source
commit, release notes link and six official package artifacts. The manifest
covers the five portable packages and Windows installer. Container and npm
registry artifacts follow their own publication workflows.

Each asset records its runtime identifier, byte size and SHA-256 digest.
Portable runtime requirements come from the archive's .NET runtime
configuration. The Windows installer inherits the requirements of the Windows
portable package. A rehearsal identifies its channel explicitly and has no
public tag or release-notes URL.

Obtain the manifest and assets from the official GitHub release. SHA-256
detects corrupted or mismatched files. Version 1 uses the repository’s release
channel as the trust source and has no independent signature or signing keys.
The manifest grants no authority to install an update. Review the release
notes and verify a configuration and database backup before updating.
Rollbacks can require the previous package and a matching backup.

Run the producer against the verified package directory:

```bash
python3 scripts/ops/check_release_assets.py release-assets 1.2.3
python3 scripts/ops/create_release_manifest.py release-assets 1.2.3 \
  "<verified-source-commit>" Swartdraak/Bibliophilarr
(cd release-assets && sha256sum release-manifest.json >> SHA256SUMS.txt && sha256sum --check SHA256SUMS.txt)
```
