"""Require the complete release artifact set and write its SHA256 manifest."""

import argparse
import hashlib
from pathlib import Path
import re


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("version")
    args = parser.parse_args()

    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9a-zA-Z]+(?:\.[0-9a-zA-Z]+)*)?", args.version):
        parser.error("Invalid package version")

    prefix = f"bibliophilarr-{args.version}-"
    expected = {
        f"{prefix}{suffix}" for suffix in (
            "linux-x64.tar.gz", "linux-arm64.tar.gz", "osx-x64.tar.gz",
            "osx-arm64.tar.gz", "win-x64.zip", "win-x64-setup.exe",
        )
    }
    manifest = args.directory / "SHA256SUMS.txt"
    if manifest.exists():
        manifest.unlink()

    entries = {path.name for path in args.directory.iterdir()}
    missing = expected - entries
    unexpected = entries - expected
    invalid = sorted(
        name for name in expected & entries
        if not (args.directory / name).is_file()
        or (args.directory / name).is_symlink()
        or (args.directory / name).stat().st_size == 0
    )

    if missing or unexpected or invalid:
        parser.error(f"Missing assets: {sorted(missing)}. Unexpected assets: {sorted(unexpected)}. Invalid assets: {invalid}.")

    hashes = []
    for name in sorted(expected):
        with (args.directory / name).open("rb") as asset:
            hashes.append(f"{hashlib.file_digest(asset, 'sha256').hexdigest()}  {name}")

    manifest.write_text("\n".join(hashes) + "\n", encoding="utf-8")
    print(manifest.read_text(encoding="utf-8"), end="")


if __name__ == "__main__":
    main()
