# Download Directory Contract

## Purpose

This subtree owns download-domain behavior, including completed-download and download-client-facing workflows that feed import.

## Protected behavior

Changes must not introduce uncontrolled completed-download retry loops, zero-file import loops, incorrect path mapping, wrong media-type association, duplicate imports, lost completed-download state, or destructive cleanup before successful import.

A completed download with zero importable files must fail safely and remain observable.

## Integration boundaries

Coordinate with indexer/download client adapters, file discovery, import decision logic, path mapping, and tracking/persistence.

Use `integration-engineer` and/or `import-file-lifecycle-engineer` based on actual ownership.

## Validation

For behavior changes: reproduce the original failure, add targeted regression coverage, validate disposable qBittorrent/download integration when applicable, use `qa-library-workflow-validator`, and verify restart/retry behavior when stateful.

Normal PR target is `develop`.
