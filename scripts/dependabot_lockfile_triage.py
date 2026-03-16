#!/usr/bin/env python3
import argparse
import json
import re
import subprocess
import sys
from collections import defaultdict
from typing import Any, Dict, List, Set, Tuple


def gh_api(path: str) -> Any:
    result = subprocess.run(
        ["gh", "api", path],
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip())
    return json.loads(result.stdout)


def parse_lockfile_versions(lockfile_path: str, packages: Set[str]) -> Dict[str, Set[str]]:
    versions: Dict[str, Set[str]] = defaultdict(set)
    current_packages: List[str] = []

    with open(lockfile_path, "r", encoding="utf-8") as f:
        for line in f:
            if not line.startswith(" ") and line.rstrip().endswith(":"):
                header = line.rstrip()[:-1]
                current_packages = []
                for spec in header.split(","):
                    spec = spec.strip().strip('"')
                    if "@" not in spec:
                        continue
                    if spec.startswith("@"):
                        parts = spec.rsplit("@", 1)
                        name = parts[0]
                    else:
                        name = spec.split("@", 1)[0]
                    if name in packages:
                        current_packages.append(name)
            elif line.startswith("  version ") and current_packages:
                m = re.search(r'"([0-9]+\.[0-9]+\.[0-9]+)"', line)
                if not m:
                    continue
                version = m.group(1)
                for pkg in current_packages:
                    versions[pkg].add(version)
    return versions


def semver_key(version: str) -> Tuple[int, int, int]:
    m = re.search(r"(\d+)\.(\d+)\.(\d+)", version)
    if not m:
        return (0, 0, 0)
    return (int(m.group(1)), int(m.group(2)), int(m.group(3)))


def at_or_above(version: str, floor: str) -> bool:
    return semver_key(version) >= semver_key(floor)


def main() -> int:
    parser = argparse.ArgumentParser(description="Triage Dependabot alerts against lockfile versions")
    parser.add_argument("--owner", default="Swartdraak")
    parser.add_argument("--repo", default="Bibliophilarr")
    parser.add_argument("--lockfile", default="yarn.lock")
    parser.add_argument("--json-out")
    parser.add_argument("--md-out")
    args = parser.parse_args()

    alerts = gh_api(f"repos/{args.owner}/{args.repo}/dependabot/alerts?state=open&per_page=100")

    package_alerts: Dict[str, List[Dict[str, Any]]] = defaultdict(list)
    for alert in alerts:
        pkg = alert["dependency"]["package"]["name"]
        package_alerts[pkg].append(alert)

    lock_versions = parse_lockfile_versions(args.lockfile, set(package_alerts.keys()))

    rows = []
    for pkg, pkg_alerts in sorted(package_alerts.items()):
        first_patched = None
        severities = sorted({a["security_vulnerability"]["severity"] for a in pkg_alerts})
        for alert in pkg_alerts:
            patched = alert["security_vulnerability"].get("first_patched_version")
            if patched and patched.get("identifier"):
                first_patched = patched["identifier"]
                break

        versions = sorted(lock_versions.get(pkg, set()), key=semver_key)

        if not versions:
            status = "not-present-in-lockfile"
        elif not first_patched:
            status = "manual-review-needed"
        elif all(at_or_above(v, first_patched) for v in versions):
            status = "likely-resolved-awaiting-graph-refresh"
        else:
            status = "potentially-unresolved"

        rows.append(
            {
                "package": pkg,
                "alerts": len(pkg_alerts),
                "severities": severities,
                "first_patched": first_patched,
                "lockfile_versions": versions,
                "status": status,
            }
        )

    report = {
        "owner": args.owner,
        "repo": args.repo,
        "open_alert_count": len(alerts),
        "packages": rows,
    }

    if args.json_out:
        with open(args.json_out, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)

    if args.md_out:
        lines = [
            "# Dependabot Lockfile Triage",
            "",
            f"Open alerts: {len(alerts)}",
            "",
            "| Package | Open Alerts | Severity | First Patched | Lockfile Versions | Triage Status |",
            "|---|---:|---|---|---|---|",
        ]
        for row in rows:
            lines.append(
                "| {package} | {alerts} | {severity} | {patched} | {versions} | {status} |".format(
                    package=row["package"],
                    alerts=row["alerts"],
                    severity=", ".join(row["severities"]),
                    patched=row["first_patched"] or "n/a",
                    versions=", ".join(row["lockfile_versions"]) or "none",
                    status=row["status"],
                )
            )
        with open(args.md_out, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")

    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
