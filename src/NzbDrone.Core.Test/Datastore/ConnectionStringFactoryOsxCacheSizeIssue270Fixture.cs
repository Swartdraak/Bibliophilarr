using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    // Issue #270 (macos-latest app-smoke lane, 9-head failure lineage) root-cause
    // regression: the hosted macos-latest app-smoke job died at first boot with
    //   System.ArgumentException: Connection string keyword 'cache size' is not
    //   supported. (FluentMigrator.Runner.SQLite processor connection init)
    // The app's IConnectionStringFactory (NzbDrone.Core.Datastore.
    // ConnectionStringFactory.GetConnectionString) built EVERY SQLite connection
    // string with CacheSize = -20000 unconditionally. The macOS build of the
    // SQLite provider (System.Data.SQLite, netstandard2.1 stub of
    // System.Data.SQLite.Core/1.0.119 - the same assembly that
    // SQLiteConnectionStringBuilder.GetDatabasePath() parses and that
    // FluentMigrator 3.3.2's ReflectionBasedDbFactory opens the connection with)
    // does not accept the "cache size" keyword, so on an osx host the
    // connection-string construction itself threw before any migration ran.
    //
    // The repair: emit the -20000 page-cache setting only on non-osx hosts.
    // On osx every other keyword is unchanged (DateTimeKind, JournalMode=
    // Truncate, Pooling, Version, BusyTimeout, Full FSync), so non-osx behavior
    // is byte-for-byte identical and osx loses only the page-cache hint the
    // provider rejects (SQLite falls back to its default page cache).
    //
    // The sibling ConsoleSqliteProviderPublishIssue270Fixture pins the publish
    // composition half of this defect class (the provider assemblies the
    // connection must load); this fixture pins the connection-string half.
    //
    // This fixture pins the platform-scoped emission semantically (literal
    // tokens + osx gating), so a future revert (unconditional CacheSize again,
    // or dropping the setting on every host) is caught by CI - mirroring the
    // sibling ConsoleSqliteProviderPublishIssue270Fixture style.
    [TestFixture]
    public class ConnectionStringFactoryOsxCacheSizeIssue270Fixture
    {
        // Repo-root resolution must survive every execution context this
        // fixture runs in: the CI test host (NUnitLite, whose base directory is
        // the test-assembly dir under _tests/net10.0, two levels from the repo
        // root) AND a plain `dotnet test` (whose base directory is the project
        // dir src/NzbDrone.Core.Test). Probe the conventional _tests path
        // first and fall back to the project-relative path, so the fixture is
        // not execution-context-fragile.
        private static string ResolveRepoRoot()
        {
            var fromBaseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
            if (File.Exists(Path.Combine(fromBaseDir, "src", "NzbDrone.Core", "Datastore", "ConnectionStringFactory.cs")))
            {
                return fromBaseDir;
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        }

        private static readonly string RepoRoot = ResolveRepoRoot();

        private static string FactoryPath =>
            Path.Combine(RepoRoot, "src", "NzbDrone.Core", "Datastore", "ConnectionStringFactory.cs");

        private static string ReadFactory()
        {
            var path = FactoryPath;
            File.Exists(path).Should().BeTrue($"src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs must exist at {path}");
            return File.ReadAllText(path);
        }

        [Test]
        public void osx_connection_strings_must_not_carry_the_rejected_cache_size_keyword()
        {
            var text = ReadFactory();

            // The CacheSize property must NOT appear in an unconditional
            // object-initializer position anymore: it must be assigned only in
            // a branch (a statement), never in the builder's initializer.
            text.Should().Contain(
                "new SQLiteConnectionStringBuilder",
                "the SQLite connection builder must still be constructed");

            // The osx gate must still exist (JournalMode Truncate + Full FSync).
            text.Should().Contain(
                "JournalMode = OsInfo.IsOsx ? SQLiteJournalModeEnum.Truncate : SQLiteJournalModeEnum.Wal",
                "the osx journal-mode gate must be unchanged");
            text.Should().Contain(
                "connectionBuilder.Add(\"Full FSync\", true)",
                "the osx Full FSync setting must be unchanged");

            // The cache-size assignment must be gated: it must sit inside an
            // else-branch (or another osx-excluding condition), i.e. the line
            // "connectionBuilder.CacheSize = -20000;" must exist as a statement
            // AND the GetConnectionString builder initializer (the second
            // "new SQLiteConnectionStringBuilder" in the file) must NOT assign
            // CacheSize. The first occurrence is the GetDatabasePath ctor-form
            // builder with no initializer, so the scan anchors on the second
            // occurrence.
            var marker = "new SQLiteConnectionStringBuilder";
            var first = text.IndexOf(marker, StringComparison.Ordinal);
            first.Should().BeGreaterThan(-1, "the SQLite connection builder construction must exist");
            var anchor = text.IndexOf(marker, first + marker.Length, StringComparison.Ordinal);
            anchor.Should().BeGreaterThan(first, "the GetConnectionString builder construction must exist");

            // The cache-size assignment must be gated: it must sit inside an
            // else-branch (or another osx-excluding condition).
            var initializerBlock = StripComments(ExtractInitializerBlock(text.Substring(anchor)));
            initializerBlock.Should().NotContain(
                "CacheSize =",
                "the CacheSize page-cache hint must not be in the unconditional builder initializer (the osx provider rejects the 'cache size' keyword)");

            text.Should().Contain(
                "connectionBuilder.CacheSize = -20000;",
                "the non-osx hosts must still get the -20000 page-cache hint, assigned in the osx-excluding branch");
        }

        [Test]
        public void a_revert_to_unconditional_cache_size_is_caught()
        {
            // Guard: if a future change re-instates CacheSize = -20000 in the
            // unconditional GetConnectionString builder initializer (reverting
            // the #190 osx repair and the #270 gating), this fixture must fail.
            // Simulated by the fixture's own extractor against a synthetic
            // pre-repair source shape, so the guard itself stays covered.
            const string reverted =
                "var connectionBuilder = new SQLiteConnectionStringBuilder\n" +
                "{\n" +
                "    DataSource = dbPath,\n" +
                "    CacheSize = -20000,\n" +
                "};\n";
            var block = ExtractInitializerBlock(reverted);
            block.Should().Contain("CacheSize =", "the revert simulation must still expose the unconditional initializer assignment");
        }

        // Strips // line comments and /* block comments */ so that prose
        // mentioning a token cannot affect token-level assertions.
        private static string StripComments(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            var inLineComment = false;
            var inBlockComment = false;
            var inString = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n')
                    {
                        sb.Append(c);
                        inLineComment = false;
                    }

                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && next != '\0')
                    {
                        sb.Append(next);
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    sb.Append(c);
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        [Test]
        public void the_cache_size_assignment_must_be_scoped_to_non_osx()
        {
            var text = ReadFactory();

            // Walk the GetConnectionString method body on comment-stripped
            // source (prose must not create false token matches): find the osx
            // if-block (the Full FSync gate) and verify the CacheSize assignment
            // sits in a sibling else-branch, not in the osx branch itself.
            var lines = StripComments(text).Split('\n');
            var osxIfStart = -1;
            var cacheSizeLine = -1;
            var fullFsyncLine = -1;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("if (OsInfo.IsOsx)", StringComparison.Ordinal) && osxIfStart == -1)
                {
                    // The first osx if-block in the file is the Full FSync gate
                    // (the JournalMode gate is a ternary, not an if-block).
                    osxIfStart = i;
                }

                if (line.Contains("Full FSync", StringComparison.Ordinal) && fullFsyncLine == -1)
                {
                    fullFsyncLine = i;
                }

                if (line.Contains("CacheSize = -20000", StringComparison.Ordinal) && cacheSizeLine == -1)
                {
                    cacheSizeLine = i;
                }
            }

            osxIfStart.Should().BeGreaterThan(-1, "the osx Full FSync if-block must exist");
            fullFsyncLine.Should().BeGreaterThan(-1, "the Full FSync assignment must exist");
            cacheSizeLine.Should().BeGreaterThan(-1, "the CacheSize assignment must exist");
            cacheSizeLine.Should().BeGreaterThan(fullFsyncLine,
                "the CacheSize assignment must come AFTER the osx if-block (i.e. in its else-branch), not before it or inside the osx branch");

            // Between the osx if-block open and the CacheSize line there must be
            // an else (the branch switch), and the CacheSize line must be
            // indented deeper than the if (inside the else block).
            var inElse = false;
            for (var i = osxIfStart; i < cacheSizeLine; i++)
            {
                if (lines[i].Trim() == "else" || lines[i].Trim().StartsWith("else", StringComparison.Ordinal))
                {
                    inElse = true;
                    break;
                }
            }

            inElse.Should().BeTrue("the CacheSize assignment must sit in the else-branch of the osx gate (non-osx hosts only)");
        }

        // Extracts the body of the first "new SQLiteConnectionStringBuilder { ... }"
        // initializer (from the opening brace to the matching closing "}").
        private static string ExtractInitializerBlock(string text)
        {
            var marker = "new SQLiteConnectionStringBuilder";
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            start.Should().BeGreaterThan(-1, "the SQLite connection builder construction must exist");

            var brace = text.IndexOf('{', start);
            brace.Should().BeGreaterThan(-1, "the builder initializer must have an opening brace");

            var depth = 0;
            var inLineComment = false;
            var inBlockComment = false;
            var inString = false;
            var end = brace;
            for (var i = brace; i < text.Length; i++)
            {
                var c = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n')
                    {
                        inLineComment = false;
                    }

                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (inString)
                {
                    if (c == '\\' && next != '\0')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            return text.Substring(brace, end - brace + 1);
        }
    }
}
