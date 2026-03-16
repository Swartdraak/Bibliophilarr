# Project Status Summary

**Last Updated**: March 16, 2026  
**Project**: Bibliophilarr (formerly Bibliophilarr)  
**Current Phase**: Phase 2 - Infrastructure Setup (90% Complete)

---

## Overview

Bibliophilarr is a community-driven project focused on sustainable metadata and reliable automation. We are migrating from proprietary Goodreads metadata to Free and Open Source Software (FOSS) alternatives.

## What Has Been Done ✅

### Phase 1: Documentation - COMPLETE ✅
- ✅ **README.md**: Updated to reflect active development status
  - Announces project revival and community focus
  - Explains FOSS metadata migration
  - Removes references to retirement
  - Updates support and contribution information

- ✅ **MIGRATION_PLAN.md**: Comprehensive 26,000+ word technical plan
  - Current architecture analysis
  - FOSS provider research and comparison
  - Multi-provider architecture design
  - 10-phase implementation plan
  - Database migration strategy
  - Testing strategy
  - Risk analysis and mitigation
  - Timeline and milestones

- ✅ **ROADMAP.md**: High-level project roadmap
  - 8 major phases with timelines
  - Clear milestones and success criteria
  - Long-term vision
  - Ways to contribute
  - Status tracking

- ✅ **CONTRIBUTING.md**: Updated contributor guide
  - Priority contribution areas
  - Development setup instructions
  - Code style guidelines
  - PR process
  - Community resources

- ✅ **QUICKSTART.md**: Quick start guide for new contributors
  - Essential reading list
  - Quick setup steps
  - Key directories and files
  - Learning resources
  - Next steps

- ✅ **package.json**: Updated project metadata
  - Project name changed to "bibliophilarr"
  - Repository URL updated
  - Author attribution updated

### Phase 2: Infrastructure Setup - IN PROGRESS 🔄 (90% Complete)

#### Provider Interface Hierarchy ✅
- ✅ **IMetadataProvider.cs**: Base interface for all providers
  - Common properties: ProviderName, Priority, IsEnabled
  - Capability flags for different operations
  - Rate limit and health status methods

- ✅ **ProviderRateLimitInfo.cs**: Rate limiting configuration
  - MaxRequests and TimeWindow properties
  - API key requirement flags
  - Support for authenticated vs. unauthenticated limits

- ✅ **ProviderHealthStatus.cs**: Provider health monitoring
  - Health states: Healthy, Degraded, Unhealthy, Unknown
  - Success rate and response time tracking
  - Failure tracking with error messages

- ✅ **ISearchForNewBookV2.cs**: Enhanced book search interface
  - Async support with Task-based methods
  - BookSearchOptions for configuration
  - Support for ISBN, ASIN, and custom identifiers
  - Backward-compatible synchronous methods

- ✅ **ISearchForNewAuthorV2.cs**: Enhanced author search interface
  - AuthorSearchOptions for configuration
  - Async search methods
  - Provider-specific identifier support

- ✅ **IProvideBookInfoV2.cs**: Enhanced book info retrieval
  - BookInfoOptions for configuration
  - Multiple identifier type support
  - Async methods for better performance

- ✅ **IProvideAuthorInfoV2.cs**: Enhanced author info retrieval
  - AuthorInfoOptions for configuration
  - Changed author tracking
  - Multiple identifier type support

- ✅ **IMetadataQualityScorer.cs**: Quality scoring interface
  - Score calculation for books, authors, and editions
  - Quality acceptance thresholds

- ✅ **MetadataQualityScorer.cs**: Quality scoring implementation
  - Weighted scoring system (0-100)
  - Book scoring: Essential (60pts), Important (25pts), Nice-to-have (15pts)
  - Author scoring: Essential (60pts), Important (20pts), Nice-to-have (20pts)
  - Edition scoring: Essential (60pts), Important (25pts), Nice-to-have (15pts)
  - Minimum acceptable score: 50

- ✅ **IMetadataAggregator.cs**: Metadata aggregation interface
  - AggregationStrategy: FirstAcceptable, BestQuality, Merge, PrimaryOnly
  - AggregationOptions for configuration
  - AggregatedResult<T> for tracking sources
  - Search and metadata retrieval across multiple providers
  - Metadata merging and deduplication

- ✅ **IMetadataProviderRegistry.cs**: Provider management interface
  - Provider registration and unregistration
  - Provider enable/disable functionality
  - Priority-based provider selection
  - Capability-based provider filtering
  - Health status tracking

- ✅ **MetadataProviderRegistry.cs**: Concrete registry (thread-safe, priority-ordered)
  - DI registration of `IEnumerable<IMetadataProvider>` providers
  - Enable/disable/priority override at runtime
  - `UpdateProviderHealth` for in-process telemetry updates

- ✅ **Integration tests** (11 tests): primary flow, fallback, exception handling,
  health-based skipping, capability routing, priority ordering, reorder override,
  all-disabled graceful handling

- ✅ **MetadataProviderSettings persistence**: SQLite-backed settings
  - Migration 041: `MetadataProviderSettings` table
  - `MetadataProviderSettingsRepository` (BasicRepository + FindByProviderName)
  - `MetadataProviderSettingsService`: ApplyPersistedSettings, SaveProviderEnabled,
    SaveProviderPriority

- ✅ **ProviderTelemetryService**: Structured health telemetry
  - EMA-based success rate and average response time
  - Auto-promotes health state on consecutive failures (≥2 → Degraded, ≥5 → Unhealthy)
  - Info log on health state transitions, Warn on failures, Debug on per-call metrics

### Architecture Analysis ✅
- ✅ Comprehensive codebase exploration
- ✅ Metadata provider architecture documented
- ✅ Current Goodreads dependencies identified
- ✅ Interface hierarchy mapped
- ✅ Testing infrastructure understood

### Research ✅
- ✅ **FOSS Metadata Providers Evaluated:**
  - Open Library (primary choice - 20M+ books, AGPL)
  - Inventaire.io (secondary - Wikidata-based, AGPL)
  - Google Books (fallback - comprehensive but proprietary)
  - BookBrainz (future consideration)
  
- ✅ **API Comparison Matrix Created**
- ✅ **Provider Capabilities Documented**
- ✅ **Rate Limiting Strategies Defined**

### Security Remediation ✅
- ✅ Merged focused security PR [#12](https://github.com/Swartdraak/Bibliophilarr/pull/12) into `develop`
  - Branch: `security/dependabot-8-alerts-2026-03-16`
  - Merge commit: `c5656a492`
- ✅ Merged follow-up no-override security PR [#13](https://github.com/Swartdraak/Bibliophilarr/pull/13) into `develop`
  - Branch: `security/dependabot-pass2-no-resolutions-2026-03-16`
  - Merge commit: `47cf259ee`
  - Removed `resolutions` overrides and remediated chains at source (`rimraf`, `webpack`, `postcss-url` removal)
- ✅ Replaced legacy PostCSS plugin chain using `postcss-color-function` with `@csstools/postcss-color-function`
- ✅ Regenerated `yarn.lock` and validated frontend build success after dependency updates
- ✅ Removed accidental local `.env.bak.*` secret backup from workspace and added `.gitignore` guard (`.env.bak*`) while keeping `.env` local-only and `.env.example` tracked
- 🔄 Dependabot API still reports 8 open npm alerts after PR #12 and PR #13 merges
  - Rechecked immediately and after delay via GitHub API on `develop`
  - Local lock graph now resolves at/above patched ranges:
    - `glob`: `10.5.0`
    - `immutable`: `4.3.8`
    - `minimatch`: `3.1.5`, `5.1.9`, `9.0.9`, `10.2.4`
    - `postcss`: `8.4.47`, `8.4.48`
    - `serialize-javascript`: not present
  - Next action: trigger/await GitHub dependency graph refresh and re-query alert state

## What Needs to Be Done 📋

### Current Focus: Phase 2 Infrastructure (10% Remaining)

#### Provider Registry Implementation 🔄
- [x] Create MetadataProviderRegistry service class
- [x] Implement provider priority and selection logic
- [x] Add provider enable/disable functionality
- [x] Create provider health monitoring service
- [x] Add configuration system for provider settings (MetadataProviderSettingsService + migration 041)
- [x] Implement dependency injection setup

#### Testing Framework 🔄
- [ ] Create base test class for provider implementations
- [ ] Add mock HTTP client for testing
- [x] Create test fixtures with sample metadata
- [x] Add integration test utilities (MetadataProviderRegistryIntegrationFixture — 11 tests)
- [x] Write unit tests for MetadataQualityScorer
- [ ] Write tests for aggregation logic
- [ ] Document testing patterns and examples
- [x] Write unit tests for MetadataProviderRegistry
- [x] Run random /media provider pull validation (75 files, app-style query format) and document findings
- [x] Validate live-provider enrichment against real `/media` gaps with iterative Open Library + Inventaire fallback
- [x] Add and test core query normalization service (alias expansion + title strip patterns via config)
- [x] Add full fallback-order integration coverage from primary search through tertiary provider
- [x] Add targeted tests for tertiary provider cooldown/backoff and confidence-aware title scoring
- [x] Add second in-app fallback provider (Hardcover GraphQL) with deterministic test coverage
- [x] Expose Hardcover fallback controls in metadata settings UI (enable, token, timeout override)
- [x] Add API-level integration tests for metadata config save/load round-trip (mapper + validation, 10 tests)
- [x] Add provider resilience tests for Hardcover execution path (408, 503, 429, empty-result — 4 tests)
- [x] Add Inventaire fallback provider with deterministic ordering ahead of Google Books and Hardcover
- [x] Add Open Library author-detail retrieval path (`LookupAuthorByKey`) and cover image mapping for search/ISBN flows
- [x] Add targeted fallback/cover integration tests (Inventaire, Open Library, Google Books)
- [x] Add Inventaire fallback localization keys in English resources (`EnableInventaireFallback`, help text)
- [x] Add integration-style mixed-provider cover precedence test through import candidate flow
- [x] Start metadata aggregation conflict-resolution policy slice with precedence/tie-break/observability model + unit tests
- [x] Wire metadata conflict policy into runtime aggregation execution paths (`MetadataAggregator`) with provider telemetry integration
- [x] Add runtime integration fixture for aggregator conflict selection + telemetry snapshot assertions
- [x] Expand Inventaire fallback localization keys across all non-English locale resources

#### Monitoring & Logging 🔄
- [x] Add structured logging for provider operations (ProviderTelemetryService)
- [x] Create provider performance metrics (EMA response time + success rate)
- [x] Add error tracking (consecutive failure counting + health promotion)
- [x] Create provider health check endpoints — GET /api/v1/metadata/providers/health
- [x] Add operational telemetry counters: TotalSearches, EmptyResultCount, TimeoutCount per provider
- [x] Implement rate limit tracking and warnings (window usage, near-ceiling signal, retry-after remaining)
- [x] Add tertiary fallback provider dampening using provider health, cooldowns, and rate-limit metadata
- [x] Add metadata conflict decision telemetry service (reason/provider/tie-break counters) with structured policy logs

#### Platform and Runtime ✅
- [x] Audit all backend project targets and confirm .NET 8-only targeting (`net8.0` / `net8.0-windows`)
- [x] Migrate residual script framework default from `net6.0` to `net8.0` (`docs.sh`)
- [x] Update workspace publish task to include explicit `-f net8.0`
- [x] Run full core test suite on `net8.0` and capture failing fixture set for follow-up triage

#### Real-World Ingest Validation ✅
- [x] Full scan and iterative organization completed for `/media/audiobooks` and `/media/ebooks`
- [x] Embedded audiobook tag extraction validated with `ffprobe` and `mutagen` fallback support
- [x] Added alias normalization and subtitle/series stripping to live enrichment workflow
- [x] Confirmed final organizer convergence with `0` remaining proposed actions after provider-assisted remediation
- [x] Promoted ingest hardening into production code: tertiary Google Books fallback, reusable normalization config, and ffprobe import fallback path
- [x] Exposed normalization controls in the metadata settings UI with API-side validation
- [x] Reduced false-positive import matches with format-aware embedded tag confidence weighting
- [x] Quarantined unresolved media under root-local excluded paths (`/media/audiobooks/_dupes/unidentified`, `/media/ebooks/_dupes/unidentified`)
- [x] Revalidated active libraries with post-quarantine `0` organization actions and `0` enrichment targets

### Phase 1 Remaining Tasks
- [ ] Community engagement and recruitment
- [ ] Set up Discord or communication channel
- [ ] Create GitHub project board for task tracking
- [ ] Set up continuous integration for documentation

### Phase 2: Infrastructure (Weeks 5-8)
- [x] Design provider interface v2
- [x] Implement provider registry
- [x] Build metadata quality scorer
- [x] Create testing framework (integration fixtures)
- [x] Set up monitoring/logging (ProviderTelemetryService)

### Phase 3: Open Library Provider (Weeks 9-14)
- [x] Implement Open Library API client
- [x] Map Open Library data to Bibliophilarr models (search result mapping)
- [x] Search functionality (primary search path in BookInfoProxy)
- [x] ISBN/ASIN lookup
- [x] Author information retrieval
- [x] Cover image handling
- [x] Rate limiting
- [x] Comprehensive testing

### Subsequent Phases
See [ROADMAP.md](ROADMAP.md) for complete phase breakdown.

## Key Decisions Made

### Architecture
1. **Multi-provider approach** with fallback and aggregation
2. **Open Library as primary provider** due to size, license, and features
3. **Inventaire as secondary** for additional coverage
4. **Google Books as tertiary fallback** for critical gaps
5. **ISBN as primary external identifier** (more universal than provider-specific IDs)

### Database
1. **Extend existing schema** rather than complete rewrite
2. **Add multiple identifier columns** for each provider
3. **Maintain Goodreads IDs** for backward compatibility during migration
4. **Create mapping table** for ID resolution

### Migration Strategy
1. **Gradual migration** - not forced on users immediately
2. **Multiple ID mapping strategies** (ISBN from files, title/author matching, etc.)
3. **User control** - allow manual overrides and provider selection
4. **Backward compatibility** - support existing Goodreads-based libraries

### Quality Assurance
1. **Metadata quality scoring** to compare provider results
2. **Multi-provider aggregation** for best possible metadata
3. **User reporting tools** for metadata issues
4. **Community contribution** pathways

## Current Challenges

### Technical
- Rate limiting with Open Library (100 req/5min default)
- ISBN mapping for existing Goodreads-based libraries
- Handling books without ISBNs
- Series information (less robust in Open Library)
- Metadata quality variance

### Community
- Need contributors, especially C# developers
- Need beta testers with various library sizes
- Documentation needs ongoing maintenance
- Community communication channels needed

### Timeline
- Ambitious 30+ week timeline
- Dependent on volunteer contributions
- May need adjustment based on resources

## Success Metrics

### Phase 1 (Current) ✅
- [x] Comprehensive documentation created
- [x] Architecture understood
- [x] FOSS providers researched
- [x] Implementation plan defined

### Phase 2: Infrastructure Setup (In Progress) 🔄
- [ ] Provider interfaces fully documented with examples
- [ ] Testing framework operational
- [ ] Quality scoring functional with tests
- [ ] Can load and manage multiple providers (partial completion)

#### Completed ✅
- [x] Provider interfaces implemented (11 files)
- [x] Quality scoring algorithm implemented
- [x] Aggregation strategy defined
- [x] Health monitoring system designed
- [x] Provider registry implementation added
- [x] Unit tests added for provider registry and quality scorer
- [x] Full solution build validated in Debug/Posix

### Latest Validation (March 15, 2026)
- `dotnet msbuild -restore /opt/Bibliophilarr/src/Bibliophilarr.sln -p:GenerateFullPaths=true -p:Configuration=Debug -p:Platform=Posix -consoleloggerparameters:NoSummary;ForceNoAlign` -> Passed
- `dotnet test /opt/Bibliophilarr/src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --configuration Debug --filter "FullyQualifiedName~MetadataProviderRegistryFixture|FullyQualifiedName~MetadataQualityScorerFixture"` -> Passed (13/13)
- Analyzer stabilization completed for `SA1200` and `SA1000` issues introduced by broad rename cleanup

### Latest Validation (March 16, 2026)
- `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter "FullyQualifiedName~MetadataSource.CandidateServiceFallbackOrderingIntegrationFixture|FullyQualifiedName~MetadataSource.InventaireFallbackSearchProviderFixture|FullyQualifiedName~MetadataSource.OpenLibrary.OpenLibrarySearchProxyFixture|FullyQualifiedName~MetadataSource.BookInfo.BookInfoProxyOpenLibraryFixture|FullyQualifiedName~MetadataSource.GoogleBooksFallbackSearchProviderFixture"` -> Passed (11/11)
- `dotnet test src/NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj --filter "FullyQualifiedName~Config.MetadataProviderConfigFixture"` -> Passed (12/12)

#### In Progress ⏳
- [ ] Provider registry implementation
- [ ] Testing framework creation
- [ ] Unit tests for quality scorer
- [ ] Integration test utilities
- [ ] Testing framework operational
- [ ] Quality scoring functional
- [ ] Can load and manage multiple providers

### Phase 3
- [ ] Open Library provider fully functional
- [ ] Performance acceptable (< 1s for searches)
- [ ] 90%+ test coverage
- [ ] Can replace Goodreads for basic operations

### Final Success (v1.0)
- [ ] Multiple FOSS providers working
- [ ] User libraries successfully migrated
- [ ] Better metadata quality than Goodreads
- [ ] Active community maintaining project
- [ ] No dependency on proprietary services

## Resources

### Documentation
- [README.md](README.md) - Project overview
- [MIGRATION_PLAN.md](MIGRATION_PLAN.md) - Detailed technical plan
- [ROADMAP.md](ROADMAP.md) - High-level roadmap
- [CONTRIBUTING.md](CONTRIBUTING.md) - Contribution guide
- [QUICKSTART.md](QUICKSTART.md) - Quick start for contributors

### External Resources
- [Open Library API](https://openlibrary.org/developers/api)
- [Inventaire API](https://api.inventaire.io/)

### Repository
- **GitHub**: https://github.com/Swartdraak/Bibliophilarr
- **Issues**: https://github.com/Swartdraak/Bibliophilarr/issues
- **Discussions**: https://github.com/Swartdraak/Bibliophilarr/discussions

## How to Help

We need:
1. **Developers** (C#, TypeScript/React) - Implement providers
2. **Testers** - Test with real libraries
3. **Writers** - Improve documentation
4. **Users** - Provide feedback and requirements
5. **Advocates** - Spread the word

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to get started.

---

## Summary

**Bibliophilarr Phase 1 is complete. Phase 2 is 90% complete.** We have:
- ✅ Clear understanding of the current architecture
- ✅ Comprehensive technical plan for migration
- ✅ Research on FOSS alternatives
- ✅ Complete documentation for contributors
- ✅ Provider interface hierarchy (11 files created)
- ✅ Metadata quality scoring system implemented
- ✅ Provider registry (thread-safe, priority-ordered, health-aware)
- ✅ Integration test suite (17 tests: 6 unit + 11 integration)
- ✅ Provider settings persistence (Migration 041 + service layer)
- ✅ Provider health telemetry (EMA metrics + structured logging)
- ⏳ Provider health check API endpoints (Phase 3)

**Current Focus**: Finalize Phase 2 (API endpoints optional), begin Phase 3 Open Library provider

**Next major milestone**: Open Library provider (`ISearchForNewBookV2`) with full fallback chain

**Project Health**: 🟢 Healthy - Infrastructure complete, ready for Phase 3 provider development

---

**Questions?** Open a discussion on GitHub or check the documentation!
