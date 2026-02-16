# .NET Modernization Project

## Issue

The Backend CI workflow is failing due to .NET 6.0 being End of Life (EOL) and End of Support (EOS).

### Current Status

- **Current Framework**: .NET 6.0
- **EOL Date**: November 12, 2024
- **Security Status**: No longer receiving security updates
- **CI Status**: Failing with warnings about EOL framework

### CI Failure Details

From the Backend CI workflow run:

```text
warning NETSDK1138: The target framework 'net6.0' is out of support and will not receive
security updates in the future. Please refer to https://aka.ms/dotnet-core-support for more
information about the support policy.
```

Additional security vulnerability detected:

```text
error NU1902: Warning As Error: Package 'SixLabors.ImageSharp' 3.1.7 has a known moderate
severity vulnerability, https://github.com/advisories/GHSA-rxmq-m78w-7wmc
```

## Recommended Solution

### Target Framework: .NET 8.0 LTS

- **Status**: Long Term Support (LTS)
- **Support End Date**: November 10, 2026
- **Benefits**:
  - Active security updates
  - Performance improvements
  - New C# language features
  - Better container support
  - Improved JSON serialization performance

### Migration Path

#### Phase 1: Assessment (1-2 days)

1. **Inventory Dependencies**
   - Audit all NuGet packages for .NET 8 compatibility
   - Identify breaking changes in dependencies
   - Check third-party library support

2. **Review Breaking Changes**
   - Review [.NET 8 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/8.0/overview)
   - Identify code patterns that need updates
   - Document required API changes

#### Phase 2: Update Projects (2-3 days)

1. **Update Project Files**
   - Change `<TargetFrameworks>net6.0</TargetFrameworks>` to `<TargetFrameworks>net8.0</TargetFrameworks>`
   - Update global.json to specify .NET 8 SDK
   - Update Docker base images to use .NET 8 runtime

2. **Update Dependencies**
   - Update all NuGet packages to versions compatible with .NET 8
   - Address the SixLabors.ImageSharp vulnerability by updating to latest secure version
   - Test package compatibility

3. **Update CI/CD**
   - Update `.github/workflows/ci-backend.yml` to use .NET 8.0
   - Update build scripts (build.sh, etc.)
   - Update Azure Pipelines if applicable

#### Phase 3: Testing & Validation (2-3 days)

1. **Unit Tests**
   - Run full test suite
   - Fix any test failures related to framework changes
   - Verify test coverage remains consistent

2. **Integration Tests**
   - Test metadata provider integrations
   - Verify API functionality
   - Test database migrations

3. **Manual Testing**
   - Test critical user workflows
   - Verify no regressions in metadata handling
   - Test performance benchmarks

#### Phase 4: Documentation (1 day)

1. **Update Documentation**
   - Update QUICKSTART.md with new .NET version requirements
   - Update CONTRIBUTING.md build instructions
   - Update Docker documentation
   - Add migration notes to CHANGELOG

2. **Communication**
   - Notify contributors of framework change
   - Update PR reviews to check for .NET 8 compatibility
   - Update issue templates if needed

### Detailed Technical Steps

#### 1. Update .csproj Files

Find all project files:

```bash
find src -name "*.csproj" -exec sed -i 's/<TargetFrameworks>net6.0<\/TargetFrameworks>/<TargetFrameworks>net8.0<\/TargetFrameworks>/g' {} \;
```

#### 2. Create/Update global.json

```json
{
  "sdk": {
    "version": "8.0.0",
    "rollForward": "latestMinor",
    "allowPrerelease": false
  }
}
```

#### 3. Update CI Workflow

Update `.github/workflows/ci-backend.yml`:

```yaml
- name: Setup .NET
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
```

#### 4. Update Vulnerable Package

Update `SixLabors.ImageSharp` to latest secure version in project files or through NuGet:

```bash
dotnet add package SixLabors.ImageSharp --version 3.1.8  # or latest secure version
```

#### 5. Test Locally

```bash
# Install .NET 8 SDK
# https://dotnet.microsoft.com/download/dotnet/8.0

# Build the solution
cd src
dotnet restore Readarr.sln
dotnet build Readarr.sln --configuration Release

# Run tests
dotnet test Readarr.sln --configuration Release
```

### Risk Assessment

**Low Risk Items:**

- Most .NET 6 code is forward-compatible with .NET 8
- Core APIs remain stable
- Breaking changes are well-documented

**Medium Risk Items:**

- Third-party library compatibility
- Potential performance characteristic changes
- Docker image updates

**High Risk Items:**

- None identified - .NET 8 is a stable LTS release

### Estimated Effort

- **Development**: 5-7 days
- **Testing**: 2-3 days
- **Documentation**: 1 day
- **Total**: 8-11 days

### Dependencies

None - this can be done independently of other work.

### Success Criteria

1. All projects build successfully on .NET 8
2. All unit tests pass
3. All integration tests pass
4. CI/CD pipelines green
5. No security warnings for EOL framework
6. Documentation updated

### References

- [.NET Support Policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)
- [.NET 8 Release Notes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/overview)
- [Breaking Changes in .NET 8](https://learn.microsoft.com/en-us/dotnet/core/compatibility/8.0/overview)
- [Migrating to .NET 8](https://learn.microsoft.com/en-us/dotnet/core/porting/)
- [SixLabors.ImageSharp Advisory](https://github.com/advisories/GHSA-rxmq-m78w-7wmc)

## Action Items

- [ ] Create GitHub Project for tracking .NET 8 migration work
- [ ] Create individual issues for each phase
- [ ] Assign owner for migration project
- [ ] Schedule work in appropriate sprint/milestone
- [ ] Coordinate with maintainers on timing
- [ ] Plan for testing support from community

## Priority

**HIGH** - Active security concern with EOL framework and known vulnerable dependency.

This work should be prioritized before significant new feature development to ensure the codebase
remains secure and maintainable.
