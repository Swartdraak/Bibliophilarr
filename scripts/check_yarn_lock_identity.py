"""Check package identity for public npm registry tarballs in a Yarn v1 lock."""

import csv
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit


REGISTRY_HOSTS = {"registry.npmjs.org", "registry.yarnpkg.com"}


def package_name(descriptor):
    separator = descriptor.find("@", 1)
    if separator < 1:
        raise ValueError(f"invalid package selector {descriptor!r}")
    name = descriptor[:separator]
    specifier = descriptor[separator + 1:]
    if specifier.startswith("npm:"):
        name, separator, version = specifier[4:].rpartition("@")
        if not name or not separator or not version:
            raise ValueError(f"invalid npm alias {descriptor!r}")
    return name


def check_lock(path):
    lock = path.read_text(encoding="utf-8")
    if "# yarn lockfile v1" not in lock.splitlines()[:3]:
        raise ValueError("expected a Yarn v1 lockfile")

    failures = []
    checked = 0
    for entry in re.split(r"\n(?:[\t ]*\n)+", lock):
        resolved = re.search(r'^  resolved "([^"]+)"$', entry, re.MULTILINE)
        if not resolved:
            continue
        url = urlsplit(resolved[1])
        if url.hostname not in REGISTRY_HOSTS:
            continue

        checked += 1
        header = entry.splitlines()[0]
        version = re.search(r'^  version "([^"]+)"$', entry, re.MULTILINE)
        archive_name, _, filename = unquote(url.path).lstrip("/").partition("/-/")
        basename = archive_name.rsplit("/", 1)[-1]
        if not header.endswith(":") or not version or filename != f"{basename}-{version[1]}.tgz":
            failures.append(f"{header} has invalid registry archive metadata")
            continue

        for descriptor in next(csv.reader([header[:-1]], skipinitialspace=True, strict=True)):
            if package_name(descriptor) != archive_name:
                failures.append(f"{descriptor} resolves the archive for {archive_name}")

    return checked, failures


def main():
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("yarn.lock")
    try:
        checked, failures = check_lock(path)
    except (OSError, ValueError, csv.Error) as error:
        print(f"{path}: {error}", file=sys.stderr)
        return 1

    for failure in failures:
        print(f"{path}: {failure}", file=sys.stderr)
    print(f"Checked {checked} public registry archives in {path}: {len(failures)} mismatches")
    return int(bool(failures))


if __name__ == "__main__":
    sys.exit(main())
