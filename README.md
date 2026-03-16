# Bibliophilarr - Sustainable Book Library Automation

## 🎉 Announcement: Project Revival & Active Development

**Bibliophilarr** is a community-driven continuation inspired by the original Bibliophilarr effort, focused on sustainable metadata and long-term maintainability. We are actively improving the project by migrating away from proprietary metadata sources to fully open-source alternatives.

### What's Different?

- **Active Development**: This fork is under active development with a clear roadmap
- **FOSS Metadata**: Transitioning to Free and Open Source Software (FOSS) metadata providers
- **Community-Driven**: Open to contributions and community input
- **Sustainable Future**: Building on reliable, open infrastructure

### Current Status: Planning & Architecture Phase

We are currently developing a comprehensive migration plan to replace Goodreads with sustainable FOSS alternatives including:

- **Open Library** (primary provider)
- **Inventaire.io** (secondary provider)
- **Additional fallback providers** for robustness

📋 See [MIGRATION_PLAN.md](MIGRATION_PLAN.md) for detailed technical scaffold and roadmap.

### Project Lineage

Bibliophilarr acknowledges and respects the original Bibliophilarr contributors. This project now follows its own roadmap, automation, and release process under the Bibliophilarr repository.

---

# Bibliophilarr

[![Backend CI](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml/badge.svg)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-backend.yml)
[![Frontend CI](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml/badge.svg)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/ci-frontend.yml)
[![Release](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/release.yml/badge.svg)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/release.yml)
[![Docker Image](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docker-image.yml/badge.svg)](https://github.com/Swartdraak/Bibliophilarr/actions/workflows/docker-image.yml)

### ⚠️ Bibliophilarr is under active development and currently in a transitional state. We are working on migrating to FOSS metadata providers

Bibliophilarr is an ebook and audiobook collection manager for Usenet and BitTorrent users. It can monitor multiple RSS feeds for new books from your favorite authors and will grab, sort, and rename them.

**Note**: Only one type of a given book is supported per instance. If you want both an audiobook and ebook of a given book, you will need multiple instances.

## What We're Working On

- 🔄 **Metadata Migration**: Transitioning from Goodreads to FOSS providers (Open Library, Inventaire)
- 🏗️ **Infrastructure**: Building a robust multi-provider metadata architecture
- 🧪 **Testing**: Comprehensive testing framework for metadata providers
- 📚 **Documentation**: Updated guides for the new metadata system

## Major Features Include

- Can watch for better quality of the ebooks and audiobooks you have and do an automatic upgrade. *e.g. from PDF to AZW3*
- Support for major platforms: Windows, Linux, macOS, Raspberry Pi, etc.
- Automatically detects new books
- Can scan your existing library and download any missing books
- Automatic failed download handling will try another release if one fails
- Manual search so you can pick any release or to see why a release was not downloaded automatically
- Advanced customization for profiles, such that Bibliophilarr will always download the copy you want
- Fully configurable book renaming
- SABnzbd, NZBGet, QBittorrent, Deluge, rTorrent, Transmission, uTorrent, and other download clients are supported and integrated
- Full integration with Calibre (add to library, conversion) (Requires Calibre Content Server)
- And a beautiful UI

## Project Operations

To support the revival effort, we now maintain repository operation blueprints:

- [MCP Server Recommendations](docs/operations/MCP_SERVER_RECOMMENDATIONS.md)
- [Repository Tags and Labels](docs/operations/REPOSITORY_TAGS.md)
- [GitHub Projects Blueprint](docs/operations/GITHUB_PROJECTS_BLUEPRINT.md)
- [Bibliophilarr Delivery Board](https://github.com/users/Swartdraak/projects/1)
- [.NET Modernization Project](docs/operations/DOTNET_MODERNIZATION.md) - **HIGH PRIORITY**
- [Phase 6 Packaging Validation Matrix](docs/operations/phase6-packaging-validation-matrix-2026-03-16.md)
- [Conflict Strategy Staged Rollout Checklist](docs/operations/conflict-strategy-staged-rollout-checklist-2026-03-16.md)
- [Branch Strategy](docs/operations/BRANCH_STRATEGY.md)
- [Release Automation](docs/operations/RELEASE_AUTOMATION.md)
- [Wiki Home](wiki/Home.md)

## Support & Community

**Note**: This is a community fork. For support:

- **GitHub Issues**: Bug reports and feature requests for Bibliophilarr
- **Discord**: Community discussion and support (coming soon)
- **Wiki**: Documentation (being updated for new metadata system)

For historical context from the original project, refer to archived external documentation as needed.

## Contributors & Developers

This is a community project building on years of open-source ecosystem learnings.

### Contributing to Bibliophilarr

We welcome contributions! Areas where we especially need help:

- **Metadata Provider Implementation**: Help implement Open Library and other FOSS providers
- **Testing**: Test metadata retrieval and edge cases
- **Documentation**: Update docs for the new metadata system
- **UI/UX**: Improve the user experience for provider selection and configuration

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines and [MIGRATION_PLAN.md](MIGRATION_PLAN.md) for the technical roadmap.

### Tribute

This project honors the people who contributed to earlier open-source book automation efforts.

### License

- [GNU GPL v3](http://www.gnu.org/licenses/gpl.html)
- Copyright 2010-2024
- Bibliophilarr maintained by the community
- Bibliophilarr fork maintained by the community
