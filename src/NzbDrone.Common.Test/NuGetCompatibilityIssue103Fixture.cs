using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Common.Test
{
    // NET10-03 (#103, epic #96 "Migration Program: .NET 8 -> .NET 10 LTS"): post-migration
    // NuGet COMPATIBILITY review (development audit run, 2026-11-01, task t_ca020c6d) at
    // develop tip e36967ad.
    //
    // What this fixture adds on top of NuGetAuditModeIssue103Fixture (which pins the
    // NuGetAuditMode=direct gate): two further compatibility invariants observed during the
    // full per-project graph audit of all 24 solution projects (23 x net10.0 + 1 x
    // net10.0-windows):
    //
    //  1. NO central-pin drift / downgrade (acceptance "no accidental dependency churn"):
    //     every PackageVersion entry in src/Directory.Packages.props is a concrete exact
    //     version (no floating '*', no ranges). Central Package Management (ManagePackageVersionsCentrally
    //     = true in src/Directory.Build.props) makes those pins the minimum for the whole graph,
    //     and the restore graph audit (scratch-restore) confirmed ZERO resolved versions below a
    //     central pin anywhere in the solution. A floating or range pin would be the first step
    //     toward accidental churn, so the format of the pin file is pinned here.
    //
    //  2. Known-vulnerable transitive pins remain explicit (residual-risk tracking): the
    //     post-migration audit documented three packages that remain on vulnerable lines by
    //     design (tracked, not silently churned):
    //       - System.Security.Cryptography.Pkcs  10.0.13 (GHSA-2fmv-rv2c-59x7 / GHSA-846f-f764-76mx;
    //         patched line, transitive-only via SqlClient/IdentityModel; central pin forces both
    //         flows up to 10.0.x)
    //       - System.Drawing.Common 9.0.0 (prevents legacy PdfPig/MonoTorrent/TagLibSharp chains
    //         from resolving the critical 4.7.0 line, GHSA-rxg9-xrhp-64gj)
    //       - SixLabors.ImageSharp 4.1.2 (Oct-2026 incident: 3.1.11 audit advisory; 4.1.2 is the
    //         current stable line with license enforcement)
    //     If any of these pins is removed or moved, the residual-risk tracking documented in
    //     Directory.Packages.props / .onyx/project.yaml silently breaks - this test catches it.
    //
    //  Both tests read the CHECKED-IN src/Directory.Packages.props (same repo-root resolution
    //  convention as NuGetAuditModeIssue103Fixture), so they run in the isolated environment
    //  with no restore.
    [TestFixture]
    public class NuGetCompatibilityIssue103Fixture
    {
        // Common.Test build output (per Directory.Build.props OutputPath for Test projects) is
        // <repo-root>/_tests/net10.0/. From that output dir the repo root is two levels up
        // (verified against the actual _tests/net10.0 layout - same convention as
        // NuGetAuditModeIssue103Fixture).
        private static readonly string RepoRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));

        private static string DirectoryPackagesPropsPath =>
            Path.Combine(RepoRoot, "src", "Directory.Packages.props");

        // (package id, exact pin version) - the post-migration residual-risk pins that must
        // remain explicitly tracked in the central package file.
        private static readonly (string Package, string Version)[] KnownVulnerableTransitivePins =
        {
            ("System.Security.Cryptography.Pkcs", "10.0.13"),
            ("System.Drawing.Common", "9.0.0"),
            ("SixLabors.ImageSharp", "4.1.2"),
        };

        private static List<(string Package, string Version)> ReadCentralPins()
        {
            var path = DirectoryPackagesPropsPath;
            File.Exists(path).Should().BeTrue($"src/Directory.Packages.props must exist at {path}");
            var lines = File.ReadAllLines(path);
            var pins = new List<(string, string)>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("<PackageVersion", StringComparison.Ordinal))
                {
                    // Match: <PackageVersion Include="Id" Version="x.y.z" .../>
                    var id = ExtractAttr(line, "Include");
                    var ver = ExtractAttr(line, "Version");
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(ver))
                    {
                        pins.Add((id, ver));
                    }
                }
            }

            pins.Should().NotBeEmpty("Directory.Packages.props must declare central package pins");
            return pins;
        }

        private static string ExtractAttr(string line, string attr)
        {
            var token = attr + "=";
            var idx = line.IndexOf(token, StringComparison.Ordinal);
            if (idx < 0)
            {
                return null;
            }

            var start = idx + token.Length;
            while (start < line.Length && char.IsWhiteSpace(line[start]))
            {
                start++;
            }

            if (start >= line.Length || (line[start] != '"' && line[start] != '\''))
            {
                return null;
            }

            var quote = line[start];
            var end = line.IndexOf(quote, start + 1);
            if (end < 0)
            {
                return null;
            }

            return line.Substring(start + 1, end - start - 1);
        }

        [Test]
        public void central_pins_should_be_exact_versions_no_floating_or_ranges()
        {
            // Acceptance #103 "no accidental dependency churn": with CPM the central pins are
            // the graph minimum. A floating '*' pin or a range (e.g. "[1.0, 2.0)" / "1.0.0-*")
            // would let restores drift package-by-package without any PR touching the pin file.
            var pins = ReadCentralPins();
            foreach (var (pkg, ver) in pins)
            {
                ver.Should().NotBe("*",
                    $"{pkg}: central pin must be an exact version, not floating '*'");
                ver.Should().NotContain("*",
                    $"{pkg}: central pin must not contain a floating wildcard");
                ver.Should().NotStartWith("[",
                    $"{pkg}: central pin must not be a NuGet range (use the exact version)");

                // exact version shape: digits and dots, optional prerelease tag
                var exact = ver.Split('-')[0];
                exact.Split('.').Should().OnlyContain(
                    part => part.Length > 0 && part.All(char.IsDigit),
                    $"{pkg}: central pin '{ver}' must be a plain numeric version (optional -prerelease suffix)");
            }
        }

        [Test]
        public void known_vulnerable_transitive_pins_should_remain_explicitly_tracked()
        {
            // Acceptance #103 "blockers are identified and assigned": the post-migration audit
            // keeps the known-vulnerable transitive lines pinned (and documented) rather than
            // silently churned. Removing or re-pointing any of these pins breaks the
            // residual-risk tracking contract.
            var pins = ReadCentralPins()
                .ToDictionary(p => p.Package, p => p.Version, StringComparer.OrdinalIgnoreCase);

            foreach (var (pkg, expected) in KnownVulnerableTransitivePins)
            {
                pins.ContainsKey(pkg).Should().BeTrue(
                    $"{pkg} must stay explicitly pinned in Directory.Packages.props (residual-risk tracking)");
                pins[pkg].Should().Be(expected,
                    $"{pkg} pin drifted from the audited post-migration line {expected} - re-run the #103 compatibility review");
            }
        }

        [Test]
        public void cpm_should_be_enabled_in_directory_build_props()
        {
            // The whole pin-drift analysis only holds if Central Package Management is on.
            // (NuGetAuditModeIssue103Fixture covers NuGetAuditMode + TreatWarningsAsErrors;
            // this closes the third gate property in the same file.)
            var path = Path.Combine(RepoRoot, "src", "Directory.Build.props");
            File.Exists(path).Should().BeTrue($"src/Directory.Build.props must exist at {path}");
            var found = false;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("<ManagePackageVersionsCentrally>", StringComparison.Ordinal) &&
                    line.EndsWith("true</ManagePackageVersionsCentrally>", StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            found.Should().BeTrue(
                "ManagePackageVersionsCentrally must remain true so the audited pins are the graph minimum");
        }
    }
}
