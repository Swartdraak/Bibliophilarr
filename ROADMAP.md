# Bibliophilarr Roadmap

**Last Updated**: March 16, 2026

This document provides a high-level overview of the Bibliophilarr project roadmap. For detailed technical specifications, see [MIGRATION_PLAN.md](MIGRATION_PLAN.md).

---

## Vision

**Bibliophilarr aims to be a sustainable, community-driven ebook and audiobook manager powered entirely by Free and Open Source Software (FOSS) metadata providers.**

---

## Current Status: Phase 5 Consolidation + Phase 6 Hardening

The project has moved beyond foundation planning into operational hardening and rollout readiness. The immediate focus is provider-consolidation stability, deterministic CI gates, and packaging/release validation.

### ✅ Completed
- Phase 1 documentation foundation
- Phase 2 provider interfaces, registry, telemetry, and persistence
- Phase 3/4 Open Library + fallback provider runtime integration slices
- Phase 5 telemetry endpoints and replay/ops automation slices
- Required-check emission hardening on develop with deterministic branch-protection operations

### 🔄 In Progress
- Phase 5 provider-consolidation stabilization in active rollout lanes
- Phase 6 packaging matrix hardening (binary/docker/npm)
- Branch policy audit and release-readiness operational automation

### 📋 Next Steps
- Expand protected-branch parity (develop/staging/main) with policy-as-code
- Continue scheduled packaging validation and drift triage
- Reduce dependency-security reporting lag through lockfile-vs-alert triage automation

---

## Roadmap Phases

### Phase 1: Foundation & Documentation ✅
**Timeline**: Weeks 1-4  
**Status**: ✅ Complete (100%)

**Goals:**
- ✅ Analyze existing codebase and architecture
- ✅ Research and evaluate FOSS metadata alternatives
- ✅ Create comprehensive migration plan
- ✅ Update all documentation to reflect active development
- 🔄 Engage community and recruit contributors (ongoing)

**Key Deliverables:**
- ✅ MIGRATION_PLAN.md
- ✅ Updated README.md
- ✅ Updated CONTRIBUTING.md
- ✅ ROADMAP.md (this document)
- ✅ PROJECT_STATUS.md
- ✅ PROVIDER_IMPLEMENTATION_GUIDE.md

---

### Phase 2: Infrastructure Setup 🔄
**Timeline**: Weeks 5-8  
**Status**: 🔄 In Progress (90% Complete)

**Goals:**
- ✅ Design and implement multi-provider architecture
- ✅ Create provider interfaces and abstractions
- ✅ Build metadata quality scoring system
- ✅ Set up comprehensive testing framework (11 integration + 6 unit tests)
- ✅ Implement monitoring and logging (ProviderTelemetryService)

**Key Deliverables:**
- ✅ Provider interface hierarchy (`IMetadataProvider` + `*V2` capability interfaces) - 11 files created
- ✅ Quality scoring algorithms - MetadataQualityScorer implemented
- ✅ PROVIDER_IMPLEMENTATION_GUIDE.md - Comprehensive guide created
- ✅ Provider registry and management system
- ✅ Testing framework for providers (17 tests: integration + unit)
- ✅ Logging and monitoring infrastructure (ProviderTelemetryService + structured NLog)
- ✅ Provider settings persistence (SQLite, migration 041)

**Success Criteria:**
- ✅ All interfaces defined and documented
- ✅ Testing framework operational (17 tests across registry, quality scorer)
- ✅ Provider registry can dynamically load providers

---

### Phase 3: Open Library Provider ✅
**Timeline**: Weeks 9-14  
**Status**: ✅ Delivered in slices

**Goals:**
- Complete Open Library API integration
- Implement all search and retrieval functions
- Handle rate limiting and caching
- Map Open Library data to Bibliophilarr models
- Comprehensive testing

**Key Deliverables:**
- Full Open Library provider implementation
- Search by title, author, ISBN, ASIN
- Author information retrieval
- Cover image handling
- 90%+ test coverage
- Performance benchmarks

**Success Criteria:**
- All metadata operations functional via Open Library
- Performance meets or exceeds Goodreads
- Comprehensive test suite passes
- Documentation complete

---

### Phase 4: Multi-Provider Support ✅
**Timeline**: Weeks 15-18  
**Status**: ✅ Delivered in slices

**Goals:**
- Implement Inventaire provider as secondary source
- Build provider aggregation layer
- Create fallback and redundancy logic
- Implement metadata merging from multiple sources
- Add Google Books as tertiary fallback

**Key Deliverables:**
- Inventaire provider implementation
- Metadata aggregation service
- Intelligent fallback logic
- Provider health monitoring
- Provider selection UI

**Success Criteria:**
- Multiple providers working in harmony
- Automatic fallback on provider failure
- Improved metadata quality from aggregation
- Users can select preferred providers

---

### Phase 5: Consolidation and Rollout Controls
**Timeline**: Weeks 19-22  
**Status**: 🔄 In Progress

**Goals:**
- Update database schema for multi-provider IDs
- Implement ID mapping system
- Create migration scripts
- Build Goodreads → ISBN → Open Library mapping
- Ensure backward compatibility

**Key Deliverables:**
- Updated database schema
- Migration scripts and tools
- ID mapping database
- Rollback procedures
- Migration testing with various library sizes

**Success Criteria:**
- Existing libraries can be migrated without data loss
- New identifier system is robust and extensible
- Backward compatibility maintained
- Rollback works if needed

---

### Phase 6: Packaging and Operational Hardening
**Timeline**: Weeks 23-26  
**Status**: 🔄 In Progress

**Goals:**
- Build user-friendly migration tools
- Create migration progress UI
- Implement metadata conflict resolution
- Add manual override capabilities
- Comprehensive user documentation

**Key Deliverables:**
- Automated migration tool
- Migration progress dashboard
- Conflict resolution UI
- Manual metadata entry/override
- User migration guide
- Video tutorials

**Success Criteria:**
- Average user can migrate library with minimal effort
- Migration progress is clear and trackable
- Edge cases have manual resolution options
- Documentation is comprehensive and clear

---

### Phase 7: Beta Release
**Timeline**: Weeks 27-30  
**Status**: ⏳ Not Started

**Goals:**
- Release beta version to community
- Gather feedback and usage data
- Fix bugs and address issues
- Performance tuning
- Expand documentation

**Key Deliverables:**
- Beta release announcement
- Bug tracking and resolution
- Performance improvements
- Updated documentation based on feedback
- Community support channels

**Success Criteria:**
- No critical bugs in beta
- Positive community feedback
- Successful migration stories
- Performance acceptable for production

---

### Phase 8: Stable Release (v1.0)
**Timeline**: Weeks 31-34  
**Status**: ⏳ Not Started

**Goals:**
- Final testing and QA
- Production-ready release
- Official deprecation of Goodreads
- Launch documentation and marketing
- Celebrate success! 🎉

**Key Deliverables:**
- Stable v1.0 release
- Complete documentation
- Release announcement
- Migration from Goodreads complete
- Community celebration

**Success Criteria:**
- Stable, production-ready release
- All critical functionality working
- Documentation complete
- Community adoption growing
- Sustainable future established

---

## Long-Term Vision (Post v1.0)

### Future Enhancements
- **BookBrainz Integration**: When mature, add MusicBrainz's book database
- **Local Metadata Mirror**: Option to host local Open Library mirror
- **Community Metadata**: Allow users to contribute metadata improvements
- **Advanced Matching**: ML-based book identification
- **Enhanced Series Support**: Better series and collection management
- **Mobile App**: Companion mobile application
- **Plugin System**: Allow third-party metadata providers

### Maintenance & Growth
- **Regular Updates**: Keep dependencies current
- **Community Growth**: Build contributor base
- **Documentation**: Maintain comprehensive docs
- **Support**: Provide excellent user support
- **Innovation**: Continue improving features

---

## How to Help

We need contributors in several areas:

### 🔨 Development
- **Backend (C#)**: Provider implementations, API integration
- **Frontend (React)**: UI for settings, migration tools
- **Testing**: Write tests, test with real libraries

### 📝 Documentation  
- **User Guides**: Help users understand new features
- **API Docs**: Document provider interfaces
- **Tutorials**: Create video and written tutorials

### 🧪 Testing
- **Beta Testing**: Test with your library
- **Bug Reports**: Report issues clearly
- **Edge Cases**: Test unusual scenarios

### 💬 Community
- **Support**: Help other users
- **Feedback**: Share your experience
- **Advocacy**: Spread the word

**See [CONTRIBUTING.md](CONTRIBUTING.md) for details on how to contribute.**

---

## Stay Updated

- **GitHub**: Watch this repository for updates
- **Discussions**: Join [GitHub Discussions](https://github.com/Swartdraak/Bibliophilarr/discussions)
- **Discord**: Join our community (link coming soon)
- **Blog**: Release announcements and progress updates (planned)

---

## Milestones

| Milestone | Target | Status |
|-----------|--------|--------|
| Phase 1: Foundation Complete | Week 4 | ✅ Complete |
| Phase 2: Infrastructure Ready | Week 8 | 🔄 In Progress (90%) |
| Phase 3: Open Library Working | Week 14 | ⏳ Not Started |
| Phase 4: Multi-Provider Live | Week 18 | ⏳ Not Started |
| Phase 5: Migration Tools Done | Week 22 | ⏳ Not Started |
| Phase 6: UX Complete | Week 26 | ⏳ Not Started |
| Phase 7: Beta Release | Week 30 | ⏳ Not Started |
| Phase 8: v1.0 Stable Release | Week 34 | ⏳ Not Started |

---

## Contact

- **Issues**: [GitHub Issues](https://github.com/Swartdraak/Bibliophilarr/issues)
- **Discussions**: [GitHub Discussions](https://github.com/Swartdraak/Bibliophilarr/discussions)
- **Email**: Coming soon

---

*This roadmap is a living document and will be updated as the project progresses. Dates are estimates and may change based on community contributions and unforeseen challenges.*

**Last Updated**: February 16, 2024  
**Version**: 1.0
