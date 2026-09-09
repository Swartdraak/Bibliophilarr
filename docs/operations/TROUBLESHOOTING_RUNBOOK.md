# Troubleshooting Runbook

This runbook covers the failure modes surfaced by the 2026-09-08 field test
(issues #203–#207). Each section gives the symptom, the root cause, the fix status,
and the operator triage steps.

## Table of contents

1. [Scan aborts mid-import (RescanFolders crash)](#1-scan-aborts-mid-import)
2. [Audiobook imports dropped ("Couldn't import book")](#2-audiobook-imports-dropped)
3. [Non-idempotent scan (BookFiles.Path UNIQUE constraint)](#3-non-idempotent-scan)
4. [Import stall ("No importable files found")](#4-import-stall)
5. [Bibliography hydration gaps (OpenLibraryWorkId, series links)](#5-bibliography-hydration-gaps)
6. [Author deletion on provider failure](#6-author-deletion-on-provider-failure)

---

## 1. Scan aborts mid-import

**Symptom:** `RescanFolders` command fails partway through; files after the failing
book are never imported.

**Log signature:**

```
Warn|ArgumentValidator|Expected an expression that evaluates to true.
Error|CommandExecutor|Error occurred while executing task RescanFolders
System.ArgumentException: Expected an expression that evaluates to true.
   at NzbDrone.Core.Books.EditionRepository.SetMonitoredByFormat(Edition edition)
   at NzbDrone.Core.MediaFiles.BookImport.ImportApprovedBooks.Import(...)
```

**Root cause (issue #203):** `EditionRepository.SetMonitoredByFormat` threw an
`ArgumentException` from its `Ensure()` guard when no same-format edition matched
(format misclassification) or the edition id was absent from `FindByBook` (stale id).
The exception propagated out of `ImportApprovedBooks.Import` → `DiskScanService.Scan`,
aborting the entire batch.

**Fix status:** ✅ Fixed in PR #213. `SetMonitoredByFormat` is now defensive (logs a
diagnostic and falls back to `SetMonitored` or no-ops), and `ImportApprovedBooks.Import`
wraps each book in a try/catch so one bad book is skipped instead of aborting the batch.

**Triage (if still observed on an unfixed build):**

1. Identify the failing book from the log line immediately before the crash
   (`Importing book N/M [id][title]`).
2. Check the book's editions: `SELECT Id, IsEbook, Monitored FROM Editions WHERE BookId = <id>;`
3. If the imported edition's `IsEbook` does not match the stored editions, the format
   was misclassified. Re-scan after the #203 fix is applied.

---

## 2. Audiobook imports dropped

**Symptom:** Individual audiobook imports fail with "Couldn't import book ..."; the
file is skipped and never imported.

**Log signature:**

```
Warn|ImportApprovedBooks|Couldn't import book /path/to/file.m4b
System.NullReferenceException: Object reference not set to an instance of an object.
   at NzbDrone.Core.MediaFiles.AudioTagService.GetTrackMetadata(BookFile trackfile)
   at NzbDrone.Core.MediaFiles.AudioTagService.WriteTags(...)
```

**Root cause (issue #210):** `AudioTagService.GetTrackMetadata` dereferenced
`book.Author.Value` (and/or `edition.Book.Value` / `trackfile.Edition.Value`) without
null-guarding. When the lazy-loaded reference was null at tag-write time, the NRE was
caught per-book, the file was skipped, and the import was dropped.

**Fix status:** ✅ Fixed in PR #212. `GetTrackMetadata` now null-guards `Edition`,
`Book`, and `Author`, logs which reference was null (with book/edition ids), and
`WriteTags` skips gracefully when metadata cannot be resolved.

**Triage (if still observed on an unfixed build):**

1. Count the occurrences: `grep -c "Couldn't import book" logs/*.txt`
2. Confirm the NRE is in `AudioTagService.GetTrackMetadata`.
3. Re-scan after the #210 fix is applied; the previously-dropped files should import.

---

## 3. Non-idempotent scan

**Symptom:** Re-scanning a folder that already has imported files aborts the scan.

**Log signature:**

```
Error|CommandExecutor|Error occurred while executing task RescanFolders
System.Data.SQLite.SQLiteException: constraint failed
UNIQUE constraint failed: BookFiles.Path
```

**Root cause (issue #206):** `DiskScanService.Scan` built its `knownFiles` set via a
**prefix** match (`GetFilesWithBasePath`). Any DB row whose stored `Path` did not
prefix-match the scanned folder was invisible to the `ExceptBy`, so a duplicate
`BookFile` was built and `AddMany` → plain `INSERT` → `UNIQUE constraint failed:
BookFiles.Path`, aborting the scan.

**Fix status:** ✅ Fixed in PR #214. `MediaFileService.AddMany` now queries the DB for
the candidate Paths (exact match) and skips any file whose `Path` already exists,
making the insert idempotent.

**Triage (if still observed on an unfixed build):**

1. Confirm the constraint is on `BookFiles.Path`.
2. Identify the duplicate: `SELECT Id, Path, EditionId FROM BookFiles WHERE Path = '<path>';`
3. Re-scan after the #206 fix is applied.

---

## 4. Import stall

**Symptom:** A completed download sits in the queue with
`trackedDownloadState: importFailed` and the message "No importable files found after
5 monitoring cycles", even though the file exists on disk.

**Root cause (issue #205):** Hypotheses include format mismatch (queue `formatType`
says "ebook" but the file is `.m4b`), path mismatch (monitor watching the download
folder instead of the library path), or filename normalization (duplicate `(1)` suffix
defeating the parser).

**Fix status:** ⏳ Under investigation. The import decision should use the actual file
extension for format, not a stale queue `formatType`.

**Triage:**

1. Check the queue record: `GET /api/v1/queue` — note `formatType`, `outputPath`, and
   the file path.
2. Verify the file exists on disk at the library path (not just the download path).
3. Check the logs for the import decision: `grep "Importing book" logs/*.txt`.
4. If the `formatType` does not match the file extension, the format classification is
   stale — re-trigger the import after the #205 fix.

---

## 5. Bibliography hydration gaps

**Symptom:** Books missing `OpenLibraryWorkId` (100% in the field test), series with
no linked books (74.6%), sparse covers.

**Root cause (issue #207):**

- **OpenLibraryWorkId:** Books added via the Hardcover provider (Priority 1,
  `hardcover:work:*` ids) never get `OpenLibraryWorkId` set — the Hardcover GraphQL
  queries do not request OpenLibrary ids, and the orchestrator's id-scope routing pins
  `hardcover:*` books to the Hardcover provider. The only bulk-population path
  (`OpenLibraryIdBackfillService`) is gated on `EnableOpenLibraryProvider`, which
  defaults to `false`.
- **Series links:** Series are created eagerly from an author's full bibliography
  (all works the author has written, per the provider). Only books that are actually
  in the library get `SeriesBookLink` rows. A series with no linked books means none
  of its works are in the library — **expected for a partial library**, not a bug.

**Fix status:** ⏳ Design decision required. Populating `OpenLibraryWorkId` for
Hardcover-sourced books requires cross-referencing Hardcover work ids to OpenLibrary
work ids (a significant feature), or enabling the OpenLibrary provider by default.

**Triage:**

1. Check the config: `grep EnableOpenLibraryProvider config.xml` (absent = disabled).
2. Verify the gap:

   ```sql
   SELECT COUNT(*) FROM Books WHERE OpenLibraryWorkId IS NULL OR OpenLibraryWorkId = '';
   SELECT COUNT(*) FROM Series s WHERE NOT EXISTS (SELECT 1 FROM SeriesBookLink l WHERE l.SeriesId = s.Id);
   ```

3. If OpenLibrary is disabled and the gap is expected, no action is needed. If
   cross-referencing is required, track it as a feature (see issue #207).

---

## 6. Author deletion on provider failure

**Symptom:** Authors are deleted during a refresh, with the log "not found in metadata
and is being deleted", even though the provider still has the data.

**Log signature:**

```
Warn|MetadataProviderOrchestrator|Metadata provider 'Hardcover' failed during get-author-info
     System.Net.Http.HttpRequestException: No route to host (api.hardcover.app:443)
Warn|RefreshAuthorService|Author [hardcover:author:171873][Frank Herbert] not found in metadata and is being deleted
```

**Root cause (issues #204 / #209):** The orchestrator swallows all exceptions (including
transient network errors) and returns `null`. `RefreshAuthorService` treats `null` as
"author removed from metadata" and deletes the author if it has no files. A transient
outage is therefore indistinguishable from a genuine 404.

**Fix status:** ⏳ In progress (issue #209, PR #211). The orchestrator will propagate a
distinct `MetadataProviderUnavailableException` for transport errors, and
`RefreshAuthorService` will not delete an author on a transport/unavailable error.

**Triage (until the fix is merged):**

1. **Do not run author refreshes during a known network outage.**
2. Check the logs for `Metadata provider 'X' failed during get-author-info` immediately
   before the deletion. If the failure was a transport error (not a 404), the deletion
   was a false positive.
3. Restore deleted authors by re-adding them (the provider still has the data).
4. Check provider health: `GET /api/v1/metadata/providers/health`.

---

## References

- [Metadata Provider Outage and Fallback Runbook](METADATA_PROVIDER_RUNBOOK.md)
- [Local Instance Operator Runbook](LOCAL_INSTANCE_OPERATOR_RUNBOOK.md)
- [Dual-Format Tracking (wiki)](../../wiki/Dual-Format-Tracking.md)
- [Metadata Fallback (wiki)](../../wiki/Metadata-Fallback.md)
