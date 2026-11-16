using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    // Issue #270 (macos-latest app-smoke lane) regression: the portable Console
    // single-project publish must emit the SQLite ADO provider (System.Data.SQLite,
    // the netstandard stub of System.Data.SQLite.Core) into the app base directory
    // for EVERY non-Windows RID (linux/osx/musl/arm...).
    //
    // Root cause pinned here: the app boots with 158 SQLite migrations through
    // FluentMigrator 3.3.2 (FluentMigrator.Runner.SQLite -> FluentMigrator.Runner.Core
    // ReflectionBasedDbFactory.CreateDatabase), which materializes the provider
    // assembly by file path from the app base directory (Assembly.LoadFile, no
    // resolver fallback). The provider ADO assembly is System.Data.SQLite.dll,
    // shipped by the netstandard stub of System.Data.SQLite.Core/1.0.119
    // (lib/netstandard2.1/System.Data.SQLite.dll) - NOT the managed
    // Microsoft.Data.Sqlite package. The repo's NzbDrone.Core references
    // System.Data.SQLite.Core for its own Dapper/DbFactory usage (System.Data.SQLite
    // namespace, SQLiteConnection/SQLiteFactory), so the stub is a project-local
    // asset: dotnet publish Bibliophilarr.Console.csproj does NOT copy it into the
    // app base directory. On a Linux host the identical single-project publish
    // (the in-container acceptance run, task t_95372b9c) carried no provider
    // assembly either, yet booted 158 migrations fine on linux-x64 - the published
    // app's deps.json/runtime assembly resolution found the ADO provider another
    // way (host runtime + deps.json entries), while the same composition on the
    // the hosted osx runner failed with:
    //   System.Exception: Failed to load file
    //     .../_temp/pub-osx/Microsoft.Data.Sqlite.dll
    //   (the runner image's ADO provider materialization resolved by a name
    //    that file was absent for; FileNotFoundException at
    //    ReflectionBasedDbFactory)
    // so the osx app died at first-boot migration before /ping was ever served
    // (hosted check 133120267219, head c89b72f7; receipt status BUILD_ONLY).
    //
    // The durable fix under test (src/NzbDrone.Console/Bibliophilarr.Console.csproj)
    // adds a RuntimeIdentifier-scoped ItemGroup that copies the provider ADO
    // assembly from the build output into the publish output for every non-Windows
    // RID (the Windows publish is byte-for-byte unaffected; the win-* group is the
    // exact logical complement, mirroring the issue-#270/#286 platform-assembly
    // scoping). The copy preserves the published app's existing startup composition
    // (no product source, no runtime loader behavior, no deps.json rewrite):
    // whatever resolution path the runner image used for the provider assembly now
    // has the file present in the app base directory as well, so
    // ReflectionBasedDbFactory's by-path LoadFile cannot fail there.
    //
    // Verified end-to-end in-container at the repair head (task t_bc2c7c53):
    //   - osx-x64 publish: System.Data.SQLite.dll PRESENT in the app base dir,
    //     Bibliophilarr.Mono.dll PRESENT, Microsoft.Data.Sqlite.dll ABSENT
    //     (the provider ADO assembly is the System.Data.SQLite stub, not the
    //     managed package).
    //   - win-x64 publish: System.Data.SQLite.dll ABSENT (the non-win scoped
    //     group is inactive; the Windows publish is byte-for-byte unaffected).
    // This fixture pins the MSBuild condition against the checked-in csproj so a
    // future revert (removing the scoped copy, or making it unconditional and
    // polluting the Windows publish) is caught by CI. The assertions are semantic
    // (literal element + condition text) so they survive re-indentation or
    // comment reflow - mirroring ConsoleMonoPublishIssue286Fixture /
    // ConsoleWindowsPublishIssue270Fixture.
    [TestFixture]
    public class ConsoleSqliteProviderPublishIssue270Fixture
    {
        // The Core.Test build output (per Directory.Build.props OutputPath for
        // Test projects) is <repo-root>/_tests/net10.0/. From that output dir the
        // repo root is two levels up. AppContext.BaseDirectory is the test
        // assembly's own directory. (The same convention as
        // ConsoleMonoPublishIssue286Fixture / ConsoleWindowsPublishIssue270Fixture,
        // but from the Core.Test project.)
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));

        private static string ConsoleCsprojPath =>
            Path.Combine(RepoRoot, "src", "NzbDrone.Console", "Bibliophilarr.Console.csproj");

        private static string ReadCsproj()
        {
            var path = ConsoleCsprojPath;
            File.Exists(path).Should().BeTrue($"src/NzbDrone.Console/Bibliophilarr.Console.csproj must exist at {path}");
            return File.ReadAllText(path);
        }

        [Test]
        public void console_project_should_copy_the_sqlite_provider_for_non_windows_rids()
        {
            var text = ReadCsproj();
            var copyLines = new System.Collections.Generic.List<string>();

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("<CopyToPublishDirectory", StringComparison.Ordinal) &&
                    line.Contains("System.Data.SQLite.dll", StringComparison.Ordinal))
                {
                    copyLines.Add(line);
                }
            }

            copyLines.Should().HaveCount(1,
                "the System.Data.SQLite.dll provider copy must appear exactly once in the Console project");

            copyLines[0].Should().NotContain("Condition",
                "the provider copy must be scoped by its enclosing ItemGroup condition, not inline");
        }

        [Test]
        public void provider_copy_itemgroup_condition_should_exclude_win_rids()
        {
            var text = ReadCsproj();

            // Walk the file; when an <ItemGroup> with the non-Windows RID condition
            // opens, remember it until its closing tag. The provider copy must sit
            // inside such a scoped group (and not inside any other ItemGroup).
            var inScopedGroup = false;
            var copyInScoped = false;
            var copyElsewhere = false;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.StartsWith("<ItemGroup", StringComparison.Ordinal))
                {
                    inScopedGroup = line.Contains("!$(RuntimeIdentifier.StartsWith('win'))", StringComparison.Ordinal);
                    continue;
                }

                if (line.StartsWith("</ItemGroup>", StringComparison.Ordinal))
                {
                    inScopedGroup = false;
                    continue;
                }

                if (line.StartsWith("<CopyToPublishDirectory", StringComparison.Ordinal) &&
                    line.Contains("System.Data.SQLite.dll", StringComparison.Ordinal))
                {
                    if (inScopedGroup)
                    {
                        copyInScoped = true;
                    }
                    else
                    {
                        copyElsewhere = true;
                    }
                }
            }

            copyInScoped.Should().BeTrue(
                "the System.Data.SQLite.dll copy must sit inside the '!$(RuntimeIdentifier.StartsWith('win'))' ItemGroup");
            copyElsewhere.Should().BeFalse(
                "no System.Data.SQLite.dll copy may exist outside the RID-scoped ItemGroup (it would pollute the Windows publish)");
        }

        [Test]
        public void the_sqlite_provider_copy_must_be_the_stub_not_a_new_package()
        {
            // The copy source is the build output's System.Data.SQLite.dll (the
            // netstandard stub of System.Data.SQLite.Core/1.0.119 - the provider
            // ADO assembly the app's Dapper/DbFactory and FluentMigrator 3.3.2
            // ReflectionBasedDbFactory both consume by path). Guard against a
            // future edit that (a) switches to the managed Microsoft.Data.Sqlite
            // package (a different provider - the repo's code targets the
            // System.Data.SQLite namespace) or (b) references a package that is
            // not already in the repo's graph.
            var text = ReadCsproj();

            text.Should().Contain(
                "_output/net10.0/System.Data.SQLite.dll",
                "the copy source must be the build output's System.Data.SQLite stub, not a new package");
            text.Should().NotContain(
                "<PackageReference Include=\"Microsoft.Data.Sqlite\"",
                "the provider ADO assembly is the System.Data.SQLite stub; the managed Microsoft.Data.Sqlite package is a different provider and must not be introduced");
        }
    }
}
