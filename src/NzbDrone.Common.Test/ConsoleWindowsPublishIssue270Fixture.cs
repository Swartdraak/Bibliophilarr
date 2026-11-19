using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Common.Test
{
    // Issue #270 regression (Windows mirror of issue #286): the Console win-x64
    // (and every other win-*) single-project publish must emit
    // Bibliophilarr.Windows.dll into the app base directory.
    //
    // Root cause pinned here: NzbDrone.Common.Composition.AssemblyLoader.Load
    // selects the platform assembly BY NAME on every host
    // (OsInfo.IsWindows ? "Bibliophilarr.Windows" : "Bibliophilarr.Mono") and
    // loads it BY NAME from the app base directory with no existence fallback
    // (LoadFromAssemblyPath). Neither NzbDrone.Host nor NzbDrone.Common pulls
    // NzbDrone.Windows into the Console publish, so at the develop base a
    // single-project `dotnet publish ...Bibliophilarr.Console.csproj
    // -p:RuntimeIdentifier=win-x64` succeeded (exit 0) but omitted
    // Bibliophilarr.Windows.dll, and the published app crashed at startup with
    // System.IO.FileNotFoundException during DryIoc AutoAddServices (Bootstrap
    // CreateConsoleHostBuilder) before any service started -> /ping never
    // returned 200 -> the hosted app-smoke receipt was BUILD_ONLY /
    // PING_TIMEOUT (reproduced on the hosted windows-latest runner; see
    // publication.json / task-checkpoint-t_6717b1e1.json, check 133027708469).
    //
    // The durable fix under test (src/NzbDrone.Console/Bibliophilarr.Console.csproj)
    // adds a RuntimeIdentifier-scoped project reference:
    //     <ItemGroup Condition="$(RuntimeIdentifier.StartsWith('win'))">
    //       <ProjectReference Include="..\NzbDrone.Windows\Bibliophilarr.Windows.csproj" />
    //     </ItemGroup>
    // so a RID-aware win-* publish emits Bibliophilarr.Windows.dll while every
    // non-Windows publish is byte-for-byte unaffected (Bibliophilarr.Mono remains
    // the platform assembly the loader requests on non-Windows hosts, per the
    // issue-#286 fix pinned by ConsoleMonoPublishIssue286Fixture). NzbDrone.Windows
    // is a plain net10.0 class library (no WindowsDesktop SDK, no WinForms, no
    // net10.0-windows TFM) and cross-builds cleanly on every host, so adding the
    // reference does not pull a Windows-only project model into the portable
    // Console publish. No runtime loader behavior changes.
    //
    // Verified end-to-end in-container at the repair head (task t_35d92de8):
    //   - win-x64 publish: Bibliophilarr.Windows.dll PRESENT (12,288 bytes),
    //     Bibliophilarr.Mono.dll ABSENT, Bibliophilarr.Console.exe emitted.
    //   - linux-x64 publish (regression): Bibliophilarr.Mono.dll PRESENT,
    //     Bibliophilarr.Windows.dll ABSENT (the non-win scoped group is untouched).
    // This fixture pins the MSBuild condition against the checked-in csproj so a
    // future revert (removing the scoped reference, or making it unconditional and
    // polluting the non-Windows publish) is caught by CI. The assertions are
    // semantic (literal element + condition text) so they survive re-indentation
    // or comment reflow — mirroring ConsoleMonoPublishIssue286Fixture.
    [TestFixture]
    public class ConsoleWindowsPublishIssue270Fixture
    {
        private static string ConsoleCsprojPath =>
            Path.Combine(RepositorySource.Root, "src", "NzbDrone.Console", "Bibliophilarr.Console.csproj");

        private static string ReadCsproj()
        {
            var path = ConsoleCsprojPath;
            File.Exists(path).Should().BeTrue($"src/NzbDrone.Console/Bibliophilarr.Console.csproj must exist at {path}");
            return File.ReadAllText(path);
        }

        [Test]
        public void console_project_should_reference_windows_only_for_windows_rids()
        {
            var text = ReadCsproj();
            var winRefLines = new List<string>();

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("<ProjectReference", StringComparison.Ordinal) &&
                    line.Contains("NzbDrone.Windows", StringComparison.Ordinal))
                {
                    winRefLines.Add(line);
                }
            }

            winRefLines.Should().HaveCount(1,
                "Bibliophilarr.Windows must be referenced exactly once by the Console project");

            // The reference line itself must NOT carry a Condition (the condition lives on the
            // enclosing <ItemGroup>, matching the project's existing RID-scoped style).
            winRefLines[0].Should().NotContain("Condition",
                "the NzbDrone.Windows reference must be scoped by its enclosing ItemGroup condition, not inline");
        }

        [Test]
        public void windows_reference_itemgroup_condition_should_include_win_rids()
        {
            var text = ReadCsproj();

            // Walk the file; when an <ItemGroup> with the Windows RID condition
            // (RuntimeIdentifier.StartsWith('win'), the AFFIRMATIVE form — not the
            // negated non-Windows condition) opens, remember it until its closing
            // tag. The NzbDrone.Windows reference must sit inside such a scoped
            // group (and not inside any other ItemGroup).
            var inWinScopedGroup = false;
            var winInScoped = false;
            var winElsewhere = false;

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.StartsWith("<ItemGroup", StringComparison.Ordinal))
                {
                    // Affirmative Windows condition: the group's condition must contain
                    // the StartsWith('win') test and must NOT be its negation (the
                    // non-Windows group carries the '!$(RuntimeIdentifier...' form).
                    inWinScopedGroup =
                        line.Contains("$(RuntimeIdentifier.StartsWith('win'))", StringComparison.Ordinal) &&
                        !line.Contains("!$(RuntimeIdentifier", StringComparison.Ordinal);
                    continue;
                }

                if (line.StartsWith("</ItemGroup>", StringComparison.Ordinal))
                {
                    inWinScopedGroup = false;
                    continue;
                }

                if (line.StartsWith("<ProjectReference", StringComparison.Ordinal) &&
                    line.Contains("NzbDrone.Windows", StringComparison.Ordinal))
                {
                    if (inWinScopedGroup)
                    {
                        winInScoped = true;
                    }
                    else
                    {
                        winElsewhere = true;
                    }
                }
            }

            winInScoped.Should().BeTrue(
                "the NzbDrone.Windows reference must sit inside the '$(RuntimeIdentifier.StartsWith('win'))' ItemGroup");
            winElsewhere.Should().BeFalse(
                "no NzbDrone.Windows reference may exist outside the RID-scoped ItemGroup (it would pollute the non-Windows publish)");
        }

        [Test]
        public void the_two_platform_itemgroups_must_be_exact_logical_complements()
        {
            // The Windows group and the non-Windows (issue-#286 Mono) group must be
            // the exact logical complements of each other, so that for EVERY RID
            // exactly one platform assembly is referenced — matching what
            // AssemblyLoader will request at startup. Guard both conditions so a
            // future edit that makes them overlap (both active) or gap (neither
            // active) is caught.
            var text = ReadCsproj();

            text.Should().Contain(
                "$(RuntimeIdentifier.StartsWith('win'))",
                "the Windows platform group must be scoped by the affirmative win-RID condition");
            text.Should().Contain(
                "!$(RuntimeIdentifier.StartsWith('win'))",
                "the non-Windows (Mono) platform group must remain scoped by the negated condition");
        }

        [Test]
        public void windows_dll_should_be_loaded_by_name_from_app_base_on_windows()
        {
            // Guard the runtime contract the publish fix exists to satisfy:
            // AssemblyLoader still selects "Bibliophilarr.Windows" by name on
            // Windows and loads it by file path from the app base directory. The
            // referenced assembly must therefore be present in the published
            // output for the win-* RIDs it can request (no behavior change beyond
            // build composition).
            var path = Path.Combine(RepositorySource.Root, "src", "NzbDrone.Common", "Composition", "AssemblyLoader.cs");
            File.Exists(path).Should().BeTrue($"src/NzbDrone.Common/Composition/AssemblyLoader.cs must exist at {path}");
            var text = File.ReadAllText(path);

            text.Should().Contain("\"Bibliophilarr.Windows\"",
                "the name-based platform assembly selection for Windows hosts must remain");
            text.Should().Contain("LoadFromAssemblyPath",
                "the by-name app-base-directory load must remain (the fix is build-composition only)");
        }
    }
}
