"""Verify the checkout and release identity before a publication workflow runs."""

import argparse
import os
from pathlib import Path
import re
import subprocess
import sys


# BRANCHING.md defines stable and numbered release-candidate SemVer tags.
# Development tags, arbitrary prereleases and build metadata do not publish.
PUBLISH_TAG = re.compile(
    r"v(?P<core>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))"
    r"(?P<rc>-rc\.(?:0|[1-9][0-9]*))?"
)


def git(*args):
    return subprocess.run(
        ["git", *args], check=True, capture_output=True, text=True
    ).stdout.strip()


def verify(event, ref, event_sha, requested_tag, use_current_stable, build_only):
    commit = git("rev-parse", "--verify", "HEAD^{commit}")
    if git("rev-parse", "--verify", f"{event_sha}^{{commit}}") != commit:
        raise ValueError("The checkout does not match the event commit.")

    if event == "push" and ref.startswith("refs/tags/"):
        tag = ref.removeprefix("refs/tags/")
    elif event == "workflow_dispatch" and ref == "refs/heads/main":
        verify_branch_history(commit, "main")
        if build_only:
            return {"commit": commit, "tag": "", "version": "", "core": "", "prerelease": "false"}
        if use_current_stable:
            tags = [
                candidate
                for candidate in git("tag", "--points-at", commit).splitlines()
                if (match := PUBLISH_TAG.fullmatch(candidate)) and not match["rc"]
            ]
            if len(tags) != 1:
                raise ValueError("Manual Docker publication requires exactly one stable tag on the dispatched main commit.")
            tag = tags[0]
        else:
            tag = requested_tag
    else:
        raise ValueError("Publication requires a release tag push or a manual dispatch from main.")

    version = PUBLISH_TAG.fullmatch(tag)
    if not version:
        raise ValueError("Publication requires a vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-rc.N tag.")

    tag_commit = git("rev-parse", "--verify", f"refs/tags/{tag}^{{commit}}")
    if tag_commit != commit:
        raise ValueError("The release tag does not resolve to the exact checked-out commit.")

    prerelease = bool(version["rc"])
    verify_branch_history(commit, "staging" if prerelease else "main")
    return {
        "commit": commit,
        "tag": tag,
        "version": tag[1:],
        "core": version["core"],
        "prerelease": str(prerelease).lower(),
    }


def verify_branch_history(commit, branch):
    branch_commit = git("rev-parse", "--verify", f"refs/remotes/origin/{branch}^{{commit}}")
    # A commit brought in by a merge may precede the approved promotion snapshot.
    if commit not in git("rev-list", "--first-parent", branch_commit).splitlines():
        raise ValueError(f"The checked-out commit must be on the first-parent history of origin/{branch}.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--requested-tag", default="")
    parser.add_argument("--use-current-stable", choices=("true", "false"), default="false")
    parser.add_argument("--build-only", choices=("true", "false"), default="false")
    args = parser.parse_args()

    try:
        outputs = verify(
            os.environ["GITHUB_EVENT_NAME"],
            os.environ["GITHUB_REF"],
            os.environ["GITHUB_SHA"],
            args.requested_tag,
            args.use_current_stable == "true",
            args.build_only == "true",
        )
    except (ValueError, subprocess.CalledProcessError) as error:
        print(f"Release source verification failed: {error}", file=sys.stderr)
        return 1

    with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as output:
        for key, value in outputs.items():
            print(f"{key}={value}", file=output)
    print(f"Verified source {outputs['commit']} for {outputs['tag'] or 'a build-only run'}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
