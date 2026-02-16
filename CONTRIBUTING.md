# How to Contribute to Bibliophilarr

We're actively seeking contributors to help revive and improve Bibliophilarr! This is a community-driven fork focused on migrating away from proprietary metadata providers to sustainable FOSS alternatives.

## 🎯 Priority Areas for Contribution

### 1. Metadata Provider Development
**Most Critical Need**

Help implement FOSS metadata providers to replace Goodreads:
- **Open Library Integration**: Primary provider implementation
- **Inventaire Integration**: Secondary provider for additional coverage
- **Provider Testing**: Comprehensive testing with real-world data
- **API Mapping**: Transform external APIs to Bibliophilarr data models

**Skills Needed**: C#, REST APIs, async/await, JSON parsing

### 2. Migration Tools
Help users transition from Goodreads to new providers:
- **ID Mapping**: Goodreads ID → ISBN → Open Library ID
- **Bulk Migration**: Tools to update existing libraries
- **Data Validation**: Verify metadata quality after migration
- **User Guides**: Document the migration process

**Skills Needed**: C#, SQL, data transformation

### 3. Testing & Quality Assurance
- **Unit Tests**: Test provider implementations
- **Integration Tests**: Test with real APIs (mocked when possible)
- **Performance Testing**: Benchmark metadata retrieval
- **User Testing**: Test with real libraries and report issues

**Skills Needed**: xUnit/NUnit, HTTP mocking, test automation

### 4. Documentation
- **User Guides**: Help users configure and use new providers
- **API Documentation**: Document provider interfaces and implementations
- **Migration Guides**: Step-by-step migration instructions
- **Troubleshooting**: Common issues and solutions

**Skills Needed**: Technical writing, markdown

### 5. User Interface
- **Provider Settings**: UI for selecting and configuring metadata providers
- **Metadata Display**: Show which provider supplied metadata
- **Migration Progress**: UI to track library migration
- **Provider Health**: Display provider status and performance

**Skills Needed**: React, TypeScript, CSS

## 📋 Current Roadmap

See [MIGRATION_PLAN.md](MIGRATION_PLAN.md) for the complete technical roadmap.

**Current Phase**: Foundation & Documentation  
**Next Phase**: Infrastructure setup for multi-provider architecture

## 🚀 Getting Started

### Development Setup

1. **Fork the Repository**
   ```bash
   git clone https://github.com/YOUR_USERNAME/Bibliophilarr.git
   cd Bibliophilarr
   ```

2. **Build the Project**
   ```bash
   # Backend (C#)
   cd src
   dotnet restore
   dotnet build
   
   # Frontend (React/TypeScript)
   cd frontend
   yarn install
   yarn build
   ```

3. **Run Tests**
   ```bash
   # Backend tests
   cd src
   dotnet test
   
   # Frontend tests
   cd frontend
   yarn test
   ```

4. **Start Development Server**
   ```bash
   ./build.sh --configuration Debug
   ```

### Making Changes

1. **Create a Feature Branch**
   ```bash
   git checkout -b feature/my-new-feature
   ```

2. **Make Your Changes**
   - Follow existing code style and conventions
   - Write tests for new functionality
   - Update documentation as needed

3. **Test Your Changes**
   ```bash
   # Run relevant tests
   dotnet test
   yarn test
   
   # Run linters
   yarn lint
   ```

4. **Commit and Push**
   ```bash
   git add .
   git commit -m "feat: descriptive commit message"
   git push origin feature/my-new-feature
   ```

5. **Open a Pull Request**
   - Provide clear description of changes
   - Reference any related issues
   - Ensure CI passes

## 💡 Contribution Guidelines

### Code Style
- **C#**: Follow existing conventions, use async/await
- **TypeScript/React**: ESLint configuration in `frontend/.eslintrc.js`
- **Commits**: Use conventional commit messages (feat:, fix:, docs:, etc.)

### Testing Requirements
- **Unit Tests**: Required for new provider implementations
- **Integration Tests**: Required for API interactions
- **Coverage**: Aim for >80% coverage on new code

### Documentation
- Update relevant docs when changing functionality
- Add XML comments to public APIs
- Keep MIGRATION_PLAN.md updated with progress

### Pull Request Process
1. Ensure all tests pass
2. Update documentation
3. Request review from maintainers
4. Address feedback
5. Squash commits if requested

## 🐛 Reporting Issues

### Bug Reports
Use the [Bug Report template](.github/ISSUE_TEMPLATE/bug_report.yml)

Include:
- Clear description of the issue
- Steps to reproduce
- Expected vs actual behavior
- Environment details (OS, version, etc.)
- Logs if available

### Feature Requests
Use the [Feature Request template](.github/ISSUE_TEMPLATE/feature_request.yml)

Include:
- Clear use case
- Proposed solution
- Alternative approaches considered
- Willingness to implement

## 🤝 Community

- **GitHub Discussions**: For questions and general discussion
- **Discord**: Real-time chat and support (coming soon)
- **Pull Requests**: Code contributions and reviews

## 📚 Additional Resources

### Original Readarr Resources
- [Servarr Wiki](https://wiki.servarr.com/readarr) (may contain outdated info)
- [Original Contributing Guide](https://wiki.servarr.com/readarr/contributing)

### Bibliophilarr-Specific Resources
- [MIGRATION_PLAN.md](MIGRATION_PLAN.md) - Technical roadmap
- [README.md](README.md) - Project overview
- [Open Library API Docs](https://openlibrary.org/developers/api)
- [Inventaire API Docs](https://api.inventaire.io/)

## 🙏 Thank You!

Every contribution helps revive this project and build a sustainable future for book management software. Whether you're fixing a typo, implementing a provider, or helping users migrate, your work is appreciated!

---

**Questions?** Open a discussion or reach out to the maintainers.

**Want to help but not sure where to start?** Look for issues tagged with `good-first-issue` or `help-wanted`.

