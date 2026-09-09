# Dual-Library and Dual-Format Tracking

Bibliophilarr manages **two media formats** for the same work: **ebooks** (EPUB, MOBI,
AZW3, PDF, FB2, DJVU, CBZ) and **audiobooks** (MP3, M4B, M4A, OGG, FLAC, AAC, OPUS).
A single *work* (e.g. *Dune*) can be present in both formats, and the app tracks them
as separate **editions** of the same **book**.

## Core model

| Concept | Description |
|---|---|
| **Author** | A person (or group) who writes works. Identified by a provider-scoped id (e.g. `hardcover:author:123`). |
| **Book** | A work by an author. Identified by a provider-scoped id (e.g. `hardcover:work:456`). |
| **Edition** | A specific format/edition of a book. Each edition has an `IsEbook` flag: `true` = ebook, `false` = audiobook. |
| **BookFile** | A physical file on disk, mapped to an edition. |

A book can have **multiple editions** — one ebook edition and one audiobook edition.
The `Edition.IsEbook` boolean is the primary discriminator. When a metadata provider
does not set `IsEbook` correctly, the API falls back to deriving the format from the
actual file quality (e.g. EPUB → Ebook, M4B → Audiobook) — see
`Bibliophilarr.Api.V1/Books/BookResource.cs`.

## Root folders

Configure **two root folders** — one for ebooks, one for audiobooks:

```
/mnt/media/libraries/ebooks      (EPUB, MOBI, AZW3, PDF, FB2, DJVU, CBZ)
/mnt/media/libraries/audiobooks  (MP3, M4B, M4A, OGG, FLAC, AAC, OPUS)
```

Each root folder is associated with a format type. During a disk scan
(`RescanFolders`), files are classified by extension and matched to the appropriate
edition format.

## Per-format monitoring

Each edition is independently **monitored** (the app will search for and download it)
or **unmonitored** (the app ignores it). Monitoring is tracked **per format**:

- `EditionRepository.SetMonitoredByFormat(edition)` sets the given edition's format to
  monitored and all other editions of the **same format** to unmonitored.
- This means a book can have its ebook edition monitored while its audiobook edition
  is unmonitored (or vice versa).

> **Note (issue #203):** `SetMonitoredByFormat` previously threw an `ArgumentException`
> when no same-format edition matched (format misclassification) or the edition id was
> absent from the book's editions, aborting the entire `RescanFolders` batch. As of the
> fix in PR #213, it is defensive: it logs a diagnostic and falls back to
> `SetMonitored` (or no-ops) instead of throwing.

## API surface

`GET /api/v1/book` returns a `formatStatuses` array describing each format:

```json
{
  "formatStatuses": [
    { "formatType": "ebook",     "monitored": true,  "hasFile": true,  "fileCount": 1 },
    { "formatType": "audiobook", "monitored": false, "hasFile": false, "fileCount": 0 }
  ]
}
```

- `formatType` — `ebook` or `audiobook`.
- `monitored` — whether an edition of that format is monitored.
- `hasFile` — whether a physical file exists for that format.
- `fileCount` — number of files for that format.

## Dual-format import

When a file is imported, its format is derived from the file extension (not from a
stale queue `formatType`). The import decision matches the file to the correct edition
format. See the [Troubleshooting Runbook](../docs/operations/TROUBLESHOOTING_RUNBOOK.md)
for import-stall diagnostics.

## References

- [Architecture Overview](Architecture.md)
- [Metadata Source Fallback](Metadata-Fallback.md)
- [Troubleshooting Runbook](../docs/operations/TROUBLESHOOTING_RUNBOOK.md)
- [Local Instance Operator Runbook](../docs/operations/LOCAL_INSTANCE_OPERATOR_RUNBOOK.md)
