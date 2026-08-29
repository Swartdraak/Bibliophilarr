# Bibliophilarr Disposable Test Stack

Use disposable local infrastructure for behavioral validation.

- Unique Compose/project name per run.
- Bind localhost unless explicitly required.
- Never reuse production config/data/library paths.
- Fresh temporary volumes/directories per run.
- Preserve logs/traces/screenshots/reports before cleanup.
- Cleanup only resources proven to belong to the current test run.

Target capabilities: Bibliophilarr candidate; fixture/replay metadata service; mock Torznab/Newznab indexer; disposable qBittorrent or compatible client; deterministic download payload source; generated ebook/audiobook fixtures; isolated config/books/audiobooks/downloads; optional Calibre fixture.

Deterministic release gate must not require public metadata services. Live provider canaries are explicitly marked, read-only/rate-limited and reported separately.