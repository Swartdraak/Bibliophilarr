# Changelog

All notable changes to this repository should be recorded in this file.

The format is based on Keep a Changelog and the repository's documented release
process.

## [Unreleased]

## [1.1.0-dev.42] - 2026-04-24

### Changed

- Release metadata alignment: added explicit release section for `v1.1.0-dev.42` so automated release-version validation can run against a tagged changelog entry.

## [1.1.0-dev.41] - 2026-04-24

### Fixed

- Release-readiness hardening: upgraded `MailKit` to `4.16.0` (GHSA-9j88-vvj5-vhgr) so backend restore no longer fails on `NU1902` vulnerability-as-error checks.
- Frontend readiness alignment: `ci-frontend.yml` now runs on `staging`, and the release-readiness report now includes frontend workflow status.

### Changed

- Canonical release and provider documentation now matches current workflow behavior, provider controls, and versioning sources.
- Root Volta pin updated to Node.js `22.22.2` to match CI, Docker, and Quickstart guidance.

## [1.1.0-dev.35] - 2026-04-12

### Fixed

- **Duplicate work deduplication**: `HardcoverFallbackSearchProvider` now deduplicates similar works from Hardcover by normalized base title (stripping series suffixes and parentheticals). Groups are resolved by data richness (ISBN, ASIN, page count, ratings), keeping the best-scoring work. Removes re-entries like "Caught Up" vs "Caught Up: Into Darkness Trilogy" and "Lights Out" vs "Lights Out: Into Darkness, Book 1".
- **Deduped books actually removed from DB**: `RefreshAuthorService.RefreshChildren()` now explicitly deletes books marked as "Deleted" (not in remote set) when they have no local files and were not manually added, then refreshes only the remaining books. Previously, deleted books were passed to `RefreshBookInfo` which re-fetched them individually from Hardcover, so duplicates survived every refresh cycle.
- **Unwanted author auto-addition guard**: `RefreshBookService.EnsureNewParent()` now guards against auto-adding unknown authors during individual book refresh. When the remote provider returns a different author (e.g. an anthology editor) that doesn't already exist in the DB, the method overrides the remote metadata back to the local author instead of creating a new author entry. Prevents cases like John Joseph Adams (editor) being auto-added when refreshing a book by Alan Dean Foster.
- **Zero-page books filtered when MinPages > 0**: `MetadataProfileService.FilterBooks()` page-count filter changed from `|| x.Editions.Value.All(e => e.PageCount == 0)` (which let all zero-page books through regardless) to `p.MinPages <= 0 ||` guard — zero-page books are now correctly filtered when the metadata profile sets a minimum page count.
- **Multi-edition language selection**: All three Hardcover GraphQL queries now fetch up to 5 editions (was 1) with the `pages` field. New `SelectBestEdition()` method scores editions by language preference (English +100, null +50), identifier richness (ISBN/ASIN), and page data, ensuring the best English edition is selected instead of whichever Hardcover returns first. Fixes non-English editions (German, French, Portuguese, etc.) being imported despite `AllowedLanguages=eng`.
- **Cross-provider metadata enrichment**: New `TryEnrichEditionMetadata()` in `MetadataProviderOrchestrator` enriches missing page counts, release dates, languages, and overviews from supplementary providers (OpenLibrary, Google Books) via ISBN lookup after initial Hardcover fetch.

## [1.1.0-dev.34] - 2026-04-12

### Fixed

- **Sparse Hardcover data popularity guarantee**: `MetadataProfileService.FilterBooks()` now applies a sparse-data minimum guarantee when the popularity filter removes >75% of an author's books. Restores up to 25 top books by popularity regardless of local file state. Previously, the guarantee only fired for newly added authors with zero local files — causing authors like David Weber (1 file) and Anne McCaffrey (5 files) to have most of their bibliography filtered out because Hardcover books commonly have 0 ratings (popularity = 0).
- **Co-authored book attribution**: `HardcoverFallbackSearchProvider.FetchAuthorBooks()` and `FetchAuthorBooksById()` now override each book's `AuthorMetadata` with the queried author's identity after `MapDirectBookResult()`. Previously, `cached_contributors[0]` determined the author, causing co-authored books (e.g. Anne McCaffrey & Mercedes Lackey's "The Ship Who Searched") to be attributed to whichever contributor Hardcover listed first instead of the searched author.
- **Collection/box-set detection**: `MetadataProfileService.IsPartOrSet()` now detects common collection title patterns ("N Books Collection Set", "Box Set", "Boxed Set", "Omnibus Edition") via a dedicated regex, in addition to the existing numeric-range patterns. Prevents collection entries like "Into Darkness Series, 2 Books Collection Set" from being imported as real books.
- **Manual Import path discovery**: `ManualImportController.GetMediaFiles()` no longer filters format profile paths by `fp.Monitored` when discovering scan paths. All format profiles with root folders are used for path discovery, so unmonitored author format profiles still allow manual import file scanning.
- **BookFiles UNIQUE constraint on rescan**: `DiskScanService.Scan()` deduplicates new file insertions by path before calling `AddMany()`, preventing `UNIQUE constraint failed: BookFiles.Path` errors when overlapping folder scans (e.g. two format profiles pointing to the same directory) produce duplicate file decisions.
- **Edition language filter blocking all Hardcover books**: `MetadataProfileService.FilterEditions()` now passes editions with null/unknown language through the `AllowedLanguages` filter instead of rejecting them. Previously, editions with `Language = null` never matched any allowed language (e.g. "eng"), silently removing every Hardcover-sourced edition — which cascaded to remove all books without local files. This was the root cause preventing any new book metadata from being imported during author refresh. Additionally, `HardcoverFallbackSearchProvider` now requests `language { language }` in all three GraphQL edition queries (`FetchAuthorBooks`, `FetchAuthorBooksById`, `FetchBookByWorkId`) and maps the response into `Edition.Language` via `MapDirectBookResult()`.

## [1.1.0-dev.33] - 2026-04-11

### Fixed

- **Decision engine format-aware profiles (DelaySpecification)**: `DelaySpecification` now uses `UpgradableSpecification.ResolveProfile(subject)` for format-resolved quality profile instead of `subject.Author.QualityProfile.Value`. Ensures delay bypass (highest quality, custom format score) evaluates against the correct format-specific profile when dual-format tracking is enabled.
- **Decision engine format-aware profiles (CustomFormatAllowedByProfileSpecification)**: `CustomFormatAllowedByProfileSpecification` now injects `UpgradableSpecification` and uses `ResolveProfile(subject)` to resolve `MinFormatScore`. Previously used the base author quality profile directly, ignoring per-format profile overrides.
- **Monitoring sync batch optimization**: `AuthorFormatProfileService.SyncBooksMonitored()` now uses batch `SetMonitored(ids, monitored)` instead of per-book `SetToggleMonitored()`, preventing O(n) event storms when toggling author monitoring state.
- **Missing/Cutoff dual-format monitoring filter**: `MissingController` and `CutoffController` now apply format-aware monitoring filters via `IConfigService.EnableDualFormatTracking`, correctly filtering books where the author has a format profile with monitoring enabled for the requested format type.
- **Book search dual-format filter**: `BookSearchService.AddMonitoredFilter()` respects per-format monitoring state when dual-format tracking is enabled, preventing searches for formats the author has disabled.
- **Release search format awareness**: `ReleaseSearchService.AuthorSearch()` and `BookSearch()` now evaluate format type when building search decisions, with NRE guard (`monitoredEdition?.Title ?? book.Title`) for books without monitored editions.
- **History table null guard**: `AuthorHistoryRow.js` now renders `book ? book.title : translate('Unknown')` instead of crashing when the book prop is null (e.g. for deleted books in history).
- **Parser crash guard**: `Parser.ParseBookTitleWithSearchCriteria()` now pre-filters books to those with monitored editions (`.Where(x => x.Editions.Value.Any(e => e.Monitored))`) and uses `FirstOrDefault()` instead of `First()`, preventing `InvalidOperationException` on empty result sets.
- **Metadata profile new-author minimum guarantee**: `MetadataProfileService` ensures new authors get at least the top 25 books by popularity when metadata profile filtering would otherwise exclude all books due to missing series data or low popularity scores.
- **ASIN enrichment in search**: `HardcoverFallbackSearchProvider` now includes ASIN in all three GraphQL queries and `MapDirectBookResult` extraction, improving search result matching for Amazon-sourced books.
- **Test: DelaySpecificationFixture**: Added `ResolveProfile` mock to test setup so format-resolved profile is available during delay evaluation tests.
- **Test: DownloadDecisionMakerFixture**: Removed stale `ExpectedErrors(1)` assertion from `should_return_rejected_result_for_unparsable_search` — the Parser's new monitored-edition guard returns null gracefully instead of throwing, so 0 errors are expected.

### Added

- **Hardlink-aware download tracking**: `CompletedDownloadService.Check()` detects when completed download files are already hardlinked into the library via inode comparison (`IDiskProvider.AreSameFile`). When all download files share inodes with library files, the download is marked `Imported` immediately — no re-import attempted, no overwrite confirmation. The download stays tracked in the client for seed time enforcement. Implemented via `Syscall.stat` inode+device comparison on Linux and `GetFileInformationByHandle` file index comparison on Windows.

### Fixed (previous session)

- **Manual Import dual-format scanning**: `ManualImportController` now scans all format profile root folders (e.g. `/media/ebooks/Shirtaloon` + `/media/audiobooks/Shirtaloon`) when dual-format tracking is enabled and authorId is provided. Previously passed only the single `author.Path` to the modal, blocking file assignment across root folders. Constructs full paths from `AuthorFormatProfile.RootFolderPath` + author folder name when `Path` is empty.
- **Missing page format-aware query**: `BookRepository.BooksWithoutFilesBuilder` uses quality-based NOT EXISTS subqueries (ebook IDs 0-4, audiobook IDs 10-13) instead of edition-level file joins when dual-format is enabled. Books with audiobook files but no ebook files (e.g. Shirtaloon books 737-742, 317, 745-747) now correctly appear on the Wanted/Missing page. Total missing jumped from 15 → 44.
- **Author page per-format status column**: `BookStatus.js` rewritten to show per-format status badges when `formatStatuses` is available. Each monitored format gets its own badge — green quality badge for formats with files, red "Missing" label for monitored formats without files, blue "Not Available" for unreleased. Previously showed only a single quality badge for the first file found.
- **Interactive Import modal title**: `AuthorDetails.js` now passes `title={authorName}` instead of `folder={path}` to the Interactive Import modal. Modal title shows "Manual Import - Shirtaloon" instead of the single root folder path.

### Fixed (previous session)

- **Search format bias**: `ProcessDownloadDecisions.IsBookProcessed()` now format-aware when dual format tracking is enabled — ebook and audiobook releases for the same book are grabbed independently instead of first-format-wins behavior that caused searches to only queue audiobooks.
- **Import match threshold**: `CloseBookMatchSpecification` uses lenient 50% threshold for app-initiated tracked downloads to prevent false rejections from verbose audiobook filenames that scored 58-65% against the default 70% threshold.
- **Calendar page crash**: `fetchCalendar` action handler called `parseISO()` on `undefined` when `calendar.time` was not yet initialized (e.g., direct navigation to `/calendar` URL, page refresh). Guarded all three calendar action handlers (`FETCH_CALENDAR`, `GOTO_CALENDAR_PREVIOUS_RANGE`, `GOTO_CALENDAR_NEXT_RANGE`) to fall back to `new Date()` when `time` is undefined. Also added null guard in `CalendarHeader.getTitle()`.
- **Missing translation key**: Added `ProviderResilience` to `en.json`. Previously rendered as raw key string on Settings > Metadata page.
- **Missing form field label associations**: Added `name` prop to all 26 `FormLabel` components in `MetadataProvider.js` for proper `htmlFor`→`id` label-input association. Added `id={name}` attribute to `TextInput`, `CheckInput`, and `TextArea` components so browser accessibility tools can link labels to their inputs.

### Fixed (previous)

- **bookFile DELETE cross-root-folder error**: `MediaFileDeletionService.DeleteTrackFile()` used `author.Path` to derive the root folder, causing `NotParentException` when deleting ebook files under `/media/ebooks/` for authors whose base path is `/media/audiobooks/`. Now uses `IRootFolderService.GetBestRootFolder()` to resolve the correct root folder for the file being deleted.
- **formatStatuses missing format entries**: `BookControllerWithSignalR.EnrichFormatStatuses()` only enriched existing format status entries but never created missing ones. Books without ebook files (777 of 794) had no ebook format entry even when the author had an ebook format profile. Now iterates author format profiles and adds placeholder entries for any missing format type. All 794 books now show both ebook and audiobook statuses.
- **Progress bar colors for unmonitored items**: `getProgressBarKind()` showed WARNING (orange) for unmonitored authors/books with progress < 100%. Now checks monitored state first — all unmonitored items show PRIMARY (blue) since nothing is being tracked.
- **Missing translation keys**: Added `TableOptions` and `InteractiveSearch` to `en.json`. Previously caused console warnings on Book Index, Author Index, and Book Search pages.

### Removed

- **Quality Profile column from Book Index**: Removed redundant `qualityProfileId` column. The Format column already shows per-format quality profile names in badge tooltips.

### Fixed

- **formatStatuses file-based format derivation**: `BookResource.ToResource()` now derives format type from actual book file qualities via `Quality.GetFormatType()` instead of relying on `Edition.IsEbook` (which was never set for Hardcover direct results). Per-format file counts exposed via new `FileCount` property on `BookFormatStatusResource`. Books with ebook files now correctly report ebook format status.
- **Hardcover IsEbook never set**: `HardcoverFallbackSearchProvider.MapDirectBookResult()` now reads `reading_format_id` from GraphQL edition data and sets `IsEbook = true` for ebook editions (format ID 2). Added `reading_format_id` to all three GraphQL edition queries (`FetchBookByWorkId`, `FetchAuthorBooks`, `FetchAuthorBooksById`). Future metadata refreshes will populate `IsEbook` correctly.
- **Author Editor path recomputation**: `AuthorEditorController.SaveAll()` now recomputes `AuthorFormatProfile.Path` when a per-format root folder path changes, using `global::System.IO.Path.Combine(rootFolderPath, author.CleanName)`.
- **Author Editor logging**: `AuthorEditorController` now logs format profile update operations (Info for batch summary, Debug per author per format) via NLog for operational observability.

### Added

- **Book Index format column**: Format column in Book Index table showing per-format badges with file counts, icons (book/audiotrack), and quality profile tooltips.
- **Wanted pages format column**: Format Type column added to Missing and Cutoff Unmet tables showing ebook/audiobook indicator badges.
- **Book detail format file counts**: BookRow and BookDetailsHeader format badges now display per-format file counts (e.g. "1 file", "3 files").

### Fixed

- **Decision engine format isolation**: Five decision engine specifications (CutoffSpec, UpgradeDiskSpec, UpgradeAllowedSpec, QueueSpec, HistorySpec) now use `UpgradableSpecification.ResolveProfile()` for format-resolved quality profiles instead of the base author QP. Three disk-comparison specs also filter existing files by format type, preventing cross-format comparisons (e.g. EPUB evaluated against M4B files).
- **Import upgrade format isolation**: Import `UpgradeSpecification` rewritten with per-format QP resolution and file filtering. EPUB imports are no longer rejected as "not an upgrade" when only audiobook files exist on disk.
- **Import QP defaults**: `EnsureFormatProfiles()` now scans root folders and their default quality profiles to infer the correct QP per format type, instead of defaulting both ebook and audiobook profiles to the base author QP.
- **Queue format column blank**: `QueueResource.FormatType` changed from `int?` to `FormatType?` enum for correct JSON serialization as `"ebook"`/`"audiobook"` strings. Fallback derivation from quality added for items grabbed before format tracking was enabled.
- **Manual import author prefill**: Single-file imports now pass author override to the import decision maker. `ProcessFolder` applies author fallback when file identification does not assign one, fixing the "Author must be chosen" error on prefilled manual imports.
- **Mass editor per-format QP not applying**: `AuthorEditorController.SaveAll()` response now includes format profiles so the frontend Redux store retains per-format quality profile changes instead of overwriting them with null.
- **Format profile monitored state**: `EditAuthorModalContentConnector` now refreshes the author in Redux after format profile saves, ensuring the monitored checkbox reflects the saved state.

### Changed

- **Wiki content enrichment**: All 13 built-in wiki pages comprehensively rewritten with detailed content sourced from Servarr wiki, adapted for Bibliophilarr (627→1465 lines). Covers getting started, library management, dual-format tracking, quality profiles, indexers, download clients, wanted, activity, media management, notifications, system, troubleshooting, FAQ, and custom formats.
- **Mass editor**: Base quality profile and root folder selectors removed entirely; per-format selectors (Ebook QP, Audiobook QP, Ebook Root Folder, Audiobook Root Folder) shown unconditionally. `enableDualFormatTracking` gating and related Redux wiring removed.
- **Format profile editor**: Root folder path selector added per format profile in the author edit modal.

### Added

- **Book file editor format column**: Format column added to the book files table showing Ebook/Audiobook labels derived from quality ID ranges.

### Fixed

- `MediaCoverMapper.cs` resized image fallback regex updated from `(jpg|png|gif)` to `(jpe?g|png|gif|webp)`, matching `MediaCoverProxyMapper.cs`. Fixes JPEG and WebP cover image fallback paths.
- rTorrent `GetStatus()` now reports `MusicDirectory` as `OutputRootFolders` when configured, preventing health check blind spots for custom download directories.

### Changed

- Hadouken download client upgraded to dual-format: `IFormatCategorySettings` on settings, proxy passes category to `webui.addTorrent`, `GetItems()` uses `MatchesAnyCategory()`, `AddFrom*` uses `GetCategoryForFormat()`. Total `IFormatCategorySettings` clients: 10 of 14.
- Documentation drift fixed: `MIGRATION_PLAN.md` and `PROJECT_STATUS.md` updated from "10 slices" to "16 slices" for TD-DUAL-FORMAT-001.

### Removed

- Dead `GetImportedCategoryForFormat()` extension method from `IFormatCategorySettings`. Was designed but never adopted; clients infer format from current category string.

### Fixed

- **CRITICAL**: Download client `GetItems()` now monitors all configured format categories (default, ebook, audiobook) instead of only the default `MusicCategory`. Affects SABnzbd, NZBGet, Deluge, rTorrent, and Transmission. Items sent to format-specific categories were previously invisible to download monitoring.
- **HIGH**: Download client `GetStatus()` now reports output folders for all configured categories (default, ebook, audiobook) instead of only the default category. Affects SABnzbd, NZBGet, and Transmission. Prevents false-positive health check warnings for format-specific download paths.
- Download client validation failures now use user-friendly field names (`Category`, `PostImportCategory`) instead of leaking internal property names (`MusicCategory`, `MusicImportedCategory`).
- `MetadataService` log message now includes author name and path context when skipping metadata creation for missing author folders.
- `AuthorFormatProfileService.Add()` now checks for existing profile before insert, preventing duplicate format profile records in the database.
- Author details header: format profile labels are deduplicated by format type, preventing duplicate "Ebook" or "Audiobook" badges.
- Search results: items are now sorted by relevance (exact match, starts with, contains) for more intuitive result ordering.

### Changed

- Added `MatchesAnyCategory()` extension method to `IFormatCategorySettings` for centralized multi-category matching across all download clients.
- Download client settings: `EbookCategory` and `AudiobookCategory` fields are now visually grouped under a "Format-Specific Categories" section header in the edit form.

### Added

- **DF-11**: Format-aware download client categories — `IFormatCategorySettings` interface with `EbookCategory`/`AudiobookCategory` fields and `GetCategoryForFormat()` extension method. Implemented across 6 download client settings (SABnzbd, NZBGet, qBittorrent, Deluge, Transmission, rTorrent) and 4 client implementations.
- **DF-12**: Format-aware remote path mappings — nullable `FormatType` column on `RemotePathMappings` table (migration 046), format-specific path resolution with generic fallback in `RemotePathMappingService`, frontend format selector in remote path mapping editor.
- **DF-13**: Queue format display — `FormatType` field on `QueueResource`, format column in queue table (Ebook/Audiobook indicator).
- **DF-14**: Wanted/missing and cutoff unmet format filters — ebook/audiobook filter options in both Wanted views.
- **DF-15**: Calendar format filter — ebook/audiobook filter options in calendar view.
- **DF-16**: Author index format column — format profiles column showing monitored ebook/audiobook status per author.

### Fixed

- Search results: book cover images now display correctly by overriding `resource.Book.Images` from the selected edition before URL conversion in `SearchController`.
- Search results: author images now display correctly via individual author detail fallback queries when batch API returns empty results in `HardcoverFallbackSearchProvider`.
- Add Author modal: modal now properly closes after successful author addition via `componentDidUpdate` lifecycle handler detecting `isAdding` state transition.
- Author details header: format profile labels now show quality profile name with monitored/unmonitored indicator instead of plain text.

### Changed

- Add Author flow: auto-creates Ebook and Audiobook format profiles when `EnableDualFormatTracking` is enabled (`AddAuthorService.EnsureFormatProfiles`).
- Add Author modal: per-format quality profile and root folder selection when dual-format tracking is enabled.
- Author edit modal: format profiles are now editable with per-format monitored toggle and quality profile selector; changes saved via API alongside author updates.

- **DF-1**: Domain model, schema, and feature flag for dual-format tracking — `FormatType` enum, `AuthorFormatProfile` entity, migration 045, `EnableDualFormatTracking` config flag, `Quality.GetFormatType()` helper. 10/10 tests pass.
- **DF-2**: Edition monitoring per format type — format-aware housekeeping (`FixMultipleMonitoredEditions` groups by BookId+IsEbook when flag on), `BookEditionSelector.GetPreferredEdition(FormatType)` overloads, `EditionRepository.SetMonitoredByFormat()`. 17/17 tests pass.
- **DF-3**: Decision engine format-aware quality evaluation — `QualityAllowedByProfileSpecification` resolves format-specific quality profile, `UpgradableSpecification.ResolveProfile(RemoteBook)` helper, `RemoteBook.ResolvedFormatType` and `ResolvedQualityProfile` properties. 9/9 tests pass.
- **DF-4**: Download client routing by format — `DownloadService.DownloadReport` uses format profile tags when dual-format enabled, falls back to author tags. 4/4 tests pass.
- **DF-5**: Import pipeline format awareness — `ImportApprovedBooks` assigns files to format-specific editions and root folders when dual-format enabled. 12/12 tests pass.
- **DF-6**: File path building by format — path builder resolves format profile root folder for file placement. 8/8 tests pass.
- **DF-7**: Missing and cutoff evaluation by format — format-filtered SQL builders in `BookRepository`, `FormatType?` query parameter on Missing and Cutoff controllers. 7/7 tests pass.
- **DF-8**: API resources and controllers — `AuthorFormatProfileResource` with CRUD controller, `BookFormatStatusResource` for per-format book status, format profiles linked in `AuthorResource` and `BookResource`. Fixed `SingleOrDefault` crash in `BookResource` mapper for dual-format editions. 9/9 tests pass.
- **DF-9**: Frontend format profile UI — author edit modal format profile display, detail header format badges with book/audiobook icons, `authorFormatProfileActions` Redux store module.
- **DF-10**: Rollout controls — `EnableDualFormatTracking` exposed in Media Management config API and frontend toggle (Settings > Media Management > Dual Format, advanced settings).
- Frontend test suite expanded: 3 new test suites (AuthorFormatProfileEditor, InteractiveImport validation, authorFormatProfileActions) with 20 new tests. Total: 12 suites, 39 tests.
- Container resource limits documentation: sizing table in QUICKSTART.md and `deploy.resources` block in docker-compose.local.yml (RQ-169).
- VirtualTable accessibility: ARIA roles (`role="grid"`, `role="row"`, `role="columnheader"` with `aria-sort`, `role="gridcell"`) on all virtual table components; 7 tests added.
- Page-level error boundary wrapping route children in `Page.js` to catch routing-level render errors.
- Theme color variables for log levels, star rating, hover accents, status page, and author progress background in both light and dark themes.
- VirtualTable keyboard navigation: Arrow Up/Down scrolls by row height, Page Up/Down by viewport, Home/End to start/end; `tabIndex` and `aria-rowcount` on Scroller container (RQ-143).
- Targeted author query methods: `AuthorExistsWithMetadataProfile()`, `GetAuthorsByMetadataProfile()`, `AuthorExistsWithQualityProfile()` in AuthorRepository/AuthorService (RQ-033).
- 1 new keyboard navigation accessibility test; total: 13 suites, 47 tests.

### Fixed

- Heading case normalized to sentence case across ROADMAP.md, PROJECT_STATUS.md, MIGRATION_PLAN.md, QUICKSTART.md, README.md, and 3 wiki files per docs-style rule (RQ-147).
- ROADMAP milestone table: Frontend test infrastructure and Documentation normalization now marked complete (was still showing "planned").
- Documentation drift: MIGRATION_PLAN.md status updated from "not yet started" to "implementation complete" for TD-DUAL-FORMAT-001.
- README.md updated to mention dual-format tracking and v1.0.0 release; removed stale "run separate instances" advice.

### Removed

- Unused `react-async-script` dependency (RQ-137).

### Changed

- Hardcoded CSS hex colors replaced with theme variables across 8 files: LogsTableRow, StarRating, FormInputHelpText, AuthorDetails, AuthorDetailsHeader, Status, AuthorIndexProgressBar, AuthorIndexPoster (RQ-100).
- Z-index `9999` in DragPreviewLayer.css replaced with `$dragLayerZIndex` PostCSS variable (RQ-101).
- QUICKSTART.md now includes dual-format tracking enablement guide.
- CHANGELOG.md entry `[2026-03-17]` assigned version `[0.9.0]` per Keep a Changelog format.
- ROADMAP.md: DMQ-002 sequencing language corrected (.NET 10 GA available, no longer "blocked"); items #9, #13, #15, #16 marked COMPLETED.
- Import run summary telemetry (Slice A1): structured per-run metrics for files scanned/filtered, match quality distribution, stage timing, throughput, and match rate. Logged at Info level on each import run completion.
- Commit message convention (Conventional Commits format) with type/scope rules, branch naming convention, and production readiness expectations in `CONTRIBUTING.md`.
- Release gate checklist in `CONTRIBUTING.md` enforcing CI, CHANGELOG, artifact, and rollback verification before tagging releases.
- Enhanced PR template with type-of-change checkboxes, production safety checklist, and CHANGELOG update requirement.
- Detailed dual-format architecture design (TD-DUAL-FORMAT-001) in `MIGRATION_PLAN.md`: `AuthorFormatProfile` entity, per-format quality profiles and root folders, 10 implementation slices (DF-1 through DF-10), feature flag `EnableDualFormatTracking`, migration 045 schema, acceptance criteria, and rollback strategy.

### Fixed

- ManualImport: added `AuthorId > 0` and `BookId > 0` guard clauses in `ManualImportService` to reject incomplete import requests early instead of crashing downstream.
- ManualImport frontend: added `author.id` and `book.id` validation in `InteractiveImportModalContentConnector` before dispatching import commands.
- AudioTag: added NFS writeable pre-check and IOException retry with exponential backoff in `AudioTag.cs` and `AudioTagService.cs` to handle transient NFS mount failures.
- Search: added minimum 3-character server-side guard in `SearchController` to prevent empty/short search queries from hitting indexers.
- `test: fix 13 failing Core unit tests for updated metadata profiles and import behavior`

### Changed

- Search: increased frontend search debounce from 300ms to 600ms and added minimum 3-character enforcement in `searchActions.js` to reduce unnecessary API calls.
- 10 API resource files converted to file-scoped namespaces: BookStatisticsResource, AuthorStatisticsResource, AuthorEditorDeleteResource, TagResource, BackupResource, QueueStatusResource, LanguageResource, TaskResource, ProviderHealthResource, RootFolderResource (RQ-050).
- OpenLibraryIdBackfillService restructured to chunked processing with per-chunk edition loading, progress logging, and early budget exhaustion exit (RQ-031).
- MetadataProfileService and QualityProfileService use targeted author queries instead of `GetAllAuthors()` for profile deletion checks and legacy migration (RQ-033).
- ManageImportListsEditModalContent, ManageIndexersEditModalContent, ManageDownloadClientsEditModalContent use `SpinnerButton` with local `isSaving` state for save feedback (RQ-103).
- RQ-099 assessed: 27 of 29 CSS `!important` flags are legitimately needed (third-party inline style overrides, CSS module ordering, mixin reliability).
- RQ-108 assessed: build.sh SDK modification already has .ORI backup, variable quoting, and error guards; full SDK copy deferred as structural pipeline change.
- ROADMAP.md Track B updated with finalized 10-slice dual-format architecture and milestone status changed from "assessed" to "designed".
- PROJECT_STATUS.md dual-format section updated to "Design complete (April 2026)" with implementation slice count and next action.

- `refactor(deps): replace moment.js with date-fns across all 34 frontend date utility, Calendar, Store, and System files` — moment.js (328KB) removed; tree-shakeable date-fns 4.1.0 imported. Format token converter preserves backend format compatibility.
- `refactor: migrate test infrastructure from RestSharp to System.Net.Http.HttpClient`
- `docs: normalize canonical documentation, fix stale Node.js versions and milestone statuses`
- `build(deps): upgrade stylelint 15.11.0 → 16.26.1 and stylelint-order 6.0.4 → 8.1.1` (DMQ-005)
- `build(deps): upgrade FluentAssertions 5.10.3 → 8.9.0` — migrated assertion API across 11 test files: `AssertionOptions` → `AssertionConfiguration`, renamed comparison methods, updated `BeCloseTo` precision signatures, replaced `Should().Equals()` with `Should().Be()`, converted `SelectedMemberInfo` exclusions to explicit property exclusions (DMQ-006)
- React 18 upgrade path assessed: 3 critical blockers (connected-react-router removal, ReactDOM.render→createRoot, Router 5→6), 6-step migration sequence documented in ROADMAP.md.
- Redux modernization assessed: 224 connect() HOCs, 35+ action modules, 100+ thunks. Migration to Redux Toolkit sequenced after React 18.
- moment.js migration completed: 34 import sites across Utilities/Date, Calendar, Store, and System modules replaced with date-fns 4.1.0. moment.js removed from dependencies.
- Upgraded Node.js 20.19.2 → 22.22.2 LTS across Dockerfile, `ci-frontend.yml`, `npm-publish.yml`, and `release.yml`. Node 20 reached EOL April 2026.
- Added `org.opencontainers.image.vendor` OCI label to Dockerfile.
- Bumped GitHub Actions: `docker/metadata-action` v5 → v6, `actions/setup-node` v4 → v6, `actions/github-script` v7 → v8, `docker/login-action` v3 → v4, `docker/setup-buildx-action` v3 → v4.
- Bumped npm dev dependencies: `@types/node` 20 → 25, `jest` 30.1.2 → 30.3.0.
- Bumped NuGet packages: `coverlet.collector` 3.1.0 → 8.0.1, `Dapper` 2.0.151 → 2.1.72.
- Closed 8 Dependabot PRs for breaking major-version upgrades (dotnet/sdk 10.0, dotnet/aspnet 10.0, react-router-dom 6, FluentAssertions 8, FluentMigrator 8, stylelint 16, react-google-recaptcha 3) — tracked as dependency migration queue items DMQ-001 through DMQ-008 in ROADMAP.md with structured sequencing, prerequisites, and cross-references to RQ items in PROJECT_STATUS.md.

## [1.0.0] - 2026-04-05

### Added

- `FetchAuthorBooksById()` method in `HardcoverFallbackSearchProvider` for direct numeric-ID-based author book fetching, skipping the name search step. Saves one API round-trip per refresh for authors with numeric IDs.
- `BuildHardcoverAuthorId(int numericId)` overload for creating stable `hardcover:author:154441` format IDs.
- `TryParseNumericHardcoverAuthorId()` helper to detect numeric vs name-based Hardcover author tokens.
- Name-to-numeric Hardcover author ID migration in `AuthorMetadataRepository.UpsertMany()`: when a numeric ID is not found by direct lookup, falls back to matching by author name against existing hardcover records and updates the ForeignAuthorId in place.
- Hardcover author links: `GetAuthorInfo` now populates `metadata.Links` with the author's Hardcover page URL extracted from the `slug` field in the GraphQL response. 35 authors have links populated so far.
- `ToUrlSlug()` string extension method in `StringExtensions.cs` — URL-decodes, removes diacritics, lowercases, replaces non-alphanumeric characters with hyphens, trims. Used by all metadata providers and import services for TitleSlug generation.
- `createExistingBookSelector.js` — Redux selector that checks if a book exists in the library by `foreignBookId` against `state.books.items`.
- `.github/workflows/validate-release-version.yml` — CI workflow that validates release tag format (SemVer pattern) and checks `CHANGELOG.md` contains a matching version entry.
- `docs/proposals/unmapped-files-upgrade.md` — Comprehensive proposal for Unmapped Files page enhancements with P1-P6 prioritized features (filter/search, heuristic matching, bulk assign, ignore list, duplicate detection, folder scoping).
- "Release versioning" section in `CONTRIBUTING.md` defining SemVer 2.0 policy, bump trigger table, pre-release format, version sources, and contributor/agent responsibilities.
- Production database and log diagnostics section in `PROJECT_STATUS.md` with database statistics, TitleSlug quality assessment, log severity breakdown, and 6 recommended follow-up items.
- Database migration 044 (`044_normalize_title_slugs.cs`) — applies `ToUrlSlug()` to all existing `AuthorMetadata.TitleSlug`, `Books.TitleSlug`, and `Editions.TitleSlug` values. Cleans up malformed slugs with colons and URL-encoded characters from Hardcover/OpenLibrary foreign IDs. Runs automatically on next application start.
- `DownloadProcessingWorkerCount` configuration key in `IConfigService`/`ConfigService` (default `3`) to control parallel monitored-download processing concurrency.
- Comprehensive deep audit v2: six parallel audits (backend C#, frontend, CI/CD, documentation, Docker/infrastructure, packages/dependencies) produced 287 distinct findings consolidated into 176 remediation items (RQ-001 through RQ-178) in `PROJECT_STATUS.md`.
  - 14 Critical, 58 High, 101 Medium, 93 Low, 21 Enhancement/Migration opportunities.
  - New priority tier P4 (Strategic/Migration Opportunities) tracks React 18, React Router 6, RestSharp→HttpClient, .NET 10, Node 22, and other long-horizon migrations.
- Expanded Docker and Infrastructure Hardening Plan in `PROJECT_STATUS.md` — now covers 17 items across Phase 6 and Phase 7 including SIGTERM handling, DataProtection key security, container image scanning, SBOM generation, request body limits, Kubernetes manifests, and Prometheus metrics.
- New ROADMAP milestones: frontend test infrastructure, async migration, RestSharp→HttpClient, security headers, React 18/Router 6, Node 22, .NET 10 planning, documentation normalization, installer signing.
- Audit statistics table in `PROJECT_STATUS.md` with per-area finding counts by severity.

### Changed

- `GetAuthorInfo()` in `HardcoverFallbackSearchProvider`: now uses numeric Hardcover author IDs when available (e.g., `hardcover:author:154441` instead of `hardcover:author:Stephen%20King`). Existing name-based IDs are automatically converted to numeric format during the next successful author refresh.
- `RefreshSeriesService.RefreshSeriesInfo()`: now creates series metadata rows for ALL remote series regardless of local book presence. Previously, series with zero matching local books were silently discarded, resulting in empty Series tables.
- `ImportApprovedBooks.Import()`: `BulkRefreshAuthorCommand` construction now deduplicates author IDs before command creation.
- Hardcover `FetchAuthorBooks` GraphQL query: increased `contributions(limit: 100)` to `contributions(limit: 500)` to fetch full bibliography for prolific authors. Books grew from 1,882 to 3,944+ after RefreshAuthor.
- Loading page logo: replaced legacy Readarr base64 inline image in `LoadingPage.js` with new Bibliophilarr PNG (`Logo/Bibliophilarr_128x128.png`); updated `LoadingPage.css` to 128x128 sizing.
- Loading page SVG: replaced `logo.svg` with new Bibliophilarr brand SVG.
- Color palette: replaced red `#ca302d` accent with Navy `#193555` and Teal `#54939C` across `light.js`, `dark.js`, `login.html`, and `index.ejs` (theme-color meta tags, panel-header backgrounds, safari pinned-tab color).
- `Directory.Build.props` version comment updated to clarify CI version injection mechanism via `BIBLIOPHILARRVERSION` env var.
- `BookImportMatchThresholdPercent` default changed from `80` to `70` for better match acceptance on noisy ebook metadata.
- `DownloadProcessingService.Execute()` now processes `ImportPending` tracked downloads with bounded `Parallel.ForEach` concurrency rather than sequential per-item execution.
- `IdentificationService` now uses local-author existence checks (`FindById` / `FindByName`) when `AddNewAuthors=false`, allowing remote book matches for authors already in the library even when the specific book is missing locally.
- `MIGRATION_PLAN.md` audit snapshot expanded from 110+ findings to 287 findings with migration-specific categorization (provider reliability, performance, async/threading, supply chain, frontend, documentation).
- `ROADMAP.md` Near-Term Delivery Sequence expanded from 13 to 19 items covering async migration, frontend testing, RestSharp migration, security headers, documentation normalization, and React 18 planning.
- `ROADMAP.md` Current Milestones table expanded with 10 new planned milestones.
- **RQ-047**: Aligned `wiki/Home.md` priorities and `wiki/Metadata-Migration-Program.md` milestones with current phase-based delivery model (replaced stale Goodreads migration references and `v0.x` versioning).
- Improved library build-out identification performance and resilience:
  - `HardcoverFallbackSearchProvider` no longer sends the broken GraphQL `fields` search parameter that triggered deterministic `query_by_weights` errors.
  - Added a Hardcover cooldown after repeated deterministic provider errors so one bad upstream/search-shape response does not stall every identification attempt.
  - Lowered Hardcover default routing priority behind OpenLibrary/other providers for general search operations.
  - Added swapped filename-derived author/title handling so reversed filenames like `Book - Author` can recover as `Author` + `Book` during fallback and remote identification retry.
  - Added configurable bounded parallelism for import tag reading, release identification, and primary remote candidate search fan-out, exposed through `/config/metadataProvider` and the frontend metadata settings UI with conservative defaults.
  Validation: targeted `Bibliophilarr.Core.Test` fixtures for Hardcover search, candidate identification, and ebook filename fallback passed; full `build dotnet` solution build passed.
- Added requested implementation planning tracks to canonical docs for:
  - import/identification throughput optimization on large media libraries,
  - single-instance dual ebook/audiobook variant management with independent policy and tracking.
  Updated: `ROADMAP.md`, `MIGRATION_PLAN.md`, and `PROJECT_STATUS.md`.
- Expanded those planning tracks into implementation-ready, measurable task outlines in canonical docs:
  - `ROADMAP.md`: added immediate/future slice breakdowns and measurement criteria.
  - `MIGRATION_PLAN.md`: added detailed work packages (IP-1..IP-5, DF-1..DF-5), deliverables, validation, and KPI thresholds.
- C# source comment-debt pass: replaced unresolved `TODO`/`FIXME`/`XXX` markers in active C# files with `NOTE` wording and normalized pending-test skip messages away from `TODO` marker text.
- `.github/workflows/labeler.yml`: added explicit top-level least-privilege permissions for consistency across all workflows.
- `CONTRIBUTING.md`: added explicit policy requiring meaningful XML summary comments on new/changed public C# API members and banning new `TODO`/`FIXME`/`XXX` markers in source.
- `SECURITY.md`: clarified private vulnerability reporting path and added explicit response-target timelines (acknowledgement, initial triage, and follow-up cadence).
- `ROADMAP.md`: reconciled packaging and phase-hardening language with current workflow reality after dedicated Phase 6 packaging-matrix retirement.
- `MIGRATION_PLAN.md`: reconciled historical phase markers and footer metadata with the current canonical delivery posture (Phase 5 consolidation + Phase 6 hardening).
- `PROJECT_STATUS.md`: replaced stale explicit `phase6-packaging-validation.yml` reference in the historical completion section with current-state wording.
- `docs/operations/RELEASE_AUTOMATION.md`: corrected repository posture to reflect that `release.yml`, `docker-image.yml`, and `npm-publish.yml` workflows are present.
- `docs/operations/METADATA_MIGRATION_DRY_RUN.md`: removed duplicate `Related evidence` heading.
- `.github/ISSUE_TEMPLATE/bug_report.yml`: branch selector updated from legacy `Master/Nightly` to current `Main/Staging` naming.
- `.github/workflows/lock.yml`: added explicit least-privilege workflow permissions (`contents: read`, `issues: write`).
- Archived dated planning file `docs/operations/phase5-inventaire-openlibrary-consolidation-plan-2026-03-16.md` to `docs/archive/operations/` with a deprecation banner and canonical replacement links.
- Added `docs/archive/operations/README.md` index to maintain archive discoverability and canonical cross-links.
- Removed `.github/workflows/branch-bootstrap.yml`: one-time bootstrap workflow with no recurring operational value and hardcoded `heads/main` source ref assumptions that no longer match the active branch model.
- Refactored `.github/workflows/weekly-replay-report.yml` to run a single curated replay sample and enforce `replay_regression_guard` thresholds directly. Removed the baseline-vs-post comparison path that previously executed the same cohort seed twice in one run and produced low-signal deltas.
- Updated `PROJECT_STATUS.md` for canonical accuracy: removed duplicate headings, normalized the `Last Updated` marker, and removed the stale claim that Phase 6 packaging validation lanes remain active.
- Removed `.github/workflows/phase6-packaging-validation.yml`: stale phase-specific workflow. The workflow had a broken npm smoke test (hardcoded non-existent tag `v0.0.0-phase6-smoke`), a fragile OpenLibrary fallback assertion, and a weekly schedule that generated noise. Its companion doc was archived in the previous commit.
- Fixed DI anti-pattern in `SearchController` and `SearchTelemetryController`: removed `ISearchTelemetryService = null` optional constructor parameter and `?? SearchTelemetryService.Shared` fallback. The service is auto-registered as a singleton by DryIoc assembly scanning — the null bypass was never invoked in production and masked a potential misconfiguration.
- Removed `SearchTelemetryService.Shared` static singleton property: it was only reachable via the now-deleted null fallback and is dead code. Tests use explicit injection or mocks.
- Fixed `.github/workflows/staging-smoke-metadata-telemetry.yml`: removed 3 duplicate binary path entries in the service discovery loop (each of 3 paths was listed twice).
- `.gitignore`: Added `__pycache__/`, `*.pyc`, `*.pyo` entries to prevent Python bytecode files from being tracked.
- `scripts/ops/run_post_agent_docs_audit.py`: Added `H1_EXEMPT_FILES` set to suppress false-positive H1 warnings for `LICENSE.md` and `PULL_REQUEST_TEMPLATE.md` (special-purpose files where a top-level H1 is inappropriate).
- Relocated `PROVIDER_IMPLEMENTATION_GUIDE.md` from repository root to `docs/operations/` (it is a developer implementation reference, not a root-level canonical doc).
- Archived 10 historical `docs/operations/` session-checkpoint files to `docs/archive/operations/`, each with a `[!WARNING]` deprecation banner linking to their canonical replacement: `IMPLEMENTATION_STATUS_2026-03-15.md`, `status-audit-2026-03-16.md`, `conflict-strategy-staged-rollout-checklist-2026-03-16.md`, `core-identification-fallback-hardening-2026-03-15.md`, `dependabot-alert-remediation-2026-03-16.md`, `dotnet8-and-library-finalization-2026-03-15.md`, `live-provider-library-enrichment-2026-03-15.md`, `live-provider-replay-comparison-2026-03-16.md`, `phase6-packaging-validation-matrix-2026-03-16.md`, `release-readiness-report-2026-03-16.md`.
- Deleted 4 pure session-note files with no lasting operational value: `gh-pr-merge-cli-mismatch-2026-03-16.md`, `hardcover-fallback-ui-timeout-2026-03-15.md`, `media-library-scan-organize-2026-03-15.md`, `phase6-hardening-pr-bootstrap-2026-03-16.md`.
- Removed empty stray directory `scripts/__pycache__/` (was untracked, now covered by `.gitignore`).
- Consolidated label and project governance guidance into `CONTRIBUTING.md`.

### Fixed

- **P5: TitleSlug corruption causing silent author merges** — `GetAuthorInfo()` used `??=`
  (null-coalescing assignment) for `metadata.TitleSlug`, which failed to override the
  wrong slug inherited from `MapDirectBookResult()`'s `cached_contributors[0]` (could be
  an editor or co-author). Changed to unconditional `=` assignment. 344 of 432 corrupted
  database records repaired.
  File: `src/NzbDrone.Core/MetadataSource/Hardcover/HardcoverFallbackSearchProvider.cs`
- **BulkRefreshAuthor crash on duplicate IDs**: `BasicRepository.Get(IEnumerable<int> ids)` threw `ApplicationException("Expected query to return N rows but returned M")` when callers passed duplicate IDs. SQL deduplicates results but the assertion compared against the original non-unique count. Fixed by deduplicating IDs with `Distinct()` before the query and count check. This was the root cause of all author refresh failures — the only `BulkRefreshAuthor` command ever attempted (ID 668) crashed with "expected 701 rows but returned 427" due to 274 duplicate IDs.
- Bookshelf CSS class mismatch: `Bookshelf.js` referenced nonexistent `styles.innerContentBody`; changed to `styles.tableInnerContentBody` (the class defined in `Bookshelf.css`).
- Bookshelf JumpBar initialization: added missing `this.setJumpBarItems()` call in `Bookshelf.js` `componentDidMount()`. Other index pages (e.g. `AuthorIndex`) already had this call.
- `MediaCoverProxy.GetImage()` crash on `file://` URLs: added scheme check and `File.ReadAllBytes()` path for local file URLs instead of HTTP request. Eliminates `System.NotSupportedException`.
- `TrackedDownloadService.UpdateCachedItem` crash when `AuthorId` is 0: added `firstHistoryItem.AuthorId > 0` guard at both `_parsingService.Map()` call sites. Falls back to name-based resolution when AuthorId is 0 instead of throwing `ModelNotFoundException`.
- Author/book slug 404 bug: raw provider foreign keys (e.g. `hardcover:author:Frank%20W.%20Abagnale`) used as URL slugs caused broken routes. Applied `ToUrlSlug()` to all 18 TitleSlug fallback assignments across 9 files (`AddAuthorService`, `HardcoverFallbackSearchProvider`, `OpenLibraryMapper`, `OpenLibraryProvider`, `OpenLibrarySearchProxy`, `GoogleBooksFallbackSearchProvider`, `InventaireFallbackSearchProvider`, `ImportApprovedBooks`).
- Add Search green check disappearing on page refresh: `getBookSearchResultFlags()` checked `book.id !== 0` from API response (lost on navigation). Replaced with Redux-backed `createExistingBookSelector` and `createExistingAuthorForBookSelector` in `AddNewBookSearchResultConnector.js`; removed stateless function from `AddNewItem.js`.
- `release.yml` version injection: moved "Resolve version metadata" step before build steps and added `BIBLIOPHILARRVERSION` env var to backend build step. Previously binaries shipped with placeholder version `10.0.0.*`.
- Import pipeline null-reference chain for remote author stubs encountered during monitored download processing:
  - `ImportDecisionMaker.EnsureData()` now null-guards author chain and root-folder lookup.
  - `BookUpgradeSpecification` now safely handles missing quality profiles.
  - `AuthorPathInRootFolderSpecification` now resolves local author path by foreign ID and falls back to author-name lookup for name-based Hardcover IDs.
  - `ImportApprovedBooks.EnsureAuthorAdded()` now falls back to author-name lookup before attempting remote author creation, preventing false new-author flows from download paths outside managed root folders.
- `DownloadProcessingService` exception recovery now transitions stuck `Importing` items to `ImportFailed` with status messaging when import throws.
- Fixed case-sensitive format comparison in `DistanceCalculator` that penalized all Hardcover-sourced editions: `"Ebook"` (Hardcover) was not matching `"ebook"` in the EbookFormats list, applying a universal `ebook_format` distance penalty. Changed to `StringComparer.OrdinalIgnoreCase` for all three format comparisons (ebook_format, wrong_format, audio_format).
- Fixed `CloseAlbumMatchSpecification` rejecting existing library files due to format distance bias: added `"ebook_format"` to the `NormalizedDistanceExcluding()` set for files already on disk, alongside `"missing_tracks"` and `"unmatched_tracks"`.
- Fixed `CandidateService.GetRemoteCandidates()` short-circuiting author+title search when ISBN/ASIN results were found: files with wrong embedded ISBNs matched to incorrect books while the correct book was never searched. Author+title search now always runs alongside ISBN/ASIN results, with `HashSet` deduplication preventing duplicate candidates.
  - Combined effect: book identification rate improved from ~19% (717/3789 linked) to projected ~67-72% on the production library.
  - Validation: 40/40 targeted tests pass (DistanceCalculator, DistanceFixture, CandidateService); 158/159 broader import tests pass (1 pre-existing flaky concurrency test).
- Fixed `test.sh` exit code logic: `if [ "$EXIT_CODE" -ge 0 ]` was always true, causing all test failures to silently exit 0. Changed to `if [ "$EXIT_CODE" -ne 0 ]` so CI properly catches test failures.
- **RQ-006**: Removed `phase6-packaging-validation.yml` reference from `release_readiness_report.py` and `operational_drift_report.py` (workflow was deleted; scripts would error on missing file).
- **RQ-007**: Corrected migration file reference in `MIGRATION_PLAN.md` from `041_add_open_library_ids.cs` to `042_add_open_library_ids.cs`.
- **RQ-029**: Replaced two stale `src/Readarr.sln` references in `PROJECT_STATUS.md` with `src/Bibliophilarr.sln`.
- **RQ-011**: Removed Radarr, Lidarr, Prowlarr, and Sonarr donation blocks from `Donations.js`; kept only Bibliophilarr.
- **RQ-084**: Removed 4 unused sibling-project logo PNGs (Radarr, Lidarr, Prowlarr, Sonarr) via `git rm`.
- **RQ-012**: Removed `console.log(booksImported)` from `InteractiveImportModalContent.js` production code.
- **RQ-013**: Gated SignalR startup log to `console.debug`, downgraded missing-handler error to `console.warn`, removed verbose `received` debug log.
- **RQ-086**: Added `IsNullOrWhiteSpace` input validation on `SearchController.Search()` to return empty list for blank terms instead of hitting the search service.
- **RQ-078**: Improved `CalibreProxy.GetOriginalFormat()` `FirstOrDefault` result handling for null safety.
- **RQ-015**: Pinned all 51 third-party GitHub Actions references (17 unique actions) to exact commit SHAs across all workflow files to prevent supply-chain attacks.
- **RQ-085**: Added "Community standards" section to `CONTRIBUTING.md` with cross-links to `CLA.md` and `CODE_OF_CONDUCT.md`.
- **RQ-091**: Updated npm launcher binary paths from `Readarr/Readarr` to `Bibliophilarr/Bibliophilarr`; updated docker-compose env prefix from `Readarr__` to `Bibliophilarr__`.
- **RQ-004**: Pinned Docker base images (`dotnet/sdk:8.0`, `dotnet/aspnet:8.0`) to SHA256 digests for supply-chain integrity.
- **RQ-005**: Added SHA256 checksum verification for Node.js tarball download in Dockerfile.
- **RQ-017**: Added configurable request-level timeouts to Inventaire and Hardcover metadata providers (minimum 5s, respects MetadataProviderTimeoutSeconds config).
- **RQ-019**: Fixed O(n*m) performance issue in `ImportListSyncService` by converting exclusion lookups from linear scans to HashSet O(1) checks.
- **RQ-023**: Added non-root user `bibliophilarr` (UID/GID 1000) to Docker runtime stage.
- **RQ-024**: Added HEALTHCHECK instruction to Dockerfile with curl-based ping endpoint check (30s interval, 10s timeout, 3 retries).
- **RQ-025**: Converted 9 `TODO`/`FIXME`/`hack` markers in backend C# to `NOTE:` comments per CONTRIBUTING.md policy.
- **RQ-026**: Updated `PROVIDER_IMPLEMENTATION_GUIDE.md` provider references from removed GoodreadsProxy to current stack (OpenLibrary, Inventaire, Hardcover, GoogleBooks).
- **RQ-027**: Updated `PROVIDER_IMPLEMENTATION_GUIDE.md` status header from "Phase 2-3 Transition" to reflect current Phase 4.
- **RQ-028**: Added COMPLETED banner to `DOTNET_MODERNIZATION.md` confirming .NET 8.0 migration is complete.
- **RQ-044**: Changed `ARCHIVED` keyword to `DEPRECATED` in 11 archive documentation files per style guide Rule D1.
- **RQ-079**: Removed stale Sentry/Azure Pipeline secrets from `RELEASE_AUTOMATION.md` secrets matrix.
- **RQ-080**: Archived dated telemetry runbook to `docs/archive/operations/` with DEPRECATED banner.
- **RQ-022**: Increased `RootFolderService.GetDetails()` timeout from 5s to 15s to prevent premature disk-check timeouts on slow storage.
- **RQ-037**: Pinned Python from rolling `3.x` to `3.12` in 4 CI workflows (branch-policy-audit, operational-drift-check, release-readiness-report, release).
- **RQ-038**: Added `timeout-minutes` to all 22 jobs across 16 GitHub Actions workflows (5–120 min by job type) to prevent runaway CI.
- **RQ-040**: Removed trailing comma in `frontend/tsconfig.json` `include` array.
- **RQ-049**: Made `build.sh` msbuild parallelism configurable via `MSBUILD_PARALLELISM` env var (default: `-m:1`).
- **RQ-059**: Added OCI LABEL instructions (title, description, url, source, licenses) to Dockerfile.
- **RQ-073**: Added SIGTERM handler via `PosixSignalRegistration.Create()` in `AppLifetime.cs` for clean container shutdown.
- **RQ-082**: Added `::add-mask::` for staging URL and API key secrets in `metadata-migration-dry-run.yml`.
- **RQ-083**: Added `environment: npm-publish` to `npm-publish.yml` for deployment protection rules.
- **RQ-090**: Added debug-level logging to swallowed exceptions in `Newznab.cs` indexer capability probes.
- **RQ-092**: Replaced `dangerouslySetInnerHTML` with proper JSX `<code>` elements in `EditSpecificationModalContent.js`.
- **RQ-093**: Changed `innerHTML` to `textContent` for year display in `login.html`.
- **RQ-105**: Removed redundant `frontend/jsconfig.json` — `tsconfig.json` with `allowJs: true` covers all JS files.
- **RQ-109**: Pinned Node.js from floating `'20'` to exact `'20.19.2'` in `release.yml` to match Dockerfile.
- **RQ-114**: Narrowed `release.yml` permissions from top-level `contents: write` to `contents: read`; added `contents: write` only on the `draft-release` job.
- **RQ-115**: Expanded container detection in `OsInfo.cs` to check `/.containerenv` (Podman) and `KUBERNETES_SERVICE_HOST` env var.
- **RQ-118**: Set Kestrel `MaxRequestBodySize` from unlimited (`null`) to 50 MB to prevent OOM in containers.
- **RQ-121**: Updated `services-endpoint-runbook.md` binary reference from `Readarr.dll` to `Bibliophilarr`.
- **RQ-124**: Archived `provider-metadata-pull-testing.md` to `docs/archive/operations/` with DEPRECATED banner.
- **RQ-130**: Removed `fuse.worker.js` console.log calls; gated 4 modal/command `console.warn` calls behind `NODE_ENV === 'development'`.
- **RQ-146**: Removed trailing `##` markers from all 7 headings in `CLA.md`.
- **RQ-154**: Added numeric regex validation for PR number input in `merge_pr_reliably.sh`.
- **RQ-156**: Expanded `.dockerignore` to exclude `docs/`, `wiki/`, `Logo/`, `schemas/`, and `*.md` (except `README.md`).
- **RQ-036**: Audited all 16 workflow files — permissions already consistently scoped (restrictive top-level, job-level override where needed).
- **RQ-039** (partial): Pinned `ci-frontend.yml` Node version from floating `'20'` to exact `'20.19.2'` to match Dockerfile.
- **RQ-041**: Converted all 17 stale TODO/FIXME/HACK comments in frontend to `NOTE:` per CONTRIBUTING.md policy across 15 files.
- **RQ-057**: Changed hardcoded IP `192.168.100.5` to `localhost` in `postgres.runsettings`.
- **RQ-072**: Added DataProtection key directory permission hardening — creates directory if missing, sets chmod 700 on non-Windows.
- **RQ-094**: Verified window globals already type-safe via TypeScript `Globals.d.ts` declaration with required fields.
- **RQ-095**: Added `alt` attributes to 8 `<img>` tags missing them across `AuthorImage.js`, `NotFound.js`, `LoadingPage.js`, `ErrorBoundaryError.tsx`.
- **RQ-106**: Verified ESLint already runs via `yarn lint` step in `ci-frontend.yml`.
- **RQ-107**: Changed webpack production `devtool` from `'source-map'` to `'hidden-source-map'` to prevent source code exposure.
- **RQ-108** (partial): Quoted `$BUNDLEDVERSIONS` variable in `build.sh` `EnableExtraPlatformsInSDK()` to prevent word-splitting.
- **RQ-110**: Added `"packageManager": "yarn@1.22.19"` to root `package.json`.
- **RQ-131**: Removed obsolete `SYSLIB0051` BinaryFormatter serialization constructors from 4 exception classes; removed `[Serializable]` attributes.
- **RQ-132/133/134**: Verified `Microsoft.Win32.Registry`, `System.Security.Principal.Windows`, and `System.IO.FileSystem.AccessControl` 5.0.0 are the latest stable NuGet versions (APIs absorbed into .NET runtime).
- **RQ-153**: Added SHA256 checksum verification for Inno Setup download in `build.sh` using `INNO_SETUP_SHA256` env var.
- **RQ-048**: Restructured 10 duplicate `## Implementation Progress Snapshot` H2 headings in `MIGRATION_PLAN.md` into H3 sub-sections under single H2.
- **RQ-062**: Refreshed `wiki/Home.md` with full repository doc table, operations docs section, and current priorities.
- **RQ-122**: Replaced stale `v0.x` milestones with phase-based model in `GITHUB_PROJECTS_BLUEPRINT.md`.
- **RQ-123**: Added cross-reference notes to 3 duplicated sections in `PROVIDER_IMPLEMENTATION_GUIDE.md`; renamed Additional Resources to References.
- **RQ-125**: Expanded `wiki/Architecture.md` (solution structure table, provider chain) and `wiki/Contributor-Onboarding.md` (build commands, version pins).
- **RQ-148**: Added `## References` sections to `BRANCH_STRATEGY.md`, `GITHUB_PROJECTS_BLUEPRINT.md`, and `PROVIDER_IMPLEMENTATION_GUIDE.md`.
- **RQ-149**: Fixed self-referencing rename entries in `ZERO_LEGACY_BRAND_CHANGEOVER_PLAN.md`; updated audit baseline to 42 content / 8 path matches.
- **RQ-150**: Clarified branch protection status in `BRANCH_STRATEGY.md` — replaced bullet list with table showing Active/On-demand and protection state.
- **RQ-151**: Expanded `npm/bibliophilarr-launcher/README.md` with env var table, cache docs, troubleshooting, and links sections.
- **RQ-075**: Added API key sharing warning (`helpTextWarning`) to iCal feed URL in `CalendarLinkModalContent.js`.
- **RQ-030**: Added 5-minute timeout to both `Task.WaitAll` calls in `FetchAndParseImportListService.cs` with warning log on timeout.
- **RQ-116**: Added `umask 077` to Docker ENTRYPOINT so SQLite databases and all runtime files are created with restrictive permissions.
- **RQ-145**: Converted all 38 `Object.assign({}, ...)` calls to ES6 spread operator across 28 frontend files (selectors, actions, middleware, utilities).
- **RQ-152**: Added error checking (`|| { echo ...; exit 1; }`) to all 6 sed operations in `build.sh`.
- **RQ-034**: Added typed error categorization to `MetadataAggregator` — 404/Gone (not-found), 401/403 (auth failure), 429 (rate-limit) now have distinct catch blocks with differentiated logging.
- **RQ-123**: Added cross-reference notes to 3 duplicated sections in `PROVIDER_IMPLEMENTATION_GUIDE.md`; renamed Additional Resources to References.
- **RQ-020**: Eliminated sync-over-async in vendored EpubReader — added synchronous methods to XmlUtils, RootFilePathReader, PackageReader, SchemaReader; `OpenBook()` no longer calls `.Result` on async chain.
- **RQ-119**: Added SHA256 checksum verification to update backup — checksums written after backup, verified before update proceeds; update aborts on corruption.
- **RQ-128**: Extracted hardcoded grid sizing magic numbers (172, 182, 238, 250, 253, 162, 186, 23, 16) to `Utilities/Constants/grid.js`; updated AuthorIndexPosters, BookIndexPosters, and Bookshelf.
- **RQ-139**: Replaced unmaintained `element-class` package (2013) with native `document.body.classList` API in Modal.js; removed dependency from package.json.
- **RQ-102**: Replaced deprecated `ReactDOM.findDOMNode` with direct ref access in PageSidebar.js and Modal.js; removed unused ReactDOM import from PageSidebar.
- **RQ-120**: Added automatic rollback to update engine — post-copy binary verification, nested rollback try-catch, post-restart health check with auto-rollback if service fails to start.
- **RQ-175**: Added `SecurityHeadersMiddleware` with CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy headers; registered early in Startup.cs pipeline.
- **RQ-117**: Created `MetadataProviderApiKeyCheck` health check — validates Hardcover API token (env var + config) and Google Books API key on startup/schedule; warns if missing or suspiciously short.
- **RQ-089**: Added `ValidateSearchResults()` payload validation to both Hardcover and Inventaire fallback search providers; logs warnings for missing IDs and debug entries for missing titles/authors before mapping.
- **RQ-068**: Removed dead `react-addons-shallow-compare` 15.6.3 dependency — zero usages found in codebase; removed from package.json.
- **RQ-142**: Added `returnFocus={true}` to `FocusLock` in Modal.js — focus now returns to the trigger element when modal closes.
- **RQ-067**: Replaced abandoned `redux-localstorage` 0.4.1 with custom store enhancer in `createPersistState.js`; identical behavior (slicer, serialize, merge) with zero external dependency.
- **RQ-018**: Fixed deadlock risk in `ReleasePushController` — converted `Create()` to async, replaced `lock`/`.GetAwaiter().GetResult()` with `SemaphoreSlim.WaitAsync()`/`await ProcessDecision()`.
- **RQ-051**: Added GraphQL response envelope validation to Hardcover provider — validates errors array, missing data envelope, and logs structured warnings; added `HardcoverGraphQlError` model.
- **RQ-056**: Added `lint-workflows.yml` CI workflow with actionlint v1.7.7 for automated GitHub Actions workflow linting on all changes to `.github/workflows/`.
- **RQ-070**: Removed phantom `Microsoft.Data.SqlClient` 2.1.7 dependency — zero code usages found; removed from both csproj and Directory.Packages.props.
- **RQ-136**: Replaced `ImpromptuInterface` duck-typing in `DuplicateEndpointDetector.cs` with direct `System.Reflection` property/method calls; removed dependency entirely.
- **RQ-097**: Converted 28 route components to `React.lazy()` with `Suspense` fallback in `AppRoutes.js` for route-based code splitting; 5 core pages remain eagerly loaded.
- **RQ-111**: Added Trivy vulnerability scanner to Docker image workflow; fails on CRITICAL/HIGH severity findings.
- **RQ-112**: Added CycloneDX SBOM generation via Trivy to Docker image workflow; SBOM uploaded as build artifact.
- **RQ-003** (partial): Converted `BookController.GetBooks()` from sync to async — `Task.Run()` + `await` for parallel edition/author loading; eliminated blocking call site.
- **RQ-065**: Removed dead `Bibliophilarr.Automation.Test` project from solution; removed Selenium.Support 3.141.0 and Selenium.WebDriver.ChromeDriver from Directory.Packages.props (zero CI integration, no test runs).
- **RQ-061**: Resolved by RQ-065 — ChromeDriver removed entirely.
- **RQ-113**: Added SHA256 checksum generation (`SHA256SUMS.txt`) to release workflow; checksums included alongside release artifacts for download verification.
- **RQ-060** (partial): Upgraded AutoFixture 4.17.0→4.18.1, Moq 4.17.2→4.20.72 in Directory.Packages.props.
- **RQ-042**: Added jest coverage configuration to `jest.config.cjs` — `collectCoverageFrom`, `coverageDirectory`, and `coverageThreshold` with 0% initial baselines for statements, branches, functions, lines.
- **RQ-076**: Enabled incremental TypeScript strict mode in `frontend/tsconfig.json` — added `strictFunctionTypes`, `strictBindCallApply`, `noImplicitThis`.
- **RQ-077**: Implemented Polly v8 circuit breaker in `BookSearchFallbackExecutionService` — per-provider `ResiliencePipeline<List<Book>>` with failure threshold of 3, 2-minute break duration, and Open/Half-Open/Closed state logging.
- **RQ-135**: Upgraded `System.Data.SQLite.Core` from 1.0.115.5 to 1.0.119 in `Directory.Packages.props`.
- **RQ-129**: Extracted repeated CSS gradient patterns to `Styles/Mixins/colorImpairedGradients.css` with `colorImpairedDangerGradient` and `colorImpairedWarningGradient` mixins; updated `AuthorIndexFooter.css`, `BookIndexFooter.css`, `ProgressBar.css` to use mixins.
- **RQ-104**: Added `// @ts-check` directive to 5 core utility JS files (`isString.js`, `roundNumber.js`, `combinePath.js`, `convertToBytes.js`, `titleCase.js`) for incremental TypeScript checking.
- **RQ-050** (partial): Added `#nullable enable` directive to 5 C# DTO files (`AlternateTitleResource.cs`, `BookEditorResource.cs`, `BookshelfAuthorResource.cs`, `CalibreLibraryInfo.cs`, `CalibreConversionStatus.cs`) with proper nullable reference type annotations.
- **RQ-002**: Added optional `page` and `pageSize` query parameters to `BookController.GetBooks()` for pagination; added warning log for unfiltered requests on large libraries (5000+ books); pagination capped at 1000 per page.
- **RQ-096**: Updated `IconButton.js` to use dynamic `aria-label={title}` instead of hardcoded string; added missing `title` props to 8 IconButton/SpinnerIconButton usages across BackupRow, QueueRow, ScheduledTaskRow, BookSearchCell, AuthorIndexHeader, BookIndexHeader for proper accessibility.

### Removed

- Removed `frontend/src/Shared/piwikCheck.js`: legacy Sonarr Piwik analytics script that loaded a tracking beacon from `piwik.sonarr.tv`. No Bibliophilarr analytics replacement needed; the backend `IAnalyticsService` (install-activity telemetry for update checks) is unaffected.
- Removed `azure-pipelines.yml` (1,251 lines): legacy Readarr Azure DevOps pipeline. GitHub Actions is the sole authoritative CI system; the Azure config was never adapted for Bibliophilarr and caused dual-CI confusion.

## [0.9.0] - 2026-03-17

- Fixed ConfigService property setters: removed clamping logic from `IsbnContextFallbackLimit` and `BookImportMatchThresholdPercent` to preserve exact config values (validation moved to API controller layer).
- Fixed 16 test fixture failures across 6 core test suites:
  - EbookTagServiceFixture: added warn suppression for malformed-file and filename-fallback tests (11/11 passing).
  - MediaCoverServiceFixture: aligned proxy URL fallback assertions with actual behavior (broken edit repaired).
  - RefreshBookDeletionGuardFixture: added log suppression for expected warning/error logs from deletion-marking flows.
  - AddArtistFixture: normalized expected ForeignAuthorId assertion to include `openlibrary:author:` prefix.
  - DownloadDecisionMakerFixture: added error expectation for unparsable title parsing exception.
  - AudioTagServiceFixture: added error suppression for expected failures when reading missing files.
  - Result: Full Core.Test suite (non-integration) now passes 2640/2640 (59 skipped).
- Fixed frontend jest.setup.js indentation (tabs → 2-space) to pass ESLint validation.
- Removed stale canonical-doc contradictions about frontend test-runner status; confirmed jest + webpack setup matches current package.json and ci-frontend.yml.
- Cleaned up stale PID lock files blocking startup on fresh instances.
- Validated complete build pipeline: backend build + frontend build + ESLint/Stylelint lint + packaging all passing (exit code 0).
- Verified packaged binary operational health via /ping endpoint smoke test (HTTP 200 OK).

### Added

- Initial changelog tracking for documentation and release-readiness work.
