# Quick Start Guide for Contributors

Welcome to Bibliophilarr! This guide will help you get started quickly.

## 📚 Essential Reading

Start with these documents in order:

1. **[README.md](README.md)** - Project overview and current status
2. **[ROADMAP.md](ROADMAP.md)** - High-level phases and milestones
3. **[MIGRATION_PLAN.md](MIGRATION_PLAN.md)** - Detailed technical plan
4. **[CONTRIBUTING.md](CONTRIBUTING.md)** - How to contribute

## 🎯 Current Focus

**We're in Phase 1: Foundation & Documentation**

The immediate priority is completing the planning phase and setting up infrastructure for multi-provider metadata support.

## 🚀 Quick Setup

### Prerequisites

- .NET 6.0+ SDK
- Node.js 20.x and Yarn 1.22.x
- Git

### Clone and Build

```bash
# Clone your fork
git clone https://github.com/YOUR_USERNAME/Bibliophilarr.git
cd Bibliophilarr

# Build backend
cd src
dotnet restore
dotnet build

# Build frontend
cd ../frontend
yarn install
yarn build

# Run tests
cd ../src
dotnet test
```

### Running Locally

```bash
# From repository root
./build.sh --configuration Debug

# The app will be available at http://localhost:8787
```

## 💡 Easy First Contributions

### Documentation

- Fix typos or improve clarity
- Add examples to MIGRATION_PLAN.md
- Create diagrams for architecture
- Write user guides

### Research

- Test Open Library API and document findings
- Compare metadata quality across providers
- Document edge cases in book identification
- Research ISBN mapping strategies

### Planning

- Review and provide feedback on MIGRATION_PLAN.md
- Suggest improvements to architecture
- Identify potential issues
- Propose testing strategies

## 🔍 Understanding the Codebase

### Key Directories

```
Bibliophilarr/
├── src/
│   ├── NzbDrone.Core/           # Core business logic
│   │   ├── MetadataSource/      # 🎯 Metadata providers (our focus!)
│   │   ├── Books/               # Book and author models
│   │   └── ...
│   ├── Readarr.Api.V1/          # REST API
│   └── ...
├── frontend/                     # React UI
├── MIGRATION_PLAN.md            # 📋 Detailed technical plan
├── ROADMAP.md                   # 🗺️ High-level roadmap
└── CONTRIBUTING.md              # 🤝 Contribution guide
```

### Important Files for Metadata Work

```
src/NzbDrone.Core/MetadataSource/
├── IProvideBookInfo.cs          # Book metadata interface
├── ISearchForNewBook.cs         # Book search interface
├── IProvideAuthorInfo.cs        # Author metadata interface
├── BookInfo/                    # Current metadata provider
│   └── BookInfoProxy.cs         # Main implementation
└── Goodreads/                   # Legacy provider (to be replaced)
    └── GoodreadsProxy.cs
```

## 🎓 Learning Resources

### Understanding Metadata Providers

1. **Open Library**
   - Docs: <https://openlibrary.org/developers/api>
   - Try it: <https://openlibrary.org/search.json?q=foundation+asimov>

2. **Inventaire**
   - Docs: <https://api.inventaire.io/>
   - Try it: <https://inventaire.io/api/search?types=works&search=foundation>

3. **Current Implementation**
   - Read `src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs`
   - Understand how it implements `IProvideBookInfo`, `ISearchForNewBook`, etc.

## 🤝 Getting Help

- **Questions?** Open a GitHub Discussion
- **Found a bug?** Create an issue
- **Need clarification?** Ask in pull request comments

## 📋 Next Steps

1. ✅ Read this guide
2. ✅ Read the essential documents
3. ✅ Set up your development environment
4. ✅ Build and run the project locally
5. 👉 Pick a task from [CONTRIBUTING.md](CONTRIBUTING.md)
6. 👉 Make your contribution!
7. 👉 Open a pull request

## 🎉 Welcome

Thank you for contributing to Bibliophilarr! Every contribution, no matter how small, helps revive this project and build a sustainable future for book management.

---

**Still have questions?** Don't hesitate to ask! We're here to help.
