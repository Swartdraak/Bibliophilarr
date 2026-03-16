# Media Library Full Scan and Organization

Date: 2026-03-15

## Scope

Two full scans and metadata-driven organization passes were executed for:

- /media/audiobooks
- /media/ebooks

## Canonical Rules Applied

Target structure and naming:

- Root/Author/Title (or Title (id) when Calibre id suffix exists)
- Media filename: Title - Author.ext
- Multi-file same-extension sets: Title - Author (N).ext

Metadata sources used in precedence order:

1. metadata.json (title, authors[0])
2. metadata.opf or other .opf in folder (dc:title, dc:creator)
3. Path inference fallback

Safety controls:

- Collision-safe move/rename behavior
- Automatic unique-name fallback for file-level rename collisions
- _dupes subtrees skipped to avoid flattening duplicate quarantine areas
- Invalid filename characters normalized

## Final Converged Status

- /media/audiobooks: 0 remaining proposed actions
- /media/ebooks: 0 remaining proposed actions

Verification reports:

- /opt/Bibliophilarr/_artifacts/media-organize-2026-03-15-final2-post/audiobooks_organize_summary.json
- /opt/Bibliophilarr/_artifacts/media-organize-2026-03-15-final/ebooks_organize_summary.json

## Aggregate Execution Metrics

Audiobooks:

- Initial scan: 501 book folders, 1596 media files, 1941 proposed actions
- Total apply operations across rounds:
  - applied: 1920
  - skipped_conflict: 2945
  - skipped_missing: 654
  - errors: 0
- Final scan: 483 book folders, 1596 media files, 0 proposed actions

Ebooks:

- Initial scan: 2134 book folders, 3762 media files, 1337 proposed actions
- Total apply operations across rounds:
  - applied: 1121
  - skipped_conflict: 771
  - skipped_missing: 647
  - errors: 0
- Final scan: 2020 book folders, 3605 media files, 0 proposed actions

## Artifacts

Primary action and summary reports are under:

- /opt/Bibliophilarr/_artifacts/media-organize-2026-03-15
- /opt/Bibliophilarr/_artifacts/media-organize-2026-03-15-round2
- /opt/Bibliophilarr/_artifacts/media-organize-2026-03-15-round3
- /opt/Bibliophilarr/_artifacts/media-organize-2026-03-15-round4
- /opt/Bibliophilarr/_artifacts/media-organize-2026-03-15-final
- /opt/Bibliophilarr/_artifacts/media-organize-2026-03-15-final2

## Automation Script

Organization automation script created and used:

- /opt/Bibliophilarr/scripts/media_library_organize.py

Usage examples:

- Dry-run:
  - python3 /opt/Bibliophilarr/scripts/media_library_organize.py --root /media/audiobooks --report-dir /opt/Bibliophilarr/_artifacts/media-organize-latest
- Apply:
  - python3 /opt/Bibliophilarr/scripts/media_library_organize.py --root /media/audiobooks --report-dir /opt/Bibliophilarr/_artifacts/media-organize-latest --apply
