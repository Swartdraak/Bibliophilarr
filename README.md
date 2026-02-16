# Bibliophilarr - Reviving the Readarr Project

## 🎉 Announcement: Project Revival & Active Development

**Bibliophilarr** is a community-driven fork of the [Readarr project](<https://github.com/Readarr/Readarr>), which was retired by the original maintainers due to metadata provider issues. We are actively working to revive and improve this project by migrating away from proprietary metadata sources to fully open-source, sustainable alternatives.

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

### Original Readarr Retirement Notice
The original Readarr project was retired due to metadata provider issues. The Servarr team announced:
> "The retirement takes effect immediately... the project's metadata has become unusable, we no longer have the time to remake or repair it, and the community effort to transition to using Open Library as the source has stalled without much progress."

**We're picking up where they left off!** This fork aims to complete the Open Library migration and establish a sustainable, FOSS-based future for book and audiobook management.

---

# Bibliophilarr

[![Build Status](https://dev.azure.com/Readarr/Readarr/_apis/build/status/Readarr.Readarr?branchName=develop)](https://dev.azure.com/Readarr/Readarr/_build/latest?definitionId=1&branchName=develop)
[![Translated](https://translate.servarr.com/widgets/servarr/-/readarr/svg-badge.svg)](https://translate.servarr.com/engage/readarr/?utm_source=widget)
[![Docker Pulls](https://img.shields.io/docker/pulls/hotio/readarr)](https://wiki.servarr.com/readarr/installation#docker)
[![Donors on Open Collective](https://opencollective.com/Readarr/backers/badge.svg)](#backers)
[![Sponsors on Open Collective](https://opencollective.com/Readarr/sponsors/badge.svg)](#sponsors)
[![Mega Sponsors on Open Collective](https://opencollective.com/Readarr/megasponsors/badge.svg)](#mega-sponsors)

### ⚠️ Bibliophilarr is under active development and currently in a transitional state. We are working on migrating to FOSS metadata providers.

Bibliophilarr (formerly Readarr) is an ebook and audiobook collection manager for Usenet and BitTorrent users. It can monitor multiple RSS feeds for new books from your favorite authors and will grab, sort, and rename them.

**Note**: Only one type of a given book is supported per instance. If you want both an audiobook and ebook of a given book, you will need multiple instances.

## What We're Working On
- 🔄 **Metadata Migration**: Transitioning from Goodreads to FOSS providers (Open Library, Inventaire)
- 🏗️ **Infrastructure**: Building a robust multi-provider metadata architecture
- 🧪 **Testing**: Comprehensive testing framework for metadata providers
- 📚 **Documentation**: Updated guides for the new metadata system

## Major Features Include

* Can watch for better quality of the ebooks and audiobooks you have and do an automatic upgrade. *e.g. from PDF to AZW3*
* Support for major platforms: Windows, Linux, macOS, Raspberry Pi, etc.
* Automatically detects new books
* Can scan your existing library and download any missing books
* Automatic failed download handling will try another release if one fails
* Manual search so you can pick any release or to see why a release was not downloaded automatically
* Advanced customization for profiles, such that Readarr will always download the copy you want
* Fully configurable book renaming
* SABnzbd, NZBGet, QBittorrent, Deluge, rTorrent, Transmission, uTorrent, and other download clients are supported and integrated
* Full integration with Calibre (add to library, conversion) (Requires Calibre Content Server)
* And a beautiful UI

## Support & Community

[![Discord](https://img.shields.io/badge/discord-chat-7289DA.svg?maxAge=60)](https://discord.gg/bibliophilarr)

**Note**: This is a community fork. For support:
- **GitHub Issues**: Bug reports and feature requests for Bibliophilarr
- **Discord**: Community discussion and support
- **Wiki**: Documentation (being updated for new metadata system)

For the original Readarr project, refer to the [Servarr Wiki](https://wiki.servarr.com/readarr) (may contain outdated information).

## Contributors & Developers

This is a community fork building on the excellent work of the original Readarr team and contributors.

### Contributing to Bibliophilarr
We welcome contributions! Areas where we especially need help:
- **Metadata Provider Implementation**: Help implement Open Library and other FOSS providers
- **Testing**: Test metadata retrieval and edge cases
- **Documentation**: Update docs for the new metadata system
- **UI/UX**: Improve the user experience for provider selection and configuration

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines and [MIGRATION_PLAN.md](MIGRATION_PLAN.md) for the technical roadmap.

### Original Readarr Contributors
This project exists thanks to all the people who contributed to the original Readarr project.

### License

* [GNU GPL v3](http://www.gnu.org/licenses/gpl.html)
* Copyright 2010-2024
* Original Readarr by Servarr Team
* Bibliophilarr fork maintained by the community
