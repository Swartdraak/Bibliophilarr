const wikiPages = [
  {
    id: 'getting-started',
    title: 'Getting Started',
    icon: 'BOOK',
    description: 'First-time setup, adding authors, and initial configuration.',
    content: [
      {
        heading: 'Welcome to Bibliophilarr',
        paragraphs: [
          'Bibliophilarr is a book library management tool for ebook and audiobook enthusiasts. It monitors your favorite authors, automatically downloads new releases, and organizes your library.',
          'This guide walks you through initial setup after your first launch.'
        ]
      },
      {
        heading: 'Initial Configuration',
        paragraphs: [
          'After launching Bibliophilarr for the first time:',
        ],
        list: [
          'Set a root folder — this is where your book files will be stored. Go to Settings → Media Management → Root Folders.',
          'Add at least one indexer — Go to Settings → Indexers and configure your preferred indexer (Usenet or Torrent).',
          'Add a download client — Go to Settings → Download Clients and configure qBittorrent, SABnzbd, or your preferred client.',
          'Set up a quality profile — Go to Settings → Profiles to define your preferred quality (EPUB, MOBI, PDF, MP3, M4B, etc.).'
        ]
      },
      {
        heading: 'Adding Your First Author',
        paragraphs: [
          'Navigate to Library → Add New, then search for an author or book title. Select the correct match from the metadata results.',
          'When adding an author you can choose to:',
        ],
        list: [
          'Monitor — Track this author for new and missing releases.',
          'Search for Missing Books — Immediately trigger a search for all monitored books that are missing files.',
          'Root Folder — Where this author\'s books will be stored.',
          'Quality Profile — Which quality settings to apply.'
        ]
      },
      {
        heading: 'How Downloads Work',
        paragraphs: [
          'Bibliophilarr uses two mechanisms to find and download books:',
        ],
        list: [
          'RSS Sync — Runs every 15 minutes by default. Checks your indexers for new releases and matches them against your monitored library. Only grabs releases that match a known author and book.',
          'Manual/Automatic Search — Actively queries indexers for specific missing or cutoff-unmet books. This is triggered by clicking the search icon on a book, using the mass search on Wanted pages, or enabling "Search for Missing Books" when adding an author.'
        ],
        note: 'RSS sync only matches releases to authors already in your library. If a release appears for an author you haven\'t added, it will be rejected as "Unknown Author".'
      }
    ]
  },
  {
    id: 'library',
    title: 'Library Management',
    icon: 'AUTHOR_CONTINUING',
    description: 'Managing authors, books, editions, and your bookshelf.',
    content: [
      {
        heading: 'Library Overview',
        paragraphs: [
          'The Library section contains your Authors, Books, and Bookshelf views. Each provides different ways to browse and manage your collection.'
        ]
      },
      {
        heading: 'Authors',
        paragraphs: [
          'The Authors page shows all authors in your library. You can filter by monitored status, quality profile, or root folder. Click an author to see their complete book list.',
          'From an author\'s detail page you can:'
        ],
        list: [
          'Toggle monitoring for individual books or the entire author.',
          'Search for all missing books at once.',
          'Edit the author\'s metadata provider mapping.',
          'View the author\'s history of downloads, imports, and renames.',
          'Manage file organization and quality for existing files.'
        ]
      },
      {
        heading: 'Books',
        paragraphs: [
          'The Books index provides a flat view of all books across your library. This is useful for quick filtering and bulk operations.',
          'Each book shows monitoring status, download status, quality, and format type (ebook or audiobook).'
        ]
      },
      {
        heading: 'Bookshelf',
        paragraphs: [
          'The Bookshelf is a compact view for bulk-editing monitoring status. Toggle individual books on or off quickly without navigating into each author.'
        ]
      },
      {
        heading: 'Unmapped Files',
        paragraphs: [
          'Files found in your root folders that Bibliophilarr couldn\'t automatically match to a known book appear here. You can manually map them to the correct book.'
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
          'Dual Format Tracking allows Bibliophilarr to manage both ebook and audiobook versions of the same book simultaneously. When enabled, each book can have independent ebook and audiobook quality profiles, monitoring status, and file tracking.',
          'This feature is enabled by default (since v1.1.0-dev.26).'
        ]
      },
      {
        heading: 'How It Works',
        paragraphs: [
          'With Dual Format Tracking enabled, each book has a "Format Statuses" array — one entry per tracked format (ebook and/or audiobook). Each entry tracks:'
        ],
        list: [
          'Format type — Ebook or Audiobook.',
          'Monitored — Whether this format is actively sought.',
          'Has File — Whether a file of this format exists.',
          'Quality Profile — The quality criteria for this format (e.g., EPUB for ebook, M4B for audiobook).'
        ]
      },
      {
        heading: 'Format Badges',
        paragraphs: [
          'Throughout the UI, format status is shown as colored badges:',
        ],
        list: [
          'E (green) — Ebook with file present.',
          'E (red) — Ebook monitored but missing.',
          'A (green) — Audiobook with file present.',
          'A (red) — Audiobook monitored but missing.',
          'Both badges appear when both formats are monitored.'
        ]
      },
      {
        heading: 'Disabling Dual Format Tracking',
        paragraphs: [
          'If you only need one format, you can disable this feature in Settings → Media Management → EnableDualFormatTracking. When disabled, Bibliophilarr tracks a single format per book based on the quality profile assigned.'
        ]
      }
    ]
  },
  {
    id: 'quality-profiles',
    title: 'Quality Profiles',
    icon: 'PROFILE',
    description: 'Configuring quality preferences for ebooks and audiobooks.',
    content: [
      {
        heading: 'Understanding Quality Profiles',
        paragraphs: [
          'Quality profiles define which file formats are acceptable and which are preferred. You assign a quality profile to each author (or per-format when using Dual Format Tracking).'
        ]
      },
      {
        heading: 'Ebook Qualities',
        list: [
          'EPUB — The most common open ebook format. Widely supported by readers.',
          'MOBI — Amazon Kindle format. Being phased out in favor of AZW3.',
          'AZW3 — Amazon\'s improved Kindle format with better formatting support.',
          'PDF — Portable Document Format. Fixed layout, not ideal for ereaders.',
          'Unknown — Unidentified ebook format.'
        ]
      },
      {
        heading: 'Audiobook Qualities',
        list: [
          'M4B — The standard audiobook format. Supports chapters and bookmarks.',
          'MP3 — Universal audio format. Widely compatible but no chapter support.',
          'FLAC — Lossless audio format. Large files but perfect quality.',
          'Unknown Audio — Unidentified audiobook format.'
        ]
      },
      {
        heading: 'Cutoff',
        paragraphs: [
          'The cutoff setting in a quality profile defines the minimum acceptable quality. If a book\'s file quality is below the cutoff, it appears on the Wanted → Cutoff Unmet page.',
          'Bibliophilarr will upgrade files when a higher-quality release matching the profile is found, up to the highest allowed quality.'
        ]
      }
    ]
  },
  {
    id: 'indexers',
    title: 'Indexers',
    icon: 'SEARCH',
    description: 'Configuring indexers and how RSS sync works.',
    content: [
      {
        heading: 'What Are Indexers?',
        paragraphs: [
          'Indexers are the services that Bibliophilarr queries to find book releases. They can be Usenet indexers (NZBs) or Torrent indexers/trackers.',
          'You can add indexers directly or use Prowlarr to manage indexers centrally across multiple *arr applications.'
        ]
      },
      {
        heading: 'Adding an Indexer',
        paragraphs: [
          'Go to Settings → Indexers → Add Indexer (+). Select your indexer type and provide the required credentials (API key, URL, etc.).',
          'Each indexer has toggles for:'
        ],
        list: [
          'Enable RSS — Include this indexer in periodic RSS sync checks.',
          'Enable Automatic Search — Allow this indexer to be queried during automatic book searches.',
          'Enable Interactive Search — Include this indexer in manual interactive searches.'
        ]
      },
      {
        heading: 'RSS Sync',
        paragraphs: [
          'RSS sync runs on a schedule (default: every 15 minutes). It fetches the latest releases from your indexers and tries to match them to monitored books in your library.',
          'Important: RSS only matches against authors and books already in your library. Releases for unknown authors are rejected.'
        ],
        note: 'If you see "Reports found: X, Reports grabbed: 0" in logs, it means none of the new indexer releases matched your library. This is normal if the releases are for authors you haven\'t added.'
      },
      {
        heading: 'Prowlarr Integration',
        paragraphs: [
          'Prowlarr can sync indexers automatically to Bibliophilarr. Configure the Bibliophilarr application in Prowlarr with your API key and URL, then Prowlarr will push indexer configurations to Bibliophilarr.'
        ]
      }
    ]
  },
  {
    id: 'download-clients',
    title: 'Download Clients',
    icon: 'DOWNLOADING',
    description: 'Setting up download clients and remote path mappings.',
    content: [
      {
        heading: 'Supported Download Clients',
        paragraphs: [
          'Bibliophilarr supports both Usenet and Torrent download clients:'
        ],
        list: [
          'Usenet — SABnzbd, NZBGet, NZBVortex.',
          'Torrent — qBittorrent, Transmission, Deluge, rTorrent, uTorrent, Vuze, Aria2, Flood.'
        ]
      },
      {
        heading: 'Configuration',
        paragraphs: [
          'Go to Settings → Download Clients → Add (+). Select your client type and provide the connection details (host, port, credentials).',
          'Ensure the download client\'s completed-download folder is accessible to Bibliophilarr for importing.'
        ]
      },
      {
        heading: 'Remote Path Mappings',
        paragraphs: [
          'If your download client and Bibliophilarr see different paths for the same files (e.g., Docker containers or remote systems), configure a Remote Path Mapping.',
          'Go to Settings → Download Clients → Remote Path Mappings. Map the path the download client reports to the path Bibliophilarr can access.'
        ],
        note: 'Remote Path Mappings are rarely needed when Bibliophilarr and the download client run on the same system. Match your paths in container configurations instead.'
      },
      {
        heading: 'Completed Download Handling',
        paragraphs: [
          'When a download completes, Bibliophilarr will:',
        ],
        list: [
          'Detect the completed download via polling the download client.',
          'Import the file to the correct author/book folder.',
          'Rename the file according to your naming settings.',
          'Remove the download from the client (if configured).'
        ]
      }
    ]
  },
  {
    id: 'wanted',
    title: 'Wanted & Searching',
    icon: 'WARNING',
    description: 'Understanding Missing, Cutoff Unmet, and search behavior.',
    content: [
      {
        heading: 'Wanted → Missing',
        paragraphs: [
          'The Missing page shows all monitored books that don\'t have any downloaded files. Each entry shows the author, book title, release date, format status (when Dual Format Tracking is enabled), and the last time a search was run.',
          'You can trigger searches for individual books or use the mass search toolbar to search for all missing books at once.'
        ]
      },
      {
        heading: 'Wanted → Cutoff Unmet',
        paragraphs: [
          'Cutoff Unmet shows books that have a file, but the file\'s quality is below the cutoff defined in the quality profile. Bibliophilarr will attempt to upgrade these when better releases are found.',
          'Like Missing, you can trigger searches individually or in bulk.'
        ]
      },
      {
        heading: 'Search Behavior',
        paragraphs: [
          'There are three ways books get downloaded:'
        ],
        list: [
          'RSS Sync — Automatic, periodic. Only matches new indexer releases to your library. Does NOT actively search for missing books.',
          'Search on Add — When you add an author with "Search for Missing Books" checked, Bibliophilarr immediately searches all indexers for that author\'s missing books.',
          'Manual Search — Click the search icon on any book, or use the "Search All" / "Search Selected" buttons on the Wanted pages.'
        ],
        warning: 'There is no scheduled automatic search for missing books. This is by design in the *arr ecosystem to avoid hammering indexers. Use "Search on Add" or manual/mass search on the Wanted pages.'
      },
      {
        heading: 'Understanding Rejections',
        paragraphs: [
          'When a search or RSS sync finds releases but doesn\'t grab them, check the log for rejection reasons:',
        ],
        list: [
          'Unknown Author — Release is for an author not in your library.',
          'Quality not wanted — Release quality doesn\'t match your profile.',
          'Cutoff already met — You already have a file at or above the cutoff quality.',
          'Not monitored — The book or format isn\'t currently monitored.'
        ]
      }
    ]
  },
  {
    id: 'activity',
    title: 'Activity & History',
    icon: 'ACTIVITY',
    description: 'Queue, history, and blocklist management.',
    content: [
      {
        heading: 'Queue',
        paragraphs: [
          'The Queue shows active downloads. Each entry shows the book being downloaded, the download client, progress, and estimated time remaining.',
          'You can remove items from the queue, blocklist them (preventing re-download), or change priority.'
        ]
      },
      {
        heading: 'History',
        paragraphs: [
          'History records all actions Bibliophilarr has taken: grabs (sent to download client), imports, renames, retags, and deletions.',
          'The Format column shows whether each event involved an ebook (E) or audiobook (A) based on the quality of the file.'
        ]
      },
      {
        heading: 'Blocklist',
        paragraphs: [
          'The Blocklist contains releases that have been explicitly blocked — either manually or because the download failed. Blocked releases will not be re-downloaded.',
          'You can clear individual blocklist entries or the entire blocklist if needed.'
        ]
      }
    ]
  },
  {
    id: 'media-management',
    title: 'Media Management',
    icon: 'ORGANIZE',
    description: 'File naming, root folders, and organization settings.',
    content: [
      {
        heading: 'Root Folders',
        paragraphs: [
          'Root folders are the top-level directories where Bibliophilarr stores your book library. Each author gets a subfolder within the root folder.',
          'You can have multiple root folders (e.g., separate folders for ebooks and audiobooks, or for different storage drives).'
        ]
      },
      {
        heading: 'File Naming',
        paragraphs: [
          'Configure how Bibliophilarr names files during import and rename operations. Go to Settings → Media Management → Naming.',
          'Tokens available include: {Author Name}, {Book Title}, {Quality}, {Release Group}, and many more. Use the naming preview to see how your pattern will look.'
        ]
      },
      {
        heading: 'Import Settings',
        paragraphs: [
          'Control how Bibliophilarr handles completed downloads:',
        ],
        list: [
          'Use Hardlinks instead of Copy — Saves disk space when download client and library are on the same filesystem.',
          'Import Extra Files — Import additional files (e.g., cover images, metadata files) alongside the book file.',
          'Delete Empty Folders — Clean up empty author/book folders after moving or deleting files.'
        ]
      }
    ]
  },
  {
    id: 'notifications',
    title: 'Notifications',
    icon: 'BELL',
    description: 'Setting up notifications and connections.',
    content: [
      {
        heading: 'Connections',
        paragraphs: [
          'Bibliophilarr can notify you of events via various connection types. Go to Settings → Connect to add notification connections.',
          'Supported notification types include:'
        ],
        list: [
          'Email — Send notifications via SMTP.',
          'Discord — Post messages to a Discord webhook.',
          'Telegram — Send messages via Telegram bot.',
          'Pushover — Push notifications to mobile devices.',
          'Webhook — Send JSON payloads to custom endpoints.',
          'And many more (Slack, Gotify, Ntfy, Apprise, etc.).'
        ]
      },
      {
        heading: 'Notification Events',
        paragraphs: [
          'You can configure which events trigger each notification:',
        ],
        list: [
          'On Grab — When a release is sent to the download client.',
          'On Import / On Upgrade — When a file is imported or upgraded.',
          'On Rename — When files are renamed.',
          'On Author Added / Deleted — When library entries change.',
          'On Book Retag — When book file tags are updated.',
          'On Health Issue / Restored — When system health status changes.',
          'On Application Update — When Bibliophilarr updates itself.'
        ]
      }
    ]
  },
  {
    id: 'system',
    title: 'System Administration',
    icon: 'SYSTEM',
    description: 'Status, tasks, backups, updates, and logs.',
    content: [
      {
        heading: 'Status',
        paragraphs: [
          'The Status page shows system health checks, disk space, and application information. Health checks alert you to configuration problems (e.g., no indexers, missing root folders, update availability).'
        ]
      },
      {
        heading: 'Tasks',
        paragraphs: [
          'View and manage scheduled and queued tasks. Scheduled tasks include RSS Sync, Refresh Author, Health Check, Housekeeping, and others.',
          'You can manually trigger any scheduled task by clicking its run button.'
        ]
      },
      {
        heading: 'Backups',
        paragraphs: [
          'Bibliophilarr automatically creates weekly backups of its database and configuration. You can also trigger manual backups.',
          'Backups are stored in the application data directory. You can download or restore from any backup.'
        ]
      },
      {
        heading: 'Updates',
        paragraphs: [
          'Check for and install application updates. The update mechanism, branch, and script settings are configurable under Settings → General → Updates.'
        ]
      },
      {
        heading: 'Events & Logs',
        paragraphs: [
          'Events show application-level log entries (info, warnings, errors). Use the log level filter to focus on specific severity levels.',
          'Log Files provides access to the full log files on disk for detailed troubleshooting.'
        ]
      }
    ]
  },
  {
    id: 'troubleshooting',
    title: 'Troubleshooting',
    icon: 'BUG',
    description: 'Common problems, log analysis, and solutions.',
    content: [
      {
        heading: 'Common Issues',
        paragraphs: [
          'Below are frequently encountered issues and their solutions.'
        ]
      },
      {
        heading: 'No Books Being Downloaded',
        paragraphs: [
          'If monitored books aren\'t being grabbed, check these in order:',
        ],
        list: [
          'Verify indexers are configured and enabled for RSS/Auto search (Settings → Indexers).',
          'Verify at least one download client is configured and enabled (Settings → Download Clients).',
          'Check System → Status for health warnings.',
          'Check Activity → History for grab events — if none exist, no matching releases have been found.',
          'Check the logs for "Unknown Author" rejections — this means the indexer releases don\'t match any author in your library.',
          'Try a manual search from the book detail page or Wanted → Missing to actively query indexers.'
        ],
        note: 'RSS sync only matches NEW releases from indexers to your library. It does not search for old/backlog releases. Use manual or mass search for missing books.'
      },
      {
        heading: 'Downloads Stuck in Queue',
        paragraphs: [
          'If downloads appear stuck:',
        ],
        list: [
          'Verify the download client is responding (check its web UI).',
          'Check for Remote Path Mapping issues if using Docker or remote clients.',
          'Look for import warnings in Activity → Queue — hover over the status icon for details.',
          'Ensure the completed download path is accessible to Bibliophilarr.'
        ]
      },
      {
        heading: 'Files Not Importing',
        paragraphs: [
          'Completed downloads not being imported:',
        ],
        list: [
          'Check the download folder permissions — Bibliophilarr needs read access.',
          'Verify the root folder exists and is writable.',
          'Check if the book was already imported (duplicate detection).',
          'Review the logs for specific import error messages.'
        ]
      },
      {
        heading: 'Getting Help',
        paragraphs: [
          'If you can\'t resolve an issue:',
        ],
        list: [
          'Enable Trace logging temporarily (Settings → General → Log Level) to capture detailed information.',
          'Check System → Events for recent errors.',
          'Download log files from System → Log Files for analysis.',
          'Visit the GitHub Discussions page to ask for community support.'
        ]
      }
    ]
  },
  {
    id: 'faq',
    title: 'FAQ',
    icon: 'TBA',
    description: 'Frequently asked questions and quick answers.',
    content: [
      {
        heading: 'Frequently Asked Questions'
      },
      {
        heading: 'How does Bibliophilarr find books?',
        paragraphs: [
          'Bibliophilarr queries your configured indexers (Usenet or Torrent) for releases matching your monitored library. It uses RSS polling for new releases and active searching for specific missing books.'
        ]
      },
      {
        heading: 'Why aren\'t my missing books being downloaded automatically?',
        paragraphs: [
          'RSS sync only matches brand-new indexer releases to your library. For existing/older releases, you need to trigger a manual search from the Wanted → Missing page using the search buttons.',
          'Also ensure you have indexers and download clients properly configured.'
        ]
      },
      {
        heading: 'What is the difference between ebook and audiobook quality IDs?',
        paragraphs: [
          'Quality IDs 0-4 are ebook formats (Unknown, PDF, MOBI, EPUB, AZW3). Quality IDs 10-13 are audiobook formats (MP3, FLAC, M4B, Unknown Audio). The format type is derived from the quality ID.'
        ]
      },
      {
        heading: 'Can I monitor both ebook and audiobook for the same book?',
        paragraphs: [
          'Yes. With Dual Format Tracking enabled (the default), each book can have both ebook and audiobook formats monitored independently with separate quality profiles.'
        ]
      },
      {
        heading: 'How do I force a search for all missing books?',
        paragraphs: [
          'Go to Wanted → Missing, select all books using the checkbox in the header, then click "Search Selected". Alternatively, you can search per-author from the author detail page.'
        ]
      },
      {
        heading: 'What are Custom Formats?',
        paragraphs: [
          'Custom Formats are user-defined rules that score releases based on criteria like release group name, quality, file size, or other attributes. They help automate choosing the best release when multiple options are available.'
        ]
      },
      {
        heading: 'How do I use Bibliophilarr with Docker?',
        paragraphs: [
          'Map your book library, download directory, and application data directory as Docker volumes. Ensure file permissions are correct and paths match between Bibliophilarr and your download client. Use Remote Path Mappings if paths differ.'
        ]
      },
      {
        heading: 'Where is the configuration stored?',
        paragraphs: [
          'On Linux, the default data directory is ~/.config/Bibliophilarr/. This contains the database (bibliophilarr.db), configuration, logs, and backups.'
        ]
      },
      {
        heading: 'How do mapped network drives work?',
        paragraphs: [
          'Mapped network drives are not available when running as a Windows Service. Use UNC paths (\\\\server\\share) instead, or run Bibliophilarr as a tray application.'
        ]
      }
    ]
  },
  {
    id: 'custom-formats',
    title: 'Custom Formats',
    icon: 'INTERACTIVE',
    description: 'Creating and managing custom format scoring rules.',
    content: [
      {
        heading: 'Custom Formats Overview',
        paragraphs: [
          'Custom Formats allow you to create rules that score releases based on various criteria. Releases with higher custom format scores are preferred when multiple options are available for the same book.'
        ]
      },
      {
        heading: 'Creating a Custom Format',
        paragraphs: [
          'Go to Settings → Custom Formats → Add (+). Give it a name, then add one or more specifications (conditions):',
        ],
        list: [
          'Release Title — Match against the release name using regex.',
          'Quality — Match a specific quality level.',
          'Size — Match file size range.',
          'Release Group — Match a specific release group.',
          'Language — Match a specific language.',
          'Indexer Flag — Match indexer-specific flags.'
        ]
      },
      {
        heading: 'Scoring',
        paragraphs: [
          'Assign a score to each custom format in your quality profile. Positive scores prefer matching releases, negative scores penalize them. A score of 0 means the format is tracked but doesn\'t influence selection.',
          'The total custom format score of a release is the sum of all matching custom format scores.'
        ]
      }
    ]
  }
];

export default wikiPages;
