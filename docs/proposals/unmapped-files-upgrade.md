# Unmapped Files Page Upgrade Proposal

**Status**: PROPOSAL — no implementation changes yet  
**Author**: Copilot session  
**Date**: 2026-03-25  
**Scope**: `frontend/src/UnmappedFiles/` and supporting backend endpoints

## Problem Statement

The Unmapped Files page currently provides a minimal table view of files with
`bookId: 0`. Users must rely on Interactive Import (modal-based, one file at
a time) or bulk delete to manage unmapped files. For libraries with many
unmatched imports, this workflow is too slow and does not surface actionable
information.

## Current Capabilities

| Feature | Status |
|---|---|
| Table view with Path, Size, Date Added, Quality, Actions | Working |
| Interactive Import modal (manual file → book assignment) | Working |
| File details modal | Working |
| Bulk deletion with confirmation | Working |
| Rescan folders with add-missing-authors flag | Working |
| SignalR live updates on `bookFileUpdated` | Working |

## Identified Gaps

1. **No search/filter** — Cannot narrow file list by name, format, size, or date.
2. **No format-based grouping** — EPUBs, PDFs, audiobooks all mixed.
3. **No heuristic matching suggestions** — No automated comparison of filename
   patterns against library authors/books to suggest likely matches.
4. **No bulk reassignment** — Cannot select multiple files and assign them to
   a single book/author in one action.
5. **No duplicate detection** — No indicator when multiple unmapped files share
   identical content (hash or title match).
6. **No archive/ignore list** — Files excluded from auto-import remain in the
   unmapped view permanently with no way to suppress them.
7. **No folder-scoping** — All unmapped files from all root folders are mixed,
   making triage across large imports difficult.

## Proposed Enhancements (Priority Order)

### P1 — Filter and Search

- Add a text filter for filename substring matching.
- Add dropdown filters for file format (EPUB, PDF, MOBI, AZW3, MP3, M4B, etc.)
  and root folder path.
- Wire filters through the existing client-side collection infrastructure
  (`createSetClientSideCollectionFilterReducer`).

### P2 — Heuristic Match Suggestions

- On page load (or button press), run a lightweight fuzzy match of each unmapped
  file's filename/path against `state.authors.items` names and
  `state.books.items` titles.
- Display a "Suggested Match" column showing the best candidate with a
  confidence score. Clicking the suggestion opens a pre-filled Interactive
  Import modal.
- Use the existing `FuzzyMatch` / `LevenshteinCoefficient` utilities already
  available in `StringExtensions.cs` for backend matching, or do client-side
  matching for responsiveness.

### P3 — Bulk Assign / Reassign

- Allow multi-select (checkboxes already exist for delete) and add an
  "Assign to Book" footer action.
- Footer action opens a book/author search modal. On confirm, POST to a new
  bulk-assign API endpoint that sets `bookId` on all selected files.
- Backend endpoint: `POST /api/v1/bookfile/bulk` accepting
  `{ bookFileIds: [], bookId: int }`.

### P4 — Ignore List

- Add an "Ignore" action per row and in bulk footer.
- Ignored files are moved to a separate `ignored` filter tab, excluded from
  heuristic matching, but still accessible for undo.
- Store ignore flag as a boolean column on the BookFile entity, guarded by a
  safe, idempotent migration.

### P5 — Duplicate Detection

- Compute file hashes (SHA-256 of first N bytes) on import.
- Group files with matching hashes in the table view, with a "duplicates" badge.
- Allow bulk delete of duplicates while keeping one.

### P6 — Folder Scope View

- Add a tree or accordion view grouping unmapped files by root folder and
  subdirectory.
- Support folder-level actions: "Assign entire folder to author" or
  "Interactive Import folder."

## Implementation Approach

- All changes should be incremental and independently shippable.
- P1 (filters) can be built entirely in the frontend with zero backend changes.
- P2 depends on whether matching is client-side or server-side. Recommend
  starting client-side with `sortName` / `cleanTitle` comparisons, then
  graduating to a backend endpoint if performance is a concern for large
  libraries.
- P3 requires a new backend endpoint but uses existing `BookFileService`
  internals.
- P4–P6 require migrations and should be batched in a single migration slice.

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Large file counts slow client-side matching | Virtualized table already in place; limit heuristic to visible rows or paginate |
| Migration for ignore flag on BookFile table | Idempotent `ADD COLUMN IF NOT EXISTS`, backward compatible |
| Bulk assign could corrupt mappings | Validate bookId exists before assignment; add undo via history tracking |

## Dependencies

- No external API or provider dependencies.
- Requires existing `BookFileController`, `BookFileService`, and
  `UnmappedFilesTable*` components.

## Success Criteria

- P1: User can filter unmapped files by filename, format, and root folder.
- P2: At least 60% of unmapped files with parseable filenames show a correct
  suggested match.
- P3: User can assign 10+ files to a book in a single operation.
- P4: Ignored files do not reappear in the default view.
