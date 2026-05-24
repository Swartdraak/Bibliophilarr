---
description: >
  Step-by-step diagnosis guide for stuck completed downloads in Bibliophilarr.
  Use when a download has been in "completed but not imported" state for more than
  one polling cycle (90 seconds) with files=0.
tools:
  - read
  - search
---

# Stuck Download Diagnosis

Use this prompt when Bibliophilarr logs show a download that is repeatedly processed
by `DownloadProcessingService` with `files=0, identified=0` and never transitions to
an imported or failed state.

## Diagnosis procedure

### Step 1: Confirm the loop

Read the application logs and search for the item name in the pattern:

```
ImportDecisionMaker|Import run complete: files=0 (filtered=0), releases=0, identified=0
```

Record:

- Item name (from log)
- First occurrence timestamp
- Last occurrence timestamp
- Number of occurrences (estimate if log has rotated)

If the same item appears more than 5 times, the download is confirmed stuck.

### Step 2: Verify path mapping

Search debug logs for:

```
RemotePathMappingService|Remapped
```

for the specific item name. Confirm the resolved local path (after remapping) matches
the expected mount structure.

- Expected: `/media/torrents/ebooks/<item-name>/`
- If no entry found, path mapping may not be configured for this client.
- If remapped path is unexpected (wrong root), check `config.xml`
  `<RemotePathMapping>` entries.

### Step 3: Check whether files exist at the mapped path

The mapped path `/media/torrents/ebooks/<item-name>/` should contain at least one
`.epub`, `.mobi`, `.azw3`, `.pdf`, or `.mp3` file. Confirm the directory exists and
is non-empty.

If the directory is empty or does not exist:

→ **Root cause confirmed: download directory is empty.**

Operator action: In qBittorrent, locate the torrent by name. Determine whether:

a) The torrent is still downloading (check percentage). Wait for completion.
b) The torrent is marked complete but files were manually removed or deleted.
   Remove the torrent from qBittorrent. Bibliophilarr will stop processing it
   within the next polling cycle (≤90 seconds).
c) The torrent is seeding but the files are not accessible from Bibliophilarr's
   mounted path. Check the NAS or download client path-remapping configuration.

### Step 4: Check whether the file extension is blocked

If files DO exist at the mapped path, verify that the file extension is on the
Bibliophilarr allowed media format list. Check the quality profile in Settings for
the author being imported.

Known causes:

- Only `.epub` files present but quality profile only allows `.mobi`.
- Files have a `.!qb` or `.part` extension (still downloading — wait for completion).
- A `filelist.txt` or `.torrent` file is present but no actual book file.

### Step 5: Evaluate permanent failure transition

If the issue is a missing-files scenario (Step 3b) and you cannot easily fix the
underlying torrent, as a workaround in the current version:

1. In qBittorrent, remove the torrent (without deleting files).
2. In Bibliophilarr Activity > Queue, if the item is visible, remove it from the queue.
3. Confirm the item stops appearing in the `DownloadProcessingService` log within
   90 seconds.

### Step 6: Long-term fix reference

File a GitHub Issue referencing **AF-01** from `docs/operations/AUDIT-2026-05-24.md`
requesting that `CompletedDownloadService` detect zero-file completed items as a
terminal failure state after N retry cycles. See sprint task S7-01 in
`docs/sprint-7/plan.md`.

## Expected healthy log pattern

A correctly processed download should produce:

```
ImportDecisionMaker|Import run complete: files=1 (filtered=0), releases=1, identified=1
ImportApprovedBooks|Importing 1 files
CompletedBookImport|Imported 1 books
```

If you see `identified=1` but `Importing 0 files`, this is a different issue
(decision rejection, not a missing-file problem). Check `ImportDecisionMaker` for
`Rejected` entries explaining the decision.
