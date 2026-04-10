const wikiPages = [
  {
    id: 'getting-started',
    title: 'Getting Started',
    icon: 'BOOK',
    description: 'First-time setup, authentication, adding authors, and initial configuration.',
    content: [
      {
        heading: 'Welcome to Bibliophilarr',
        paragraphs: [
          'Bibliophilarr is a book library management tool for ebook and audiobook enthusiasts. It monitors your favorite authors, automatically downloads new releases, and organizes your library. It is a community-driven fork of the retired Readarr project, focused on sustainable FOSS metadata providers and native dual-format (ebook + audiobook) tracking.',
          'This guide walks you through initial setup after your first launch.'
        ]
      },
      {
        heading: 'Authentication',
        paragraphs: [
          'Authentication is required. On first launch you will be prompted to configure authentication before the UI is accessible.',
          'Supported authentication methods:'
        ],
        list: [
          'Forms (Login Page) — A standard username/password login page. This is the recommended method for most users.',
          'Basic (Browser Popup) — HTTP Basic authentication via browser-native dialog. Useful for API-only access or simple setups.',
          'External — Delegates authentication to an external reverse proxy (e.g. Authelia, Authentik). Only use this if you have a properly configured authentication proxy in front of Bibliophilarr.'
        ],
        note: 'The API key (found in Settings → General → Security) authenticates external API consumers such as Prowlarr, download clients, and custom scripts. Keep this key private.'
      },
      {
        heading: 'Initial Configuration',
        paragraphs: [
          'After first launch, complete these steps in order:'
        ],
        list: [
          'Set a root folder — Go to Settings → Media Management → Root Folders and add the top-level directory for your book library. Each author will get a subfolder within this root. You can have multiple root folders (e.g. one for ebooks, one for audiobooks, or different drives).',
          'Configure Media Management — In Settings → Media Management, set your book naming format, import behavior (hardlinks vs copy), and whether to delete empty folders after moves.',
          'Add at least one indexer — Go to Settings → Indexers and configure your preferred Usenet or Torrent indexer. If you use Prowlarr, it can sync indexers automatically.',
          'Add a download client — Go to Settings → Download Clients and configure qBittorrent, SABnzbd, Transmission, or your preferred client. Ensure the completed download folder is accessible to Bibliophilarr.',
          'Set up quality profiles — Go to Settings → Profiles to define which formats are acceptable and preferred. Create separate profiles for ebooks (EPUB, AZW3, MOBI, PDF) and audiobooks (M4B, FLAC, MP3) if using Dual Format Tracking.',
          'Configure metadata profiles — Metadata profiles control which book editions and types appear in your library. This filters out unwanted foreign editions or non-book entries.'
        ]
      },
      {
        heading: 'Adding Your First Author',
        paragraphs: [
          'Navigate to Library → Add New, then search for an author name or book title. You can also search by GoodReads ID, ISBN, or ASIN using the format shown in the search box.',
          'Select the correct match from the metadata results. A configuration dialog will appear with these options:'
        ],
        list: [
          'Root Folder — Where this author\'s books will be stored on disk.',
          'Monitor — Choose which books to track: All Books, Future Books Only, Missing Books, Existing Books, First Book, Latest Book, or None.',
          'Quality Profile — Which quality criteria to apply when grabbing releases for this author.',
          'Metadata Profile — Controls which book editions appear (filters foreign editions, box sets, etc.).',
          'Tags — Optional tags for organizing authors into groups (useful for applying indexer restrictions or notification rules).',
          'Search for Missing Books — Check this to immediately trigger a search across all indexers for this author\'s monitored missing books. If unchecked, only newly released books will be grabbed via RSS.'
        ],
        note: 'Adding many authors at once can be resource-intensive. Add a few at a time and allow the metadata refresh to complete before adding more.'
      },
      {
        heading: 'How Downloads Work',
        paragraphs: [
          'Bibliophilarr uses two mechanisms to find and download books:'
        ],
        list: [
          'RSS Sync — Runs periodically (default: every 15 minutes). Fetches the latest releases from your indexers and matches them against your monitored library. Only grabs releases for known authors and monitored books. Does NOT actively search for old or backlog releases.',
          'Search (Manual/Automatic) — Actively queries all enabled indexers for specific books. Triggered by: clicking the search icon on a book, using mass search on the Wanted pages, or enabling "Search for Missing Books" when adding an author.'
        ]
      },
      {
        heading: 'How Download Decisions Work',
        paragraphs: [
          'When multiple releases are found for the same book, Bibliophilarr ranks them using this priority order:'
        ],
        list: [
          'Quality — Higher quality as defined in your profile is preferred.',
          'Custom Format Score — Sum of all matching custom format scores.',
          'Protocol — Usenet or Torrent preference (configurable in Delay Profiles).',
          'Indexer Priority — Lower priority number = preferred (configurable per indexer).',
          'Seeds/Peers — For torrents, more seeders are preferred.',
          'Book Count — Fewer books in a release is preferred (avoids box sets when you want a single book).',
          'Age — For Usenet, newer releases are preferred.',
          'Size — Smaller file size is preferred when all else is equal.'
        ],
        note: 'There is no scheduled automatic search for old/missing books. This is by design in the *arr ecosystem to avoid excessive indexer load. Use "Search for Missing Books" on add, or manual/mass search from the Wanted pages.'
      }
    ]
  },
  {
    id: 'library',
    title: 'Library Management',
    icon: 'AUTHOR_CONTINUING',
    description: 'Managing authors, books, editions, bookshelf, and unmapped files.',
    content: [
      {
        heading: 'Library Overview',
        paragraphs: [
          'The Library section contains your Authors, Books, Bookshelf, and Add New views. Each provides different ways to browse, manage, and expand your collection.'
        ]
      },
      {
        heading: 'Authors',
        paragraphs: [
          'The Authors page shows all authors in your library with multiple view options:'
        ],
        list: [
          'Table — A tabular list view with sortable columns.',
          'Posters — Visual poster grid similar to other media managers.',
          'Overview — Detailed view showing author information alongside their poster.'
        ]
      },
      {
        heading: 'Author Toolbar',
        paragraphs: [
          'The Authors toolbar provides bulk operations:'
        ],
        list: [
          'Update All — Refresh metadata for all authors, update posters, rescan folders, and rescan book files.',
          'Refresh & Scan — Refresh the currently selected author\'s metadata and rescan their folder.',
          'RSS Sync — Manually trigger an RSS feed check across all indexers.',
          'Author Editor — Toggle into mass-edit mode for bulk changes to monitoring, quality profiles, root folders, or tags.',
          'Sort — Sort by name, date added, quality profile, or other fields.',
          'Filter — Filter by monitored status, quality profile, root folder, tags, and more.'
        ]
      },
      {
        heading: 'Author Detail Page',
        paragraphs: [
          'Click an author to see their complete book list. From the detail page you can:'
        ],
        list: [
          'Toggle monitoring for individual books or the entire author.',
          'Search for all missing books at once with the search button.',
          'Edit the author\'s metadata, quality profile, root folder, and tags.',
          'View the author\'s history of downloads, imports, renames, and retags.',
          'Manage file organization and manually import or rename existing files.',
          'When Dual Format Tracking is enabled, see format badges (E/A) on each book showing which formats have files.'
        ]
      },
      {
        heading: 'Books',
        paragraphs: [
          'The Books index provides a flat view of all books across all authors. This is useful for quick filtering and bulk operations.',
          'Each book shows monitoring status, download status, quality, and format type (ebook or audiobook). The toolbar includes:'
        ],
        list: [
          'Search All / Search Filtered / Search Selected — Search indexers for matching books.',
          'Book Editor — Toggle into mass-edit mode for bulk monitoring changes.',
          'The same View (Table/Posters/Overview), Sort, and Filter options as the Authors page.'
        ]
      },
      {
        heading: 'Bookshelf',
        paragraphs: [
          'The Bookshelf is a compact, author-grouped view for quickly toggling monitoring on individual books. Each author is shown with all their books as toggleable rows — ideal for rapidly setting up which books to track without navigating into each author\'s detail page.'
        ]
      },
      {
        heading: 'Add New',
        paragraphs: [
          'Search for authors or individual books to add to your library. You can search by:'
        ],
        list: [
          'Author name or book title — Free text search against metadata providers.',
          'GoodReads Author ID — Use the format "author:12345".',
          'ISBN — Enter a 10 or 13-digit ISBN.',
          'ASIN — Enter an Amazon Standard Identification Number.'
        ],
        note: 'When adding an individual book and selecting "None" for the metadata profile, only that specific book will appear under the author. Choose an appropriate metadata profile if you want the author\'s full catalog.'
      },
      {
        heading: 'Unmapped Files',
        paragraphs: [
          'Files found in your root folders that Bibliophilarr couldn\'t automatically match to a known book appear here. These are files that exist on disk in one of your root folders but aren\'t linked to any book entry in the database.',
          'For each unmapped file you can:'
        ],
        list: [
          'Click the info icon (i) to see file details including path, size, and detected quality.',
          'Click the person icon to manually map the file to the correct author and book.',
          'Click the trash icon to delete the file.',
          'Use "Add Missing" at the top to attempt to automatically add all unmapped files to Bibliophilarr — note this can be time-intensive for large collections.'
        ]
      }
    ]
  },
  {
    id: 'dual-format',
    title: 'Dual Format Tracking',
    icon: 'INTERACTIVE',
    description: 'Monitoring both ebook and audiobook formats for the same title.',
    content: [
      {
        heading: 'What is Dual Format Tracking?',
        paragraphs: [
          'Dual Format Tracking is a Bibliophilarr-exclusive feature that allows you to manage both ebook and audiobook versions of the same book simultaneously within a single instance. In the original Readarr, tracking both formats required running two separate instances. Bibliophilarr solves this natively.',
          'This feature is enabled by default (since v1.1.0-dev.27).'
        ]
      },
      {
        heading: 'How It Works',
        paragraphs: [
          'With Dual Format Tracking enabled, each book has a "Format Statuses" array — one entry per tracked format (ebook and/or audiobook). Each entry independently tracks:'
        ],
        list: [
          'Format type — Ebook or Audiobook.',
          'Monitored — Whether this format is actively sought via RSS and search.',
          'Has File — Whether a file of this format exists on disk.',
          'Quality Profile — The quality criteria for this format (e.g., EPUB/AZW3 for ebook, M4B/FLAC for audiobook).'
        ]
      },
      {
        heading: 'Format-Aware Search and Grabbing',
        paragraphs: [
          'When you search for a book with both formats monitored, Bibliophilarr will grab one release per format. For example, it can send an EPUB to your download client AND an M4B for the same book in the same search operation.',
          'The download decision engine uses format-specific quality profiles to evaluate each release independently. An ebook release is compared against the ebook quality profile, and an audiobook release against the audiobook quality profile.'
        ]
      },
      {
        heading: 'Format Badges',
        paragraphs: [
          'Throughout the UI — on Library, Wanted, Activity, and History pages — format status is shown as colored badges:'
        ],
        list: [
          'E (green) — Ebook with file present.',
          'E (red) — Ebook monitored but missing.',
          'A (green) — Audiobook with file present.',
          'A (red) — Audiobook monitored but missing.',
          'Both badges appear side by side when both formats are monitored.'
        ]
      },
      {
        heading: 'Disabling Dual Format Tracking',
        paragraphs: [
          'If you only need one format, disable this feature in Settings → Media Management → EnableDualFormatTracking. When disabled, Bibliophilarr behaves like standard Readarr — tracking a single format per book based on the author\'s assigned quality profile.'
        ]
      }
    ]
  },
  {
    id: 'quality-profiles',
    title: 'Quality Profiles',
    icon: 'PROFILE',
    description: 'Quality profiles, metadata profiles, delay profiles, and release profiles.',
    content: [
      {
        heading: 'Understanding Quality Profiles',
        paragraphs: [
          'Quality profiles define which file formats are acceptable, which are preferred, and the minimum acceptable quality (cutoff). You assign a quality profile to each author, or per-format when using Dual Format Tracking.',
          'Each quality in a profile can be toggled on (allowed) or off (rejected). The order of qualities from top to bottom defines their ranking — drag qualities to reorder them. Higher-ranked qualities are preferred when multiple releases are available.'
        ]
      },
      {
        heading: 'Ebook Qualities',
        paragraphs: [
          'Quality IDs 0–4 represent ebook formats:'
        ],
        list: [
          'EPUB — The most common open ebook format. Reflowable text, widely supported by readers and apps. Recommended as the default ebook format.',
          'AZW3 — Amazon\'s improved Kindle format (KF8). Better formatting than MOBI, required for newer Kindle features.',
          'MOBI — Legacy Amazon Kindle format. Being phased out in favor of AZW3. Still useful for older Kindle devices.',
          'PDF — Portable Document Format. Fixed layout, not ideal for ereaders but necessary for some technical or illustrated content.',
          'Unknown — Unidentified ebook format. Useful as a catch-all if you want to grab any ebook release.'
        ]
      },
      {
        heading: 'Audiobook Qualities',
        paragraphs: [
          'Quality IDs 10–13 represent audiobook formats:'
        ],
        list: [
          'M4B — The standard audiobook format (AAC in MP4 container). Supports chapter markers and bookmarks. Recommended as the default audiobook format.',
          'FLAC — Lossless audio format. Perfect quality but large file sizes. Best for archival or high-end audio setups.',
          'MP3 — Universal audio format. Widely compatible but no native chapter support. Good for compatibility with older devices.',
          'Unknown Audio — Unidentified audiobook format.'
        ]
      },
      {
        heading: 'Cutoff',
        paragraphs: [
          'The cutoff setting defines the minimum quality you\'re satisfied with. Books with files below the cutoff appear on Wanted → Cutoff Unmet and will be upgraded when better releases are found.',
          'Example: If your ebook profile allows Unknown, PDF, MOBI, EPUB, and AZW3 with cutoff set to EPUB — a PDF file will show as "cutoff unmet" and Bibliophilarr will look for EPUB or AZW3 upgrades. Once EPUB is obtained, it won\'t look for AZW3 (since EPUB meets the cutoff).',
          'Set the cutoff to the highest quality if you always want Bibliophilarr to upgrade to the best available format.'
        ]
      },
      {
        heading: 'Quality Groups',
        paragraphs: [
          'You can group multiple qualities together so they are treated as equivalent. For example, grouping EPUB and AZW3 means Bibliophilarr won\'t prefer one over the other and won\'t upgrade between them.',
          'To create a group, drag one quality onto another in the profile editor.'
        ]
      },
      {
        heading: 'Format Priority Warning',
        paragraphs: [
          'When Bibliophilarr imports files, it imports in order of your quality priorities — the ranking applies regardless of whether a quality is checked (allowed) or not. If a download contains both EPUB and AZW3 files, the one ranked higher in your profile will be imported. Make sure your preferred formats are ranked highest to avoid surprises.'
        ],
        note: 'If you find unwanted formats being imported, check the quality ranking order in your profile. Drag your preferred format to the top of the list.'
      },
      {
        heading: 'Metadata Profiles',
        paragraphs: [
          'Metadata profiles control which book editions and types appear in your library from the metadata source. They filter out unwanted entries such as foreign-language editions, box sets, or non-book products.',
          'When you add an author, you choose a metadata profile. Selecting "None" means no filtering — all editions from the metadata source will appear. This can include unwanted foreign editions.'
        ],
        note: 'If you see unexpected foreign editions, create a metadata profile that filters by your preferred language and edition types.'
      },
      {
        heading: 'Delay Profiles',
        paragraphs: [
          'Delay profiles allow you to set a waiting period after a release is grabbed before it\'s imported, giving time for preferred protocols or higher-quality releases to become available.',
          'You can configure separate delays for Usenet and Torrent protocols, and set a preferred protocol for each tag group. For example, prefer Usenet but wait 60 minutes for a torrent to appear.'
        ]
      },
      {
        heading: 'Release Profiles',
        paragraphs: [
          'Release profiles let you set preferred and required/rejected words that are matched against release names. Preferred words add a score to matching releases (positive = prefer, negative = penalize). Required words must be present, and rejected words cause the release to be skipped.',
          'Common use cases:'
        ],
        list: [
          'Prefer specific narrators for audiobooks by adding their name as a preferred word.',
          'Reject low-quality rips by rejecting keywords like "abridged" for unabridged preference.',
          'Prefer specific release groups known for high quality.',
          'Restrict by indexer — you can limit a release profile to specific indexers.'
        ],
        note: 'Release profile preferred word scores are additive. A release matching multiple preferred words gets the sum of all scores. The release with the highest total score (after quality/protocol comparison) is grabbed.'
      }
    ]
  },
  {
    id: 'indexers',
    title: 'Indexers',
    icon: 'SEARCH',
    description: 'Configuring indexers, RSS sync, Prowlarr integration, and search behavior.',
    content: [
      {
        heading: 'What Are Indexers?',
        paragraphs: [
          'Indexers are the services Bibliophilarr queries to find book releases. They provide a searchable catalog of available downloads, similar to a search engine for Usenet and Torrent content.',
          'Two types of indexers are supported:'
        ],
        list: [
          'Newznab — The standard API for Usenet indexers (e.g. NZBGeek, NZBPlanet, DrunkenSlug). Returns NZB files for download via your Usenet client.',
          'Torznab — The standard API for Torrent indexers/trackers (e.g. IPTorrents, MyAnonamouse, Nyaa). Returns torrent files or magnet links for your torrent client.'
        ]
      },
      {
        heading: 'Adding an Indexer',
        paragraphs: [
          'Go to Settings → Indexers → Add (+). Select your indexer type (Newznab or Torznab) and provide the required information:'
        ],
        list: [
          'Name — A friendly label for this indexer.',
          'URL — The indexer API endpoint URL.',
          'API Key — Your personal API key from the indexer (found in your indexer account settings).',
          'Categories — Which content categories to query. Ensure you select the correct book/audiobook categories for your indexer.',
          'Enable RSS — Include this indexer in periodic RSS sync checks.',
          'Enable Automatic Search — Allow this indexer to be queried when Bibliophilarr automatically searches for missing/cutoff-unmet books.',
          'Enable Interactive Search — Include this indexer in manual interactive searches triggered from the UI.',
          'Indexer Priority — Lower numbers indicate higher priority. When the same release is available from multiple indexers, the one with the lower priority number is preferred.'
        ],
        note: 'After adding an indexer, use the Test button to verify connectivity, authentication, and that results are returned. If the test fails, check your URL, API key, and network connectivity.'
      },
      {
        heading: 'RSS Sync',
        paragraphs: [
          'RSS sync runs on a schedule (configurable in Settings → Indexers → RSS Sync Interval, default: every 15 minutes). It fetches the most recent releases from each indexer\'s RSS feed and tries to match them to monitored books in your library.'
        ],
        list: [
          'Only matches against authors and books already in your library — unknown author releases are silently skipped.',
          'Only processes new releases since the last sync — it does not search historical/backlog releases.',
          'Respects your quality profile, cutoff, and custom format settings.',
          'Applies to all indexers with "Enable RSS" checked.'
        ],
        note: 'If you see "Reports found: X, Reports grabbed: 0" in logs after an RSS sync, it means none of the new indexer releases matched your library. This is completely normal — most RSS items will be for authors you haven\'t added.'
      },
      {
        heading: 'Prowlarr Integration',
        paragraphs: [
          'Prowlarr is a centralized indexer management tool that can sync indexer configurations to all *arr applications. Instead of configuring each indexer in Bibliophilarr individually, you configure them once in Prowlarr.',
          'To set up Prowlarr integration:'
        ],
        list: [
          'In Prowlarr, go to Settings → Apps → Add and select Bibliophilarr/Readarr.',
          'Enter your Bibliophilarr API URL and API key.',
          'Prowlarr will automatically push indexer configurations to Bibliophilarr.',
          'Prowlarr also provides unified search history across all your *arr applications.'
        ]
      },
      {
        heading: 'Indexer Categories',
        paragraphs: [
          'Incorrect categories are one of the most common causes of missing search results. Each indexer uses numbered categories to organize content. Common book-related categories include:'
        ],
        list: [
          '7000 — Books (general)',
          '7010 — Books/Mags',
          '7020 — Books/EBook',
          '7030 — Books/Comics',
          '7040 — Books/Technical',
          '7050 — Books/Other',
          '3000/3010/3030 — Audio (may include audiobooks on some trackers)'
        ],
        note: 'Each indexer may use different or custom categories. Check your indexer\'s documentation or category list to ensure you\'ve selected the correct ones for ebooks and audiobooks.'
      },
      {
        heading: 'Avoid Jackett /all Endpoint',
        paragraphs: [
          'If you use Jackett, do not use the /all aggregate endpoint. While convenient, it causes problems:'
        ],
        list: [
          'You lose control over indexer-specific settings (categories, search modes).',
          'Mixing search modes can produce low-quality or incorrect results.',
          'Slow indexers will slow down all results.',
          'Total results are limited to 1000 across all indexers combined.',
          'If one tracker in /all returns an error, the entire endpoint may be disabled.'
        ],
        paragraphs2: [
          'Add each tracker individually in Bibliophilarr, or use Prowlarr for centralized management.'
        ]
      }
    ]
  },
  {
    id: 'download-clients',
    title: 'Download Clients',
    icon: 'DOWNLOADING',
    description: 'Usenet and torrent clients, remote path mappings, completed download handling, and Docker paths.',
    content: [
      {
        heading: 'Supported Download Clients',
        paragraphs: [
          'Bibliophilarr supports both Usenet and Torrent download clients:'
        ],
        list: [
          'Usenet — SABnzbd, NZBGet, NZBVortex, Download Station.',
          'Torrent — qBittorrent, Transmission, Deluge, rTorrent/ruTorrent, uTorrent, Vuze, Aria2, Flood, Download Station.'
        ]
      },
      {
        heading: 'Adding a Download Client',
        paragraphs: [
          'Go to Settings → Download Clients → Add (+). Select your client type and provide connection details:'
        ],
        list: [
          'Host — The hostname or IP address of your download client (usually 127.0.0.1 or localhost if on the same machine).',
          'Port — The web UI port of your client (e.g. 8080 for qBittorrent, 8085 for SABnzbd).',
          'Username/Password — Credentials for your client\'s web UI.',
          'Category — A dedicated category for Bibliophilarr downloads (recommended: "bibliophilarr" or "readarr"). This keeps Bibliophilarr downloads separate from other downloads.',
          'Priority — Default priority for new downloads.',
          'Remove Completed — Whether to remove downloads from the client after successful import.'
        ],
        note: 'Ensure the download client\'s web UI is enabled. Bibliophilarr communicates via the client\'s API through its web interface.'
      },
      {
        heading: 'How Usenet Downloads Work',
        paragraphs: [
          'The Usenet download flow:'
        ],
        list: [
          '1. Bibliophilarr sends an NZB file to your Usenet client (SABnzbd/NZBGet).',
          '2. The client downloads the content from Usenet servers using your provider.',
          '3. The client\'s post-processing extracts and verifies the files.',
          '4. Bibliophilarr detects the completed download via client API polling.',
          '5. Bibliophilarr imports the file to the correct author/book folder, renames it per your settings, and optionally removes the download from the client.'
        ]
      },
      {
        heading: 'How Torrent Downloads Work',
        paragraphs: [
          'The Torrent download flow:'
        ],
        list: [
          '1. Bibliophilarr sends a .torrent file or magnet link to your torrent client.',
          '2. The client downloads the content from peers/seeds.',
          '3. Upon completion, Bibliophilarr detects the completed download.',
          '4. Bibliophilarr hardlinks (preferred) or copies the file to the correct author/book folder.',
          '5. The original torrent continues seeding — hardlinks allow this without duplicating disk space.',
          '6. Once seeding requirements are met, the torrent can be removed from the client.'
        ],
        note: 'Hardlinks require the download and library folders to be on the same filesystem. If they\'re on different filesystems or network mounts, Bibliophilarr will fall back to copying.'
      },
      {
        heading: 'Completed Download Handling',
        paragraphs: [
          'Bibliophilarr automatically monitors your download clients for completed downloads. When a download finishes:'
        ],
        list: [
          'The file is matched to the book that triggered the grab.',
          'A match threshold check verifies the download matches the expected book (prevents importing wrong content).',
          'The file is imported (hardlink, copy, or move) to the correct author folder.',
          'The file is renamed according to your naming settings in Settings → Media Management.',
          'Book file tags are updated if retagging is enabled.',
          'If Remove Completed is enabled, the download is removed from the client.',
          'Notifications are sent for configured connection events (On Import, On Upgrade).'
        ]
      },
      {
        heading: 'Failed Download Handling',
        paragraphs: [
          'When a download fails (incomplete, corrupted, or times out), Bibliophilarr can automatically:'
        ],
        list: [
          'Detect the failure via the download client API.',
          'Add the failed release to the blocklist (preventing re-download of the same release).',
          'Search for an alternative release from other indexers.',
          'Send configured notifications.'
        ]
      },
      {
        heading: 'Remote Path Mappings',
        paragraphs: [
          'Remote Path Mappings are needed when your download client reports a different file path than what Bibliophilarr can access. This commonly occurs with:'
        ],
        list: [
          'Docker containers where the download client and Bibliophilarr have different volume mounts.',
          'Remote download clients running on a different machine.',
          'Mixed OS setups (Windows download client, Linux Bibliophilarr or vice versa).'
        ]
      },
      {
        heading: 'Configuring Remote Path Mappings',
        paragraphs: [
          'Go to Settings → Download Clients → Remote Path Mappings and add a mapping:'
        ],
        list: [
          'Host — The hostname of the download client this mapping applies to.',
          'Remote Path — The path as reported by the download client (e.g. /downloads/books/).',
          'Local Path — The corresponding path as seen by Bibliophilarr (e.g. /data/downloads/books/).'
        ],
        note: 'Remote Path Mappings are a simple search/replace — when the download client reports a path starting with the Remote Path, it\'s replaced with the Local Path. If both Bibliophilarr and your download client are Docker containers with consistent volume mounts, you typically don\'t need remote path mappings. Fix the mounts instead.'
      },
      {
        heading: 'Docker Path Best Practices',
        paragraphs: [
          'The most common path issues in Docker setups can be avoided with consistent mount points:'
        ],
        list: [
          'Use a common parent folder like /data for both downloads and library.',
          'Download client: /mnt/user/data/downloads:/data/downloads',
          'Bibliophilarr: /mnt/user/data:/data',
          'Set the download client category folder inside /data/downloads/books/',
          'Set the Bibliophilarr root folder to /data/media/books/',
          'This allows hardlinks to work correctly (same filesystem) and paths to be consistent.'
        ],
        warning: 'Your download folder and your root/library folder MUST be separate directories. Never set your download client to download directly into your library root folder.'
      },
      {
        heading: 'Download Client Retention',
        paragraphs: [
          'Configure your download client to keep completed download history until Bibliophilarr has imported the files:'
        ],
        list: [
          'SABnzbd — Set Post Processing → Keep Jobs to at least 14 days.',
          'Torrent clients — Set to pause (not remove) completed torrents. Let Bibliophilarr handle removal via the Remove Completed setting.',
          'Do not configure your download client to auto-remove completed downloads — Bibliophilarr needs to see them in the client\'s history to import them.'
        ]
      }
    ]
  },
  {
    id: 'wanted',
    title: 'Wanted & Searching',
    icon: 'WARNING',
    description: 'Missing books, cutoff unmet, search behavior, and understanding rejections.',
    content: [
      {
        heading: 'Wanted → Missing',
        paragraphs: [
          'The Missing page shows all monitored books that don\'t have any downloaded files. Each entry displays the author, book title, release date, and format status badges (E/A) when Dual Format Tracking is enabled.',
          'Toolbar options:'
        ],
        list: [
          'Search All — Search all indexers for every missing book. Use with caution on large libraries to avoid hitting indexer rate limits.',
          'Search Selected — Select specific books with checkboxes and search only for those. With Dual Format Tracking, this searches for both ebook and audiobook releases.',
          'Search Filtered — Search only for books matching your current filter.',
          'Manual Search (magnifying glass icon per row) — Opens an interactive search showing all available releases from all indexers, with quality, size, age, and indexer details. You can manually choose which release to grab.'
        ]
      },
      {
        heading: 'Wanted → Cutoff Unmet',
        paragraphs: [
          'Cutoff Unmet shows books that have a file, but the file\'s quality is below the cutoff defined in the quality profile. For example, a PDF when your cutoff is EPUB.',
          'Bibliophilarr will attempt to upgrade these when better releases are found via RSS or search. The same search options (All/Selected/Filtered/Manual) are available.'
        ]
      },
      {
        heading: 'Three Ways Books Get Downloaded',
        list: [
          'RSS Sync — Automatic, periodic (default every 15 minutes). Checks each indexer\'s RSS feed for new releases. Only matches against your existing library. Does NOT search for old or backlog releases.',
          'Search on Add — When you add an author with "Search for Missing Books" checked, Bibliophilarr immediately queries all enabled indexers for that author\'s missing books.',
          'Manual/Mass Search — Click the search icon on any book, or use Search All / Search Selected / Search Filtered on the Wanted pages. This actively queries indexers for specific books.'
        ],
        warning: 'There is no scheduled automatic search for missing/backlog books. This is by design in the *arr ecosystem to avoid hammering indexers with repeated queries. Use "Search on Add" for new authors, or periodic manual/mass searches on the Wanted pages.'
      },
      {
        heading: 'Understanding Rejections',
        paragraphs: [
          'When a search or RSS sync finds releases but doesn\'t grab them, check Activity → History or the logs for rejection reasons:'
        ],
        list: [
          'Unknown Author — The release is for an author not in your library.',
          'Quality not wanted in profile — The release quality (e.g. MP3) isn\'t allowed in your quality profile.',
          'Cutoff already met — You already have a file at or above the cutoff quality; no upgrade needed.',
          'Not monitored — The book or format isn\'t currently monitored.',
          'Release is blocklisted — The release was previously blocklisted due to a failed download or manual blocklist.',
          'Wrong book — The release couldn\'t be matched to any specific book in the author\'s catalog.',
          'Book match is not close enough — The release name doesn\'t match the expected book title closely enough (configurable threshold, default 70%).',
          'Size outside acceptable range — The file size is below minimum or above maximum for the quality profile.'
        ]
      },
      {
        heading: 'Download Decision Priority',
        paragraphs: [
          'When multiple releases are available for the same book, Bibliophilarr selects the best one using this priority order (highest priority first):'
        ],
        list: [
          '1. Quality — Higher quality ranking in your profile wins.',
          '2. Custom Format Score — Higher total custom format score wins.',
          '3. Protocol preference — Usenet or Torrent, as configured in Delay Profiles.',
          '4. Indexer Priority — Lower priority number wins.',
          '5. Seed count (torrents) — More seeders is preferred.',
          '6. Book count — Releases with fewer books preferred (avoids grabbing box sets).',
          '7. Age (Usenet) — Newer NZBs preferred.',
          '8. File size — Smaller size preferred when all else is equal.'
        ]
      }
    ]
  },
  {
    id: 'activity',
    title: 'Activity & History',
    icon: 'ACTIVITY',
    description: 'Queue monitoring, history tracking, blocklist management, and troubleshooting downloads.',
    content: [
      {
        heading: 'Queue',
        paragraphs: [
          'The Queue shows all active downloads being tracked by Bibliophilarr. Each entry displays:'
        ],
        list: [
          'Status icon — Hover over it for detailed status messages, warnings, or errors.',
          'Book information — Author and book title being downloaded.',
          'Download client — Which client is handling the download.',
          'Progress — Download percentage and estimated time remaining.',
          'Quality — The detected quality/format of the release.',
          'Format — E (ebook) or A (audiobook) badge when Dual Format Tracking is enabled.',
          'Actions — Remove from queue, blocklist the release, or manually import.'
        ]
      },
      {
        heading: 'Queue Status Icons',
        paragraphs: [
          'The status icon color indicates the download state:'
        ],
        list: [
          'Blue (downloading) — Download is in progress.',
          'Purple (importing) — Download is complete, Bibliophilarr is processing the import.',
          'Green (completed) — Successfully imported.',
          'Orange (warning) — Import issue detected. Hover for details — common causes: path not found, permission denied, book match too low.',
          'Red (error) — Download or import failed. Check the warning message and logs for details.'
        ],
        note: 'Items stuck with an orange warning often indicate a remote path mapping issue or permission problem. Check that Bibliophilarr can read the download client\'s completed folder and write to the library root folder.'
      },
      {
        heading: 'Queue Options',
        paragraphs: [
          'Click Options at the top of the Queue page to toggle:'
        ],
        list: [
          'Show Unknown — Display downloads that Bibliophilarr can\'t match to a library book. These may need manual mapping/import. This is enabled by default in recent versions.'
        ]
      },
      {
        heading: 'History',
        paragraphs: [
          'History records all actions Bibliophilarr has taken, with event types:'
        ],
        list: [
          'Grabbed — A release was sent to the download client.',
          'Imported — A file was successfully imported to the library.',
          'Upgraded — An existing file was replaced with a higher-quality version.',
          'Renamed — A file was renamed per your naming settings.',
          'Retagged — Book file metadata tags were updated.',
          'Deleted — A file was removed from the library.',
          'Failed — A download failed and was (optionally) blocklisted.'
        ]
      },
      {
        heading: 'History Details',
        paragraphs: [
          'Click any history entry to see full details including: source indexer, release name, quality, size, download client, and the complete path. The Format column shows whether the event involved an ebook (E) or audiobook (A) based on the detected quality of the file.'
        ]
      },
      {
        heading: 'Blocklist',
        paragraphs: [
          'The Blocklist contains releases that have been explicitly blocked — either manually from the Queue, or automatically when a download fails and Failed Download Handling is enabled.',
          'Blocked releases will not be re-downloaded even if they appear on indexers again. You can:'
        ],
        list: [
          'Remove individual blocklist entries to allow that specific release to be grabbed again.',
          'Clear the entire blocklist.',
          'Review why a release was blocklisted by checking the history entry.'
        ]
      }
    ]
  },
  {
    id: 'media-management',
    title: 'Media Management',
    icon: 'ORGANIZE',
    description: 'Root folders, file naming, import settings, Calibre integration, and permissions.',
    content: [
      {
        heading: 'Root Folders',
        paragraphs: [
          'Root folders are the top-level directories where Bibliophilarr stores your book library. Each author gets a subfolder within the root folder.',
          'You can have multiple root folders for different purposes:'
        ],
        list: [
          'Separate drives — Place libraries on different storage volumes.',
          'Separate formats — One root for ebooks, another for audiobooks (when not using Dual Format Tracking).',
          'Different retention policies — Active vs archive libraries.'
        ],
        warning: 'Never use your download client\'s download folder as a root folder. The root folder is for your organized library. Downloads and library MUST be separate directories.'
      },
      {
        heading: 'Author Folder Format',
        paragraphs: [
          'Configure how author folders are named. Go to Settings → Media Management → Naming.',
          'The default format is {Author Name}. Available tokens include:'
        ],
        list: [
          '{Author Name} — Full author name.',
          '{Author SortName} — Author name formatted for sorting (Last, First).',
          '{Author CleanName} — Author name with special characters removed.'
        ]
      },
      {
        heading: 'Book Naming',
        paragraphs: [
          'Configure how book files are renamed during import. Available naming tokens include:'
        ],
        list: [
          '{Book Title} — The book title.',
          '{Book CleanTitle} — Book title with special characters removed.',
          '{Author Name} — The author name.',
          '{Quality Full} — Full quality name (e.g. "EPUB", "M4B 320kbps").',
          '{Quality Title} — Quality name only.',
          '{Release Group} — The release group name.',
          '{Edition Title} — The book edition name if different from default.',
          '{PartNumber} — Part number for multi-part books.',
          'Custom Format tokens — Include matched custom format names in filenames.'
        ],
        note: 'Use the naming preview in Settings → Media Management to see how your pattern will look with real data before saving.'
      },
      {
        heading: 'Import Settings',
        paragraphs: [
          'Control how Bibliophilarr handles completed downloads when importing:'
        ],
        list: [
          'Use Hardlinks instead of Copy — When the download and library are on the same filesystem, create a hardlink instead of copying. This saves disk space and allows torrent clients to continue seeding the file. Recommended for torrent users.',
          'Import Extra Files — Import additional files alongside the book file, such as cover images (.jpg, .png), metadata files (.opf), or other configured extensions.',
          'Delete Empty Folders — Automatically clean up empty author/book folders after moving or deleting files.',
          'Skip Free Space Check — Disable the disk space check before importing. Only enable if you know what you\'re doing.',
          'Minimum Free Space — The minimum amount of free disk space to maintain before importing stops.',
          'Recycle Bin — Optionally move deleted files to a recycle/trash folder instead of permanent deletion. Useful for recovery.'
        ]
      },
      {
        heading: 'File Management',
        paragraphs: [
          'Additional file management settings:'
        ],
        list: [
          'Watch Root Folders for Changes — Monitor root folders for external file changes (added, removed, renamed). Triggers rescans when changes are detected.',
          'Rescan Author Folder after Refresh — Rescan the author\'s disk folder when their metadata is refreshed.',
          'Allow Fingerprinting — Enable audio fingerprinting for better audiobook matching. Can be CPU-intensive.',
          'Change File Date — Optionally set the file\'s last-modified date to the book release date or import date.'
        ]
      },
      {
        heading: 'Calibre Integration',
        paragraphs: [
          'Bibliophilarr can integrate with Calibre Content Server for ebook management. When configured:'
        ],
        list: [
          'Books are sent to Calibre instead of directly to a root folder.',
          'Calibre manages the file organization and metadata.',
          'You must set the root folder to point to Calibre\'s library folder.',
          'Calibre Content Server must be running and accessible from Bibliophilarr.'
        ],
        note: 'When using Calibre integration, do not change the root folder path. Let Calibre manage the file organization. If Bibliophilarr can\'t access the Calibre library path, check your Docker mount consistency or see Remote Path Mappings.'
      },
      {
        heading: 'Permissions and Ownership',
        paragraphs: [
          'File permission issues are one of the most common problems. Ensure:'
        ],
        list: [
          'The user running Bibliophilarr has read/write access to all root folders.',
          'The user has read access to the download client\'s completed download folder.',
          'On Linux, check ownership with: ls -la /path/to/library',
          'For Docker, ensure the PUID/PGID environment variables match the owner of your media and download directories.',
          'On NFS mounts, ensure "nolock" is enabled.',
          'On SMB/CIFS mounts, ensure "nobrl" is enabled.'
        ],
        note: 'Windows Service users: the default "Local Service" account has limited permissions. Consider running Bibliophilarr as a tray application or assign appropriate permissions to the service account.'
      }
    ]
  },
  {
    id: 'notifications',
    title: 'Notifications',
    icon: 'BELL',
    description: 'Setting up notifications and connections for events.',
    content: [
      {
        heading: 'Connections',
        paragraphs: [
          'Bibliophilarr can notify you of events via various connection types. Go to Settings → Connect to add notification connections.',
          'Supported notification types include:'
        ],
        list: [
          'Email — Send notifications via SMTP.',
          'Discord — Post messages to a Discord channel webhook.',
          'Telegram — Send messages via Telegram bot (requires bot token and chat ID).',
          'Pushover — Push notifications to iOS/Android devices.',
          'Slack — Post to a Slack channel via webhook.',
          'Gotify — Self-hosted push notification server.',
          'Ntfy — Open-source push notifications.',
          'Apprise — Unified notification gateway supporting 80+ services.',
          'Webhook — Send raw JSON payloads to custom HTTP endpoints. Useful for integration with custom scripts or automation.',
          'Custom Script — Execute a local script on specific events.',
          'And more — Boxcar, Join, Prowl, Pushbullet, Simplepush, and others.'
        ]
      },
      {
        heading: 'Notification Events',
        paragraphs: [
          'Each connection can be configured to trigger on specific events:'
        ],
        list: [
          'On Grab — When a release is sent to the download client.',
          'On Import / On Upgrade — When a file is successfully imported or upgraded to better quality.',
          'On Rename — When files are renamed per your naming settings.',
          'On Author Added / Author Deleted — When library entries are added or removed.',
          'On Book Retag — When book file metadata tags are updated.',
          'On Book File Delete / On Book File Delete for Upgrade — When files are removed.',
          'On Health Issue / On Health Restored — When system health problems are detected or resolved.',
          'On Application Update — When Bibliophilarr updates itself.',
          'Include Health Warnings — Whether to include warnings (not just errors) in health notifications.'
        ]
      },
      {
        heading: 'Tags and Notification Filtering',
        paragraphs: [
          'You can assign tags to notification connections. When tags are used, the notification only fires for authors that have at least one matching tag. This lets you send different notifications for different parts of your library (e.g., Discord for audiobooks, email for ebooks).'
        ]
      }
    ]
  },
  {
    id: 'system',
    title: 'System Administration',
    icon: 'SYSTEM',
    description: 'Health checks, scheduled tasks, backups, updates, events, and log files.',
    content: [
      {
        heading: 'Status & Health Checks',
        paragraphs: [
          'The Status page runs periodic health checks and alerts you to configuration problems. Health checks are color-coded:'
        ],
        list: [
          'Red (Error) — Critical issues that prevent normal operation.',
          'Orange (Warning) — Problems that may affect functionality but aren\'t blocking.'
        ]
      },
      {
        heading: 'Common Health Check Warnings',
        paragraphs: [
          'System warnings:'
        ],
        list: [
          'Branch is not a valid release branch — Change to a valid release branch in Settings → General.',
          'New update available — A newer version of Bibliophilarr is available.',
          'Cannot install update because startup folder is not writable — Fix permissions on the installation directory.',
          'Could not connect to SignalR — Usually a reverse proxy configuration issue. For Nginx, add: proxy_http_version 1.1; proxy_set_header Upgrade $http_upgrade; proxy_set_header Connection $http_connection;',
          'System time is off by more than 1 day — Sync your system clock with an authoritative time server.'
        ]
      },
      {
        heading: 'Download Client Health Checks',
        list: [
          'No download client is available — Add and configure at least one download client.',
          'Unable to communicate with download client — Check the client\'s host, port, and credentials. Verify its web UI is enabled.',
          'Download clients are unavailable due to failure — The client is temporarily unresponsive. Bibliophilarr will retry automatically.',
          'Docker bad remote path mapping — Different Docker volume mounts between Bibliophilarr and the download client. Fix mount consistency or add a Remote Path Mapping.',
          'Downloading into root folder — Your download client is saving files directly in your library root. Change the download folder to a separate location.',
          'Completed Download Handling is disabled — Enable it for automatic import of completed downloads.'
        ]
      },
      {
        heading: 'Indexer Health Checks',
        list: [
          'No indexers available with automatic search enabled — Enable "Automatic Search" on at least one indexer.',
          'No indexers available with RSS sync enabled — Enable "RSS" on at least one indexer for automatic new-release detection.',
          'No indexers are enabled — Add and enable at least one indexer.',
          'Indexers are unavailable due to failures — An indexer is temporarily unreachable. Will auto-recover.',
          'Jackett /all endpoint used — Add each tracker individually instead of using the aggregate /all endpoint.'
        ]
      },
      {
        heading: 'Other Health Checks',
        list: [
          'Missing root folder — A configured root folder no longer exists on disk. Update the path or move the affected authors to a valid root folder.',
          'Import lists are unavailable due to failures — A list provider is unreachable. Check connectivity and credentials.',
          'Author mount is read only — The filesystem containing an author\'s folder is mounted read-only. Fix mount options.'
        ]
      },
      {
        heading: 'Disk Space',
        paragraphs: [
          'Shows available disk space for each root folder and the application data directory. In Docker, this shows available space within the container\'s volume mount.'
        ]
      },
      {
        heading: 'About',
        paragraphs: [
          'Displays version information, .NET runtime version, database type, application data directory, startup directory, and other system details useful for troubleshooting.'
        ]
      },
      {
        heading: 'Scheduled Tasks',
        paragraphs: [
          'View and manually trigger all scheduled tasks. Key tasks include:'
        ],
        list: [
          'RSS Sync — Fetch new releases from indexer RSS feeds (default: every 15 minutes).',
          'Refresh Author — Update metadata for all authors from the metadata provider.',
          'Application Check Update — Check for new Bibliophilarr versions.',
          'Housekeeping — Database maintenance, cleanup of orphaned records.',
          'Import List Sync — Import new entries from configured import lists.',
          'Rescan Folders — Scan all root folders for new, changed, or deleted files.',
          'Refresh Monitored Downloads — Poll download clients for progress updates.',
          'Messaging Cleanup — Clean up UI notification messages.',
          'Backup — Create an automatic weekly backup of the database and configuration.'
        ],
        note: 'You can manually trigger any task by clicking its run icon. Tasks that are currently running show their progress.'
      },
      {
        heading: 'Task Queue',
        paragraphs: [
          'The queue tab shows running and upcoming tasks, plus a history of recently completed tasks with their execution duration.'
        ]
      },
      {
        heading: 'Backups',
        paragraphs: [
          'Bibliophilarr automatically creates weekly backups of its database (bibliophilarr.db) and configuration files. Backups are stored in the application data directory.'
        ],
        list: [
          'Backup Now — Trigger an immediate manual backup.',
          'Restore Backup — Restore from a previous backup. You can also upload a backup .zip file.',
          'Download — Download any backup to save it externally.',
          'Delete — Remove old backups you no longer need.'
        ],
        note: 'Backups include the database and essential configuration. They do NOT include your media files, root folder contents, or download client state. Always keep external copies of critical backups.'
      },
      {
        heading: 'Updates',
        paragraphs: [
          'Shows the last 5 updates with release notes. You can install updates from this page if auto-update is not configured.',
          'Update settings (branch, mechanism, script) are configurable in Settings → General → Updates.'
        ],
        note: 'If running in Docker, application updates happen by pulling a new Docker image, not through the built-in updater.'
      },
      {
        heading: 'Events',
        paragraphs: [
          'Events show application-level log entries at the INFO level and above. This provides a quick overview of what Bibliophilarr has been doing without diving into full log files.',
          'Each event shows the component that generated it and the message. Use the gear icon to adjust how many events are displayed per page. Events can be refreshed or cleared.'
        ],
        note: 'Events are not the same as log files. For detailed troubleshooting, use the trace/debug log files accessible from System → Log Files.'
      },
      {
        heading: 'Log Files',
        paragraphs: [
          'Access and download log files directly from the UI. Bibliophilarr uses rolling log files:'
        ],
        list: [
          'readarr.txt — Current main log file (info, warn, error, fatal). Limited to 1MB, up to 51 rolling files.',
          'readarr.debug.txt — Debug-level log (when Debug logging enabled). Contains info + debug entries. Usually covers ~40 hours.',
          'readarr.trace.txt — Trace-level log (when Trace logging enabled). Most verbose, contains everything. Covers a few hours at most.',
          'Update logs — Separate logs for the update process, accessible via the dropdown toggle.'
        ]
      },
      {
        heading: 'Changing Log Level',
        paragraphs: [
          'Change the log level in Settings → General → Logging. No restart required — the change takes effect immediately.',
          'If you can\'t access the UI, edit config.xml in the AppData directory and set the LogLevel value to Debug or Trace.'
        ]
      }
    ]
  },
  {
    id: 'troubleshooting',
    title: 'Troubleshooting',
    icon: 'BUG',
    description: 'Downloads, imports, searches, permissions, Docker, and common problems with solutions.',
    content: [
      {
        heading: 'General Troubleshooting Steps',
        paragraphs: [
          'For any issue, start with these steps:'
        ],
        list: [
          '1. Enable Trace logging: Settings → General → Log Level → Trace.',
          '2. Clear existing logs: System → Logs → Clear Logs.',
          '3. Reproduce the issue (trigger the operation that\'s failing).',
          '4. Check the trace log file (readarr.trace.txt) for error messages.',
          '5. Look for the relevant context — search for the book title, author, or operation.',
          '6. When sharing logs for help, use a pastebin service and share a relevant excerpt, not the entire file.'
        ]
      },
      {
        heading: 'No Books Being Downloaded',
        paragraphs: [
          'If monitored books aren\'t being grabbed, check these in order:'
        ],
        list: [
          'Verify indexers are configured and enabled for RSS and/or Auto Search (Settings → Indexers). Test each indexer.',
          'Verify at least one download client is configured, enabled, and responding (Settings → Download Clients). Test the client.',
          'Check System → Status for any health warnings (red/orange indicators).',
          'Check Activity → History for grab events — if none exist, no matching releases have been found.',
          'Check the logs for rejection reasons — common: "Unknown Author" (author not in library), "Quality not wanted" (release quality excluded from profile).',
          'Verify the books are monitored (not just the author — individual books must also be monitored).',
          'Try a manual search from the book detail page or Wanted → Missing to actively query indexers and see available releases.'
        ],
        note: 'RSS sync only matches NEW releases from indexers to your library. It does not search for old/backlog releases. For missing books from the past, use the manual/mass search on the Wanted pages.'
      },
      {
        heading: 'Downloads Stuck in Queue',
        paragraphs: [
          'If downloads appear in the Queue but never import:'
        ],
        list: [
          'Hover over the status icon for detailed error messages.',
          'Verify the download client is responding — check its web UI directly.',
          'Check for Remote Path Mapping issues: if the log shows "path does not exist or is not accessible", the download client reports a path Bibliophilarr can\'t access.',
          'Check file/folder permissions — Bibliophilarr needs read access to the download folder.',
          'For Docker: ensure consistent volume mounts between Bibliophilarr and your download client containers.',
          'Check the Completed Download Handling setting is enabled.'
        ]
      },
      {
        heading: 'Files Not Importing',
        paragraphs: [
          'Completed downloads not being imported (orange status icon):'
        ],
        list: [
          'Check download folder permissions — Bibliophilarr needs read access to the completed download path.',
          'Verify the root folder exists and is writable by Bibliophilarr.',
          'Check if the book file was already imported (duplicate detection).',
          'Verify the book match threshold — the release name must match the expected book closely enough. Audiobook releases with verbose naming may score below the threshold.',
          'Check for packed/archived torrents — .rar files need extraction (consider Unpackerr).',
          'Review the logs for the specific import error message.'
        ]
      },
      {
        heading: 'Wrong Format Imported',
        paragraphs: [
          'If the wrong format gets imported (e.g., PDF instead of EPUB when both exist in the download):',
          'Bibliophilarr imports in order of quality priority in your profile, regardless of which qualities are checked (allowed) or not. To fix this:'
        ],
        list: [
          'Go to Settings → Profiles and edit the relevant quality profile.',
          'Drag your preferred format to the TOP of the quality list.',
          'Ensure your preferred format is ranked above less-desired formats.',
          'Save the profile — future imports will respect the new priority order.'
        ]
      },
      {
        heading: 'Permission Issues',
        paragraphs: [
          'Permission problems are the most common cause of import failures. Check:'
        ],
        list: [
          'Library root folder — Bibliophilarr needs read + write access.',
          'Download folder — Bibliophilarr needs read access (and write if using move instead of hardlink/copy).',
          'On Linux: check ownership with ls -la and id. The Bibliophilarr process user/group must have access.',
          'On Docker: set PUID/PGID to match the owner of your media and download directories.',
          'On NFS mounts: add "nolock" to mount options.',
          'On SMB/CIFS mounts: add "nobrl" to mount options.',
          'On Windows Service: the default "Local Service" account has limited permissions — consider running as a tray application or assigning explicit folder permissions.'
        ]
      },
      {
        heading: 'Remote Path Mapping Issues',
        paragraphs: [
          'If you see errors like "Import failed, path does not exist or is not accessible", you likely need a Remote Path Mapping or need to fix your Docker volume mounts.'
        ],
        list: [
          'Verify: what path does the download client report? (Check the log for the exact path.)',
          'Verify: can Bibliophilarr access that path? (Check from Bibliophilarr\'s perspective, not yours.)',
          'If paths differ: add a Remote Path Mapping (Settings → Download Clients → Remote Path Mappings).',
          'In Docker: if both Bibliophilarr and the client are containers, fix the volume mounts instead of using remote path maps.',
          'Remote Path Mapping is a simple find/replace — it replaces the Remote Path prefix with the Local Path. Verify the replacement produces a valid path.'
        ]
      },
      {
        heading: 'Docker-Specific Issues',
        paragraphs: [
          'Docker adds complexity with volume mounts, user permissions, and path mapping. Common Docker issues:'
        ],
        list: [
          'Inconsistent volume mounts — Use the same base path in all containers (e.g., /data/). Your download client should download to /data/downloads/ and Bibliophilarr should import from /data/media/.',
          'PUID/PGID mismatch — Ensure PUID and PGID environment variables match the actual owner of the mounted directories.',
          'Different paths for the same folder — If the download client sees /downloads/books/ but Bibliophilarr sees /data/downloads/books/, either fix the mounts or add a Remote Path Mapping.'
        ]
      },
      {
        heading: 'Search Returns No Results',
        paragraphs: [
          'If manual search returns no results or unexpected results:'
        ],
        list: [
          'Verify the indexer is working: Settings → Indexers → Test the indexer.',
          'Check categories — incorrect categories are the most common cause of missing results. Each indexer may use different category IDs for books vs audiobooks.',
          'Check if the book is properly tagged/ID\'d on the indexer — search-by-ID may fail if the indexer doesn\'t have the correct mapping.',
          'Verify the book and author are monitored.',
          'Enable trace logging and run the search again — check the log for the exact query URL sent to the indexer and any error responses.',
          'Test the indexer URL directly in a browser (with your API key) to see if it returns results.'
        ]
      },
      {
        heading: 'Connection Issues',
        paragraphs: [
          'Network and SSL errors:'
        ],
        list: [
          '"The underlying connection was closed" — The indexer or client uses a TLS/SSL version not supported by Bibliophilarr\'s .NET runtime.',
          '"The request timed out" — The target server didn\'t respond in time. May be caused by: VPN misconfiguration, proxy issues, DNS problems, or IPv6 being enabled but non-functional.',
          'Certificate validation errors — Ensure system certificates are up to date and system time is correct.',
          'Rate limiting / IP bans — Some indexers (e.g. Nyaa) may temporarily ban IPs that make too many requests. Wait for the ban to expire or use a different IP.'
        ]
      },
      {
        heading: 'Repeated Downloads',
        paragraphs: [
          'If the same book keeps being re-downloaded:'
        ],
        list: [
          'Check release profile indexer restrictions — preferred word scores are zero for existing library files if restricted to a specific indexer, causing a false "upgrade" detection loop.',
          'Check if the download is actually failing to import — a file that\'s grabbed but never imported will be re-grabbed on the next search.',
          'Verify your quality profile — ensure the cutoff is set correctly to avoid unnecessary "upgrades".'
        ]
      },
      {
        heading: 'Book Imported with Wrong Edition',
        paragraphs: [
          'If a book imports with an incorrect edition, the only reliable way to fix it is:'
        ],
        list: [
          'Move the book file completely out of Bibliophilarr\'s root folder.',
          'Go to Wanted → Missing or use manual import to re-import the file.',
          'During import, use the edition dropdown at the bottom of the screen to select the correct edition.',
          'The file will be re-imported and mapped to the correct edition.'
        ]
      },
      {
        heading: 'Getting Help',
        paragraphs: [
          'If you can\'t resolve an issue:'
        ],
        list: [
          'Enable Trace logging temporarily (Settings → General → Log Level → Trace).',
          'Reproduce the issue to capture detailed logs.',
          'Check System → Events for recent errors.',
          'Download log files from System → Log Files for analysis.',
          'Share relevant log excerpts (not the entire file) on a pastebin service.',
          'Visit the GitHub Discussions or Issues page for community support.',
          'Include in your help request: Bibliophilarr version, OS/distribution, Docker (yes/no), download client type and version, and a clear description of the issue.'
        ]
      }
    ]
  },
  {
    id: 'faq',
    title: 'FAQ',
    icon: 'TBA',
    description: 'Frequently asked questions about setup, behavior, downloads, and troubleshooting.',
    content: [
      {
        heading: 'General Questions'
      },
      {
        heading: 'How does Bibliophilarr find books?',
        paragraphs: [
          'Bibliophilarr does NOT actively monitor torrent/usenet sites. It uses two methods:',
          'RSS Sync — Every 15 minutes (configurable), it checks each indexer\'s RSS feed for newly posted releases and matches them against your monitored library. This is passive — it only catches releases posted since the last check.',
          'Search — Active queries against all enabled indexers for specific books. Triggered manually (from the UI), on author/book add (if "Search for Missing" is checked), or when the user initiates mass search from the Wanted pages.',
          'There is no scheduled automatic search for old/missing books. This is deliberate to avoid overloading indexers.'
        ]
      },
      {
        heading: 'Why is authentication required?',
        paragraphs: [
          'All *arr applications require authentication. Exposing Bibliophilarr without authentication — especially to the internet — allows anyone to access your library, trigger downloads, and potentially execute actions on your system.',
          'The authentication requirement cannot be disabled. Configure Forms, Basic, or External authentication on first launch.'
        ]
      },
      {
        heading: 'Can I monitor both ebook and audiobook for the same book?',
        paragraphs: [
          'Yes. Bibliophilarr is the only *arr application that supports this natively. With Dual Format Tracking enabled (the default since v1.1.0-dev.27), each book can have independent ebook and audiobook monitoring with separate quality profiles.',
          'In the original Readarr, this required running two separate instances. Bibliophilarr eliminates that complexity.'
        ]
      },
      {
        heading: 'How does download decision comparison work?',
        paragraphs: [
          'When multiple releases are available for the same book, Bibliophilarr ranks them in this priority order:'
        ],
        list: [
          '1. Quality — Higher ranking in your quality profile wins.',
          '2. Custom Format Score — Higher total custom format score wins.',
          '3. Protocol — Usenet or Torrent preference (set in Delay Profiles, default: both equal).',
          '4. Indexer Priority — Lower priority number = preferred.',
          '5. Seed count — For torrents, more seeders preferred.',
          '6. Book count — Fewer books in release preferred (avoids box set when you want single book).',
          '7. Age — For Usenet, newer NZBs preferred.',
          '8. Size — Smaller file size preferred when all else is equal.'
        ],
        paragraphs2: [
          'The first criterion that differs between two releases determines the winner. If quality is the same, it moves to custom format score, and so on.'
        ]
      },
      {
        heading: 'Why aren\'t my missing books being downloaded automatically?',
        paragraphs: [
          'RSS sync only matches brand-new indexer releases to your library. It does NOT search for old or backlog content. For existing/historical releases, you must trigger a search manually:',
        ],
        list: [
          'When adding an author: check "Search for Missing Books".',
          'From Wanted → Missing: use Search All, Search Filtered, or Search Selected.',
          'From an author\'s detail page: use the Search button.',
          'From a book\'s detail page: click the search icon.'
        ]
      },
      {
        heading: 'What are the quality IDs?',
        paragraphs: [
          'Quality IDs determine the format type:'
        ],
        list: [
          'Ebook qualities: 0 = Unknown, 1 = PDF, 2 = MOBI, 3 = EPUB, 4 = AZW3.',
          'Audiobook qualities: 10 = MP3, 11 = FLAC, 12 = M4B, 13 = Unknown Audio.',
          'The format type (Ebook or Audiobook) is automatically derived from the quality ID.'
        ]
      },
      {
        heading: 'Setup & Configuration'
      },
      {
        heading: 'How do I force a search for all missing books?',
        paragraphs: [
          'Go to Wanted → Missing, select all books using the header checkbox, then click "Search Selected". You can also use "Search All" to search without selecting. For a single author, use the search button on their detail page.',
        ],
        note: 'Large mass searches may take a long time and could hit indexer rate limits. Consider searching in smaller batches.'
      },
      {
        heading: 'Where is the configuration stored?',
        paragraphs: [
          'The application data directory varies by OS:'
        ],
        list: [
          'Linux — ~/.config/Bibliophilarr/ (default)',
          'Windows — C:\\ProgramData\\Bibliophilarr\\',
          'macOS — ~/.config/Bibliophilarr/',
          'Docker — Wherever you mount the /config volume'
        ],
        paragraphs2: [
          'This directory contains: the database (bibliophilarr.db), configuration (config.xml), logs (logs/), backups (Backups/), and update files.'
        ]
      },
      {
        heading: 'How do mapped network drives work?',
        paragraphs: [
          'Mapped network drives (like X:\\) are not available when running as a Windows Service because the service runs under "Local Service" which doesn\'t have access to drive mappings created by your user session.',
          'Solutions:'
        ],
        list: [
          'Use UNC paths (\\\\server\\share) instead of mapped drives.',
          'Run Bibliophilarr as a tray application instead of a Windows Service.',
          'Assign permissions to the service account and use UNC paths in the configuration.'
        ]
      },
      {
        heading: 'How do I use Bibliophilarr with Docker?',
        paragraphs: [
          'Key Docker configuration tips:'
        ],
        list: [
          'Map your book library, download directory, and application data directory as Docker volumes.',
          'Use a common parent path (e.g., /data/) for both downloads and library to enable hardlinks.',
          'Set PUID and PGID environment variables to match the owner of your media directories.',
          'Ensure paths are consistent between Bibliophilarr and download client containers.',
          'Use Remote Path Mappings only if you can\'t fix mount inconsistencies.'
        ],
        warning: 'Docker updates happen by pulling a new image, not through the built-in updater. The in-app updater won\'t work in Docker.'
      },
      {
        heading: 'Downloads & Importing'
      },
      {
        heading: 'Why did it import the wrong format?',
        paragraphs: [
          'When a download contains multiple formats (e.g., both EPUB and AZW3), Bibliophilarr imports based on the quality priority ranking in your profile — not just which qualities are checked/allowed.',
          'Fix: Go to Settings → Profiles, edit the quality profile, and drag your preferred format to the top of the priority list.'
        ]
      },
      {
        heading: 'What is the book match threshold?',
        paragraphs: [
          'When importing a completed download, Bibliophilarr compares the file name to the expected book title. If the match percentage is too low, import is rejected to prevent importing wrong content.',
          'The default threshold is 70%. For app-initiated downloads (books that Bibliophilarr itself searched and grabbed), a more lenient 50% threshold is used because the search already verified the correct book.',
          'This threshold is configurable in the advanced settings.'
        ]
      },
      {
        heading: 'Why are my downloads being blocklisted?',
        paragraphs: [
          'Downloads are blocklisted when they fail to download or import. Common causes:'
        ],
        list: [
          'Download failed in the client (incomplete, corrupted, or extraction error).',
          'Import failed due to path/permission issues.',
          'The file didn\'t match the expected book.',
          'You can clear blocklist entries to allow re-download: Activity → Blocklist → remove the entry.'
        ]
      },
      {
        heading: 'Maintenance & Recovery'
      },
      {
        heading: 'How do I backup and restore?',
        paragraphs: [
          'Automatic backups run weekly and are stored in the application data directory under Backups/.'
        ],
        list: [
          'Manual backup: System → Backups → Backup Now.',
          'Download: System → Backups → click the backup entry to download.',
          'Restore: System → Backups → Restore. You can also upload a backup .zip file.',
          'For disaster recovery: stop Bibliophilarr, replace the database file with the backup copy, restart.'
        ],
        note: 'Backups include the database and configuration, NOT your media files. Always maintain separate backup strategies for your book library.'
      },
      {
        heading: 'How do I recover from a failed update?',
        list: [
          'Check the update log files in the UpdateLogs/ folder of your app data directory.',
          'The most common cause is /tmp directory being cleared during the upgrade process.',
          'Permission issues — ensure Bibliophilarr can write to its installation directory and /tmp.',
          'For Docker: simply pull the latest stable image and recreate the container.',
          'For manual installs: download the latest release from GitHub and extract over the existing installation, then restart.'
        ]
      },
      {
        heading: 'VPN & Proxy Considerations',
        paragraphs: [
          'Bibliophilarr itself generally does not need a VPN. The download client handles the actual downloading. However:'
        ],
        list: [
          'If using a VPN, only route the download client through it, not Bibliophilarr.',
          'VPNs can cause indexer connectivity issues (rate limiting, IP bans, SSL errors).',
          'Proxies must be properly configured in Settings → General → Proxy if used.',
          'Local DNS issues from VPN configurations can prevent indexer connectivity.'
        ]
      },
      {
        heading: 'What Custom Formats should I use?',
        paragraphs: [
          'Custom Formats are optional but can refine release selection. Common setups:'
        ],
        list: [
          'Prefer specific narrators for audiobooks.',
          'Prefer specific release groups known for quality.',
          'Penalize abridged releases for unabridged preference.',
          'Score by file size ranges for your preferred balance of quality vs space.',
          'See the Custom Formats wiki page for detailed setup instructions.'
        ]
      }
    ]
  },
  {
    id: 'custom-formats',
    title: 'Custom Formats',
    icon: 'INTERACTIVE',
    description: 'Creating scoring rules for automated release selection.',
    content: [
      {
        heading: 'Custom Formats Overview',
        paragraphs: [
          'Custom Formats are user-defined rules that score releases based on various criteria. When multiple releases are available for the same book, the release with the highest total custom format score (after quality comparison) is preferred.',
          'Custom Formats are completely optional — Bibliophilarr works well with just quality profiles. They\'re for users who want fine-grained control over which specific releases are grabbed.'
        ]
      },
      {
        heading: 'Creating a Custom Format',
        paragraphs: [
          'Go to Settings → Custom Formats → Add (+). Give it a name and add one or more specifications (conditions):'
        ],
        list: [
          'Release Title — Match against the release name using regex patterns. Examples: "unabridged", "narrator name", release group names.',
          'Quality — Match a specific quality level (e.g., M4B, EPUB, FLAC).',
          'Size — Match file size ranges (e.g., prefer releases between 200MB–2GB for audiobooks).',
          'Release Group — Match a specific release group name.',
          'Language — Match a specific language.',
          'Indexer Flag — Match flags set by indexers (e.g., freeleech, internal).'
        ]
      },
      {
        heading: 'Condition Logic',
        paragraphs: [
          'Each specification has a "Negate" toggle. When negated, the condition is inverted (matches when the criterion is NOT met).',
          'Each specification also has a "Required" toggle. When required, the custom format only matches if this specific condition is met. When not required, the condition is optional — matching any non-required condition (when all required ones pass) activates the format.',
          'Multiple specifications in a custom format use AND logic by default (all must match). Use the Required flag to create more complex matching rules.'
        ]
      },
      {
        heading: 'Scoring Custom Formats in Profiles',
        paragraphs: [
          'After creating custom formats, assign scores to them in your quality profiles:',
          'Go to Settings → Profiles → select a quality profile. At the bottom, each custom format has a score field.'
        ],
        list: [
          'Positive scores — Prefer releases matching this format (e.g., +100 for preferred narrator).',
          'Negative scores — Penalize releases matching this format (e.g., -1000 for abridged releases to effectively reject them).',
          'Zero score — Track the format (shown in the UI) but don\'t influence selection.',
          'Minimum Custom Format Score — Set a minimum total score threshold. Releases below this threshold are rejected entirely.'
        ]
      },
      {
        heading: 'Scoring Math',
        paragraphs: [
          'The total custom format score for a release is the sum of all matching custom format scores. If a release matches "Preferred Narrator" (+100) and "Freeleech" (+50) but also "Low Quality Rip" (-30), the total score is 120.',
          'This total score is compared after quality comparison — two releases of the same quality are differentiated by their custom format scores.'
        ]
      },
      {
        heading: 'Example Use Cases',
        list: [
          'Audiobook narrator preference — Create a custom format with Release Title regex matching your preferred narrator\'s name. Score it +100.',
          'Avoid abridged — Create a format matching "abridged" in release title, negate it (so it matches when "abridged" IS present), score -1000.',
          'Prefer freeleech — Match "Indexer Flag: Freeleech", score +50.',
          'Release group preference — Match your trusted release group names, score +75 each.'
        ]
      }
    ]
  }
];

export default wikiPages;
