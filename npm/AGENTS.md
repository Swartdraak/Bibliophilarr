# npm Directory Contract

## Purpose

`npm/` contains npm launcher/package-specific content used to distribute or invoke Bibliophilarr through npm.

It is not the primary WebUI source tree.

## Rules

- Keep launcher/package behavior minimal and explicit.
- Do not duplicate application logic from the .NET runtime.
- Keep package version/release behavior aligned with stable repository tags and release automation.
- Do not independently publish or bump production package versions from a task branch.
- Validate platform/path/process-spawn behavior when changed.
- Do not embed credentials.

## Release policy

Routine npm changes branch from and PR to `develop`.

Production npm publication must correspond to an approved stable release/tag on `main` and remains human/release-workflow gated.

Use security/dependency review for package or publish workflow changes.
