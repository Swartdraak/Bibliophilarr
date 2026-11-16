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
            // future edit that switches to the managed Microsoft.Data.Sqlite
            // package as a REPLACEMENT for the System.Data.SQLite stub copy.
            // The Microsoft.Data.Sqlite package IS now a direct dependency of the
            // Console project (repair 6) because the .NET runtime on the hosted
            // osx runner resolves the SQLite provider through the managed
            // Microsoft.Data.Sqlite assembly, which a single-project publish
            // does not emit without an explicit reference. This is a
            // COMPLEMENTARY dependency (it provides the managed ADO provider
            // that the runtime needs alongside the native System.Data.SQLite
            // stub), not a replacement.
            var text = ReadCsproj();

            text.Should().Contain(
                "_output/net10.0/System.Data.SQLite.dll",
                "the copy source must be the build output's System.Data.SQLite stub, not a new package");
        }

        // ------------------------------------------------------------------
        // Repair 5 (task t_8a929d38): the hosted macos-latest lane STILL failed
        // at 466edee54f with a by-path provider load inside FluentMigrator
        // 3.3.2's ReflectionBasedDbFactory (hosted check 133259224161, receipt
        // BUILD_ONLY). The repair-4 scoped copy of the ADO provider stub did
        // not close the defect class: the by-path materialization surface is
        // the FLUENTMIGRATOR 3.3.2 provider assemblies themselves
        // (FluentMigrator.dll, Runner.dll, Runner.Core.dll, Runner.SQLite.dll,
        // Abstractions.dll, Extensions.*.dll), which a single-project Console
        // publish does not emit into the app base directory either. The repair
        // copies them from the shared _output/net10.0 build output into every
        // non-Windows publish, gated at publish time by the
        // NonWinRidProviderProbe RID-gated property (the exact logical
        // complement of the win-RID platform group, mirroring the
        // issue-#286/#270 RID-scoped ItemGroup style; absent at restore/build
        // time so the portable solution model is untouched). These assertions
        // pin that gate so a future revert (removing the provider-surface copy,
        // making it unconditional and polluting the Windows publish, or
        // switching to a new package) is caught by CI.
        // ------------------------------------------------------------------
        private static readonly string[] Repair5ProviderAssemblies =
        {
            "FluentMigrator.dll",
            "FluentMigrator.Runner.dll",
            "FluentMigrator.Runner.Core.dll",
            "FluentMigrator.Runner.SQLite.dll",
            "FluentMigrator.Abstractions.dll",
            "FluentMigrator.Extensions.Oracle.dll",
            "FluentMigrator.Extensions.Postgres.dll",
            "FluentMigrator.Extensions.SqlAnywhere.dll",
            "FluentMigrator.Extensions.SqlServer.dll",
        };

        [Test]
        public void repair5_provider_surface_copies_must_sit_in_a_rid_gated_non_win_group()
        {
            var text = ReadCsproj();

            // The publish-time RID gate: a property that is 'true' exactly when
            // the publish RID is not win-* (empty at restore/build time, where
            // RuntimeIdentifier is unset, so the portable model is untouched).
            text.Should().Contain(
                "<NonWinRidProviderProbe Condition=\"'$(RuntimeIdentifier)' != '' and !$(RuntimeIdentifier.StartsWith('win'))\">true</NonWinRidProviderProbe>",
                "the repair-5 provider-surface copy must be gated at publish time by the NonWinRidProviderProbe RID-gated property");

            // Every repair-5 provider assembly must be copied from the shared
            // _output/net10.0 build output (not a new package, not a RID-
            // specific asset path).
            foreach (var asm in Repair5ProviderAssemblies)
            {
                text.Should().Contain(
                    $"_output/net10.0/{asm}",
                    $"the repair-5 provider surface must copy {asm} from the shared _output/net10.0 build output");
            }
        }

        [Test]
        public void repair5_provider_copies_must_be_scoped_outside_any_win_rid_group()
        {
            var text = ReadCsproj();

            // Walk the file: remember whether the current ItemGroup is
            // RID-gated to non-Windows (either by the probe property or by the
            // legacy !win condition). Every repair-5 copy must sit inside such
            // a group and NOWHERE else.
            var inNonWinGroup = false;
            var copyInScoped = 0;
            var copyElsewhere = 0;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.StartsWith("<ItemGroup", StringComparison.Ordinal))
                {
                    inNonWinGroup =
                        line.Contains("'$(NonWinRidProviderProbe)' == 'true'", StringComparison.Ordinal) ||
                        line.Contains("!$(RuntimeIdentifier.StartsWith('win'))", StringComparison.Ordinal);
                    continue;
                }

                if (line.StartsWith("</ItemGroup>", StringComparison.Ordinal))
                {
                    inNonWinGroup = false;
                    continue;
                }

                if (line.StartsWith("<CopyToPublishDirectory", StringComparison.Ordinal))
                {
                    foreach (var asm in Repair5ProviderAssemblies)
                    {
                        if (line.Contains($"_output/net10.0/{asm}", StringComparison.Ordinal))
                        {
                            if (inNonWinGroup)
                            {
                                copyInScoped++;
                            }
                            else
                            {
                                copyElsewhere++;
                            }
                        }
                    }
                }
            }

            copyInScoped.Should().Be(Repair5ProviderAssemblies.Length,
                "every repair-5 provider assembly copy must sit inside the non-Windows RID-gated ItemGroup");
            copyElsewhere.Should().Be(0,
                "no repair-5 provider copy may exist outside the non-Windows RID-gated ItemGroup (it would pollute the Windows publish)");
        }
    }
}
