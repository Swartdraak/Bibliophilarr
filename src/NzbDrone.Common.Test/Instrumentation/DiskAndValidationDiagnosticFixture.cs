using System;
using System.IO;
using System.IO.Abstractions;
using System.Text.RegularExpressions;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Test.Instrumentation
{
    [TestFixture]
    [NonParallelizable]
    public class DiskAndValidationDiagnosticFixture
    {
        [Test]
        public void should_report_failed_mount_as_one_line_and_return_no_mount()
        {
            var filesystem = new Mock<IFileSystem>();
            filesystem.Setup(system => system.DriveInfo.GetDrives())
                .Throws(new IOException("Mount information is unavailable." + Environment.NewLine + "The volume is offline."));
            var provider = new Mock<DiskProviderBase>(filesystem.Object) { CallBase = true }.Object;
            var path = Path.GetTempPath();

            var diagnostic = Capture("DiskProviderBase", () => provider.GetMount(path).Should().BeNull());

            diagnostic.Should().Be($"Failed to get mount for path {path}. IOException: Mount information is unavailable.The volume is offline.|");
        }

        [Test]
        public void should_report_failed_writability_probe_as_one_line_and_return_false()
        {
            var directory = Directory.CreateTempSubdirectory("bibliophilarr-diagnostic-");
            try
            {
                var filesystem = new Mock<IFileSystem>();
                filesystem.Setup(system => system.File.Delete(Path.Combine(directory.FullName, "bibliophilarr_write_test.txt")))
                    .Throws(new IOException("The probe could not be removed." + Environment.NewLine + "The volume is read-only."));
                var provider = new Mock<DiskProviderBase>(filesystem.Object) { CallBase = true }.Object;

                var diagnostic = Capture("DiskProviderBase", () => provider.FolderWritable(directory.FullName).Should().BeFalse());

                diagnostic.Should().Be($"Directory '{directory.FullName}' isn't writable. The probe could not be removed.The volume is read-only.|");
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        }

        [Test]
        public void should_keep_validation_exception_details_and_log_one_line()
        {
            const string text = "Ordinary text";
            var pattern = new Regex("First line" + Environment.NewLine + "Second line");

            var diagnostic = Capture("ArgumentValidator", () =>
            {
                var error = Assert.Throws<ArgumentException>(() => Ensure.That(text, nameof(text)).Matches(pattern));
                error.ParamName.Should().Be(nameof(text));
                error.Message.Should().Contain(text).And.Contain(pattern.ToString());
            });

            diagnostic.Should().Contain(text).And.Contain("First lineSecond line");
            diagnostic.Should().NotContain("\r").And.NotContain("\n").And.EndWith("|");
        }

        private static string Capture(string loggerName, Action action)
        {
            // Initialize the application's static logging configuration before replacing it for this test.
            NzbDroneLogger.GetLogger(typeof(DiskProviderBase));
            var previous = LogManager.Configuration;
            var target = new MemoryTarget { Layout = "${message}|${exception:format=ToString}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, target, loggerName);
            LogManager.Configuration = configuration;
            try
            {
                action();
                return target.Logs.Should().ContainSingle().Which;
            }
            finally
            {
                LogManager.Configuration = previous;
            }
        }
    }
}
