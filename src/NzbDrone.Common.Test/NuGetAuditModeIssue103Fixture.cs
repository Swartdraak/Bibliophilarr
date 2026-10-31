using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Common.Test
{
    // NET10-03 (#103, epic #96 "Migration Program: .NET 8 -> .NET 10 LTS"): durable regression
    // coverage for the NuGet vulnerability-audit gate in src/Directory.Build.props.
    //
    // Context: the .NET 10 SDK enables NuGetAudit by default (audit level = high), which emits
    // NU1901-NU1904 (vulnerable-package) warnings at restore time. The repo also sets
    // TreatWarningsAsErrors=true globally, so any NuGet-audit warning is escalated to a hard
    // restore/build error that ABORTS the whole restore before a single test runs. That made a
    // single new advisory (direct OR transitive) enough to red-line the entire solution and stop
    // every PR / CI lane (the documented Oct-2026 "develop went red" trap: SixLabors/ImageSharp
    // 3.1.11 and System.Security.Cryptography.Pkcs).
    //
    // The fix under test (src/Directory.Build.props) sets <NuGetAuditMode>direct</NuGetAuditMode>:
    // the audit is narrowed to DIRECT package references. Transitive-only advisories degrade to
    // a visible, triageable warning instead of a full restore abort, while direct-reference
    // advisories still surface as NU19xx and (via TreatWarningsAsErrors) still gate the build.
    //
    // This fixture pins that contract against the actual checked-in MSBuild props so a future
    // revert - removing the NuGetAuditMode line, flipping it back to the default all-packages
    // audit, or deleting TreatWarningsAsErrors - is caught by CI. The assertions are semantic
    // (a direct reference to the literal NuGetAuditMode element) so they survive re-indentation
    // or comment reflow.
    [TestFixture]
    public class NuGetAuditModeIssue103Fixture
    {
        // The Common.Test build output (per Directory.Build.props OutputPath for Test projects)
        // is <repo-root>/_tests/net10.0/. From that output dir the repo root is two levels up
        // (verified against the actual _tests/net10.0 layout). AppContext.BaseDirectory is the
        // test assembly's own directory.
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));

        private static string DirectoryBuildPropsPath =>
            Path.Combine(RepoRoot, "src", "Directory.Build.props");

        private static string[] ReadPropsLines()
        {
            var path = DirectoryBuildPropsPath;
            File.Exists(path).Should().BeTrue($"src/Directory.Build.props must exist at {path}");
            return File.ReadAllLines(path);
        }

        [Test]
        public void directory_build_props_should_declare_nugetauditmode_direct_not_all()
        {
            var lines = ReadPropsLines();
            var directCount = 0;
            var allCount = 0;
            var defaultAllCount = 0;

            foreach (var raw in lines)
            {
                var line = raw.Trim();

                // The element must be the bare literal, no Condition attribute (the gate is
                // unconditional across every project in the solution).
                if (line.StartsWith("<NuGetAuditMode>", StringComparison.Ordinal))
                {
                    if (line.EndsWith("direct</NuGetAuditMode>", StringComparison.Ordinal))
                    {
                        directCount++;
                    }
                }

                if (line.StartsWith("<NuGetAuditMode>", StringComparison.Ordinal) &&
                    line.EndsWith("all</NuGetAuditMode>", StringComparison.Ordinal))
                {
                    allCount++;
                }

                // <NuGetAuditMode>default</NuGetAuditMode> would re-enable the full-graph audit.
                if (line.StartsWith("<NuGetAuditMode>", StringComparison.Ordinal) &&
                    line.EndsWith("default</NuGetAuditMode>", StringComparison.Ordinal))
                {
                    defaultAllCount++;
                }
            }

            directCount.Should().Be(1,
                "NuGetAuditMode must be pinned to 'direct' exactly once (the controlled gate)");
            allCount.Should().Be(0,
                "NuGetAuditMode must NOT be 'all' (the default that red-lines the whole graph)");
            defaultAllCount.Should().Be(0,
                "NuGetAuditMode must NOT be 'default' (same effect as 'all')");
        }

        [Test]
        public void treat_warnings_as_errors_should_remain_true()
        {
            // The gate still relies on TreatWarningsAsErrors to make the (now direct-scope)
            // NU19xx findings into hard build failures. If someone flips this off, a
            // directly-referenced vulnerable package could ship silently.
            var lines = ReadPropsLines();
            var found = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("<TreatWarningsAsErrors>", StringComparison.Ordinal) &&
                    line.EndsWith("true</TreatWarningsAsErrors>", StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            found.Should().BeTrue(
                "TreatWarningsAsErrors must remain true so direct-scope NU19xx findings still gate the build");
        }

        [Test]
        public void nugetauditmode_direct_should_precede_any_other_nugetauditmode_occurrence()
        {
            // If a second <NuGetAuditMode> line were added later in the file (e.g. a per-project
            // override pulled in via another props), MSBuild's last-wins semantics would silently
            // undo the 'direct' gate. The direct declaration must be the ONLY occurrence.
            var lines = ReadPropsLines();
            var occurrences = 0;
            foreach (var raw in lines)
            {
                if (raw.Trim().StartsWith("<NuGetAuditMode>", StringComparison.Ordinal))
                {
                    occurrences++;
                }
            }

            occurrences.Should().Be(1,
                "Only one <NuGetAuditMode> declaration is permitted; a later override would win in MSBuild");
        }
    }
}
