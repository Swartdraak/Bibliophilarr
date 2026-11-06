using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Common.Test
{
    // Issue #286 regression: the Console linux-x64 (and every other non-Windows) single-project
    // publish must emit Bibliophilarr.Mono.dll into the app base directory.
    //
    // Root cause pinned here: NzbDrone.Common.Composition.AssemblyLoader.Load selects the
    // platform assembly BY NAME on every non-Windows host
    // (OsInfo.IsWindows ? "Bibliophilarr.Windows" : "Bibliophilarr.Mono") and loads it
    // BY NAME from the app base directory with no existence fallback. Neither NzbDrone.Host
    // nor NzbDrone.Common pulls NzbDrone.Mono into the Console publish, so at the develop
    // base (f52a6993) a single-project `dotnet publish ...Bibliophilarr.Console.csproj
    // -p:RuntimeIdentifier=linux-x64` succeeded (exit 0) but omitted
    // Bibliophilarr.Mono.dll, and the published app crashed at startup with
    // System.IO.FileNotFoundException before any service started (reproduced in-container;
    // see baseline.json / scratch-t_02ee5d55 logs).
    //
    // The durable fix under test (src/NzbDrone.Console/Bibliophilarr.Console.csproj) adds a
    // RuntimeIdentifier-scoped project reference:
    //     <ItemGroup Condition="!$(RuntimeIdentifier.StartsWith('win'))">
    //       <ProjectReference Include="..\NzbDrone.Mono\Bibliophilarr.Mono.csproj" />
    //     </ItemGroup>
    // so a RID-aware non-Windows publish emits Bibliophilarr.Mono.dll while the Windows
    // publish (win-* RIDs) is byte-for-byte unaffected (Bibliophilarr.Windows remains the
    // platform assembly the loader requests on Windows).
    //
    // Verified end-to-end in-container at the candidate head (task t_02ee5d55, run 3987):
    //   - linux-x64 publish: Bibliophilarr.Mono.dll PRESENT, app boots, /ping 200 JSON,
    //     persisted-API-key /api/v1/book 200 (AC1 + AC2).
    //   - win-x64 publish: Bibliophilarr.Mono.dll ABSENT, Bibliophilarr.Console.exe emitted
    //     (AC3).
    // This fixture pins the MSBuild condition against the checked-in csproj so a future
    // revert (removing the scoped reference, or making it unconditional and polluting the
    // Windows publish) is caught by CI. The assertions are semantic (literal element +
    // condition text) so they survive re-indentation or comment reflow.
    [TestFixture]
    public class ConsoleMonoPublishIssue286Fixture
    {
        // The Common.Test build output (per Directory.Build.props OutputPath for Test
        // projects) is <repo-root>/_tests/net10.0/. From that output dir the repo root is
        // two levels up. AppContext.BaseDirectory is the test assembly's own directory.
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
        public void console_project_should_reference_mono_only_for_non_windows_rids()
        {
            var text = ReadCsproj();
            var monoRefLines = new List<string>();

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("<ProjectReference", StringComparison.Ordinal) &&
                    line.Contains("NzbDrone.Mono", StringComparison.Ordinal))
                {
                    monoRefLines.Add(line);
                }
            }

            monoRefLines.Should().HaveCount(1,
                "Bibliophilarr.Mono must be referenced exactly once by the Console project");

            // The reference line itself must NOT carry a Condition (the condition lives on the
            // enclosing <ItemGroup>, matching the project's existing RID-scoped style).
            monoRefLines[0].Should().NotContain("Condition",
                "the NzbDrone.Mono reference must be scoped by its enclosing ItemGroup condition, not inline");
        }

        [Test]
        public void mono_reference_itemgroup_condition_should_exclude_win_rids()
        {
            var text = ReadCsproj();

            // Walk the file; when an <ItemGroup> with the non-Windows RID condition opens,
            // remember it until its closing tag. The NzbDrone.Mono reference must sit inside
            // such a scoped group (and not inside any other ItemGroup).
            var inScopedGroup = false;
            var monoInScoped = false;
            var monoElsewhere = false;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.StartsWith("<ItemGroup", StringComparison.Ordinal))
                {
                    inScopedGroup = line.Contains("RuntimeIdentifier.StartsWith('win')", StringComparison.Ordinal);
                    continue;
                }

                if (line.StartsWith("</ItemGroup>", StringComparison.Ordinal))
                {
                    inScopedGroup = false;
                    continue;
                }

                if (line.StartsWith("<ProjectReference", StringComparison.Ordinal) &&
                    line.Contains("NzbDrone.Mono", StringComparison.Ordinal))
                {
                    if (inScopedGroup)
                    {
                        monoInScoped = true;
                    }
                    else
                    {
                        monoElsewhere = true;
                    }
                }
            }

            monoInScoped.Should().BeTrue(
                "the NzbDrone.Mono reference must sit inside the '!$(RuntimeIdentifier.StartsWith('win'))' ItemGroup");
            monoElsewhere.Should().BeFalse(
                "no NzbDrone.Mono reference may exist outside the RID-scoped ItemGroup (it would pollute the Windows publish)");
        }

        [Test]
        public void mono_dll_should_be_loaded_by_name_from_app_base_on_non_windows()
        {
            // Guard the runtime contract the publish fix exists to satisfy:
            // AssemblyLoader still selects "Bibliophilarr.Mono" by name on non-Windows and
            // loads it by file path from the app base directory. The referenced assembly
            // must therefore be present in the published output for every RID it can
            // request (AC4: no behavior change beyond build composition).
            var path = Path.Combine(RepoRoot, "src", "NzbDrone.Common", "Composition", "AssemblyLoader.cs");
            File.Exists(path).Should().BeTrue($"src/NzbDrone.Common/Composition/AssemblyLoader.cs must exist at {path}");
            var text = File.ReadAllText(path);

            text.Should().Contain("\"Bibliophilarr.Mono\"",
                "the name-based platform assembly selection for non-Windows hosts must remain");
            text.Should().Contain("LoadFromAssemblyPath",
                "the by-name app-base-directory load must remain (the fix is build-composition only)");
        }
    }
}
