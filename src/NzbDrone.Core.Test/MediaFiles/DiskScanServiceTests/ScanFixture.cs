using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.DiskScanServiceTests
{
    [TestFixture]
    public class ScanFixture : FileSystemTest<DiskScanService>
    {
        private Author _author;
        private string _rootFolder;
        private string _otherAuthorFolder;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConnectionStringFactory>()
                .Setup(factory => factory.MainDbConnection)
                .Returns(new DatabaseConnectionInfo(DatabaseType.SQLite, string.Empty));

            _rootFolder = @"C:\Test\Music".AsOsAgnostic();
            _otherAuthorFolder = @"C:\Test\Music\OtherAuthor".AsOsAgnostic();
            var authorFolder = @"C:\Test\Music\Author".AsOsAgnostic();

            _author = Builder<Author>.CreateNew()
                                     .With(s => s.Path = authorFolder)
                                     .Build();

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                .Returns(new RootFolder { Path = _rootFolder });

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAuthors(It.IsAny<List<int>>()))
                .Returns(new List<Author>());

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(v => v.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(v => v.GetFilesWithBasePath(It.IsAny<string>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(v => v.FilterUnchangedFiles(It.IsAny<List<IFileInfo>>(), It.IsAny<FilterFilesType>()))
                .Returns((List<IFileInfo> files, FilterFilesType filter) => files);
        }

        private void GivenRootFolder(params string[] subfolders)
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(_rootFolder))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetDirectories(_rootFolder))
                .Returns(subfolders);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderEmpty(_rootFolder))
                .Returns(subfolders.Empty());

            FileSystem.AddDirectory(_rootFolder);

            foreach (var folder in subfolders)
            {
                Mocker.GetMock<IDiskProvider>()
                    .Setup(s => s.FolderExists(folder))
                    .Returns(true);

                FileSystem.AddDirectory(folder);
            }
        }

        private void GivenAuthorFolder()
        {
            GivenRootFolder(_author.Path);
        }

        private void GivenFiles(IEnumerable<string> files, DateTimeOffset? lastWrite = null)
        {
            if (lastWrite == null)
            {
                TestLogger.Debug("Using default lastWrite");
                lastWrite = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            foreach (var file in files)
            {
                FileSystem.AddFile(file, new MockFileData(string.Empty) { LastWriteTime = lastWrite.Value });
            }

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFileInfos(It.IsAny<string>(), true, false))
                .Returns(files.Select(x => DiskProvider.GetFileInfo(x)).ToList());
        }

        private void GivenKnownFiles(IEnumerable<string> files, DateTimeOffset? lastWrite = null)
        {
            if (lastWrite == null)
            {
                TestLogger.Debug("Using default lastWrite");
                lastWrite = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesWithBasePath(_author.Path))
                .Returns(files.Select(x => new BookFile
                {
                    Path = x,
                    Modified = lastWrite.Value.UtcDateTime
                }).ToList());
        }

        [Test]
        public void should_not_scan_if_root_folder_does_not_exist()
        {
            Subject.Scan(new List<string> { _author.Path });

            ExceptionVerification.ExpectedWarns(1);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.FolderExists(_author.Path), Times.Never());

            Mocker.GetMock<IMediaFileTableCleanupService>()
                  .Verify(v => v.Clean(It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never());

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Never());
        }

        [Test]
        public void should_not_scan_if_root_folder_is_empty()
        {
            GivenRootFolder();

            Subject.Scan(new List<string> { _author.Path });

            ExceptionVerification.ExpectedWarns(1);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.GetFiles(_author.Path, true), Times.Never());

            Mocker.GetMock<IMediaFileTableCleanupService>()
                  .Verify(v => v.Clean(It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never());

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Never());
        }

        [Test]
        public void should_clean_if_folder_does_not_exist()
        {
            GivenRootFolder(_otherAuthorFolder);
            Mocker.GetMock<IDiskProvider>()
                .Setup(provider => provider.GetFileInfos(_author.Path, true, false))
                .Throws(new DirectoryNotFoundException());

            Subject.Scan(new List<string> { _author.Path });

            DiskProvider.FolderExists(_author.Path).Should().BeFalse();

            Mocker.GetMock<IMediaFileTableCleanupService>()
                  .Verify(v => v.Clean(_author.Path, It.Is<List<string>>(paths => paths.Count == 0)), Times.Once());
        }

        [Test]
        public void should_keep_tracked_files_if_an_explicit_folder_has_an_unreadable_ancestor()
        {
            GivenRootFolder(_otherAuthorFolder);
            Mocker.GetMock<IDiskProvider>()
                .Setup(provider => provider.GetFileInfos(_author.Path, true, false))
                .Throws(new UnauthorizedAccessException());

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMediaFileTableCleanupService>()
                .Verify(cleanup => cleanup.Clean(_author.Path, It.IsAny<List<string>>()), Times.Never());
            Mocker.GetMock<ICommandResultReporter>()
                .Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_find_files_at_root_of_author_folder()
        {
            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, "file1.mobi"),
                           Path.Combine(_author.Path, "s01e01.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 2), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_not_scan_extras_subfolder()
        {
            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, "EXTRAS", "file1.mobi"),
                           Path.Combine(_author.Path, "Extras", "file2.mobi"),
                           Path.Combine(_author.Path, "EXTRAs", "file3.mobi"),
                           Path.Combine(_author.Path, "ExTrAs", "file4.mobi"),
                           Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.GetFileInfos(It.IsAny<string>(), true, false), Times.Once());

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_not_scan_AppleDouble_subfolder()
        {
            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, ".AppleDouble", "file1.mobi"),
                           Path.Combine(_author.Path, ".appledouble", "file2.mobi"),
                           Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_scan_extras_author_and_subfolders()
        {
            _author.Path = @"C:\Test\Music\Extras".AsOsAgnostic();

            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, "Extras", "file1.mobi"),
                           Path.Combine(_author.Path, ".AppleDouble", "file2.mobi"),
                           Path.Combine(_author.Path, "Season 1", "s01e01.mobi"),
                           Path.Combine(_author.Path, "Season 1", "s01e02.mobi"),
                           Path.Combine(_author.Path, "Season 2", "s02e01.mobi"),
                           Path.Combine(_author.Path, "Season 2", "s02e02.mobi"),
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 4), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_scan_files_that_start_with_period()
        {
            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, "Book 1", ".t01.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_not_scan_subfolders_that_start_with_period()
        {
            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, ".@__thumb", "file1.mobi"),
                           Path.Combine(_author.Path, ".@__THUMB", "file2.mobi"),
                           Path.Combine(_author.Path, ".hidden", "file2.mobi"),
                           Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_not_scan_subfolder_of_season_folder_that_starts_with_a_period()
        {
            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, "Season 1", ".@__thumb", "file1.mobi"),
                           Path.Combine(_author.Path, "Season 1", ".@__THUMB", "file2.mobi"),
                           Path.Combine(_author.Path, "Season 1", ".hidden", "file2.mobi"),
                           Path.Combine(_author.Path, "Season 1", ".AppleDouble", "s01e01.mobi"),
                           Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_not_scan_Synology_eaDir()
        {
            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, "@eaDir", "file1.mobi"),
                           Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_not_scan_thumb_folder()
        {
            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, ".@__thumb", "file1.mobi"),
                           Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_scan_dotHack_folder()
        {
            _author.Path = @"C:\Test\Music\.hack".AsOsAgnostic();

            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, "Season 1", "file1.mobi"),
                           Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 2), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        [Test]
        public void should_exclude_osx_metadata_files()
        {
            GivenAuthorFolder();

            GivenFiles(new List<string>
                       {
                           Path.Combine(_author.Path, ".DS_STORE"),
                           Path.Combine(_author.Path, "._24 The Status Quo Combustion.mobi"),
                           Path.Combine(_author.Path, "24 The Status Quo Combustion.mobi")
                       });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMakeImportDecision>()
                .Verify(v => v.GetImportDecisions(It.Is<List<IFileInfo>>(l => l.Count == 1), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()), Times.Once());
        }

        private void GivenRejections(Edition candidate = null, LibraryImportHoldReason? holdReason = null)
        {
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(x => x.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<IFileInfo> fileList, IdentificationOverrides idOverrides, ImportDecisionMakerInfo idInfo, ImportDecisionMakerConfig idConfig) =>
                          fileList.Select(x => new LocalBook
                          {
                              Author = _author,
                              Edition = candidate,
                              Path = x.FullName,
                              Modified = x.LastWriteTimeUtc,
                              FileTrackInfo = new ParsedTrackInfo()
                          })
                          .Select(x => new ImportDecision<LocalBook>(x, new Rejection("Reject", holdReason: holdReason)))
                          .ToList());
        }

        [Test]
        public void should_insert_new_unmatched_files_when_all_new()
        {
            GivenAuthorFolder();

            var files = new List<string>
            {
                Path.Combine(_author.Path, "Season 1", "file1.mobi"),
                Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
            };

            GivenFiles(files);
            GivenKnownFiles(new List<string>());
            GivenRejections();

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.AddMany(It.Is<List<BookFile>>(l => l.Select(t => t.Path).SequenceEqual(files))),
                        Times.Once());
        }

        [Test]
        public void should_track_rejected_candidate_without_linking_its_edition()
        {
            GivenAuthorFolder();
            var path = Path.Combine(_author.Path, "rejected.epub");
            GivenFiles(new List<string> { path });
            GivenKnownFiles(new List<string>());
            GivenRejections(new Edition { Id = 8, BookId = 9 }, LibraryImportHoldReason.LowConfidence);

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMediaFileService>().Verify(service => service.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 &&
                files[0].Path == path &&
                files[0].EditionId == 0 &&
                files[0].Edition == null &&
                files[0].UnmappedReason == LibraryImportHoldReason.LowConfidence)), Times.Once);
        }

        [Test]
        public void should_insert_new_unmatched_files_when_some_known()
        {
            GivenAuthorFolder();

            var files = new List<string>
            {
                Path.Combine(_author.Path, "Season 1", "file1.mobi"),
                Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
            };

            GivenFiles(files);
            GivenKnownFiles(files.GetRange(1, 1));
            GivenRejections();

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.AddMany(It.Is<List<BookFile>>(l => l.Select(t => t.Path).SequenceEqual(files.GetRange(0, 1)))),
                        Times.Once());
        }

        [Test]
        public void should_not_insert_files_when_all_known()
        {
            GivenAuthorFolder();

            var files = new List<string>
            {
                Path.Combine(_author.Path, "Season 1", "file1.mobi"),
                Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
            };

            GivenFiles(files);
            GivenKnownFiles(files);
            GivenRejections();

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.AddMany(It.Is<List<BookFile>>(l => l.Count == 0)),
                        Times.Once());

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.AddMany(It.Is<List<BookFile>>(l => l.Count > 0)),
                        Times.Never());
        }

        [Test]
        public void should_not_update_info_for_unchanged_known_files()
        {
            GivenAuthorFolder();

            var files = new List<string>
            {
                Path.Combine(_author.Path, "Season 1", "file1.mobi"),
                Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
            };

            GivenFiles(files);
            GivenKnownFiles(files);
            GivenRejections();

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.UpdateScannedFileInfo(It.Is<List<BookFile>>(l => l.Count == 0)),
                        Times.Once());

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.UpdateScannedFileInfo(It.Is<List<BookFile>>(l => l.Count > 0)),
                        Times.Never());
        }

        [Test]
        public void should_report_an_unavailable_root_and_still_import_files_from_the_other_root()
        {
            GivenAuthorFolder();
            var availableFile = Path.Combine(_author.Path, "available.epub");
            var unavailableRoot = @"C:\OtherLibrary".AsOsAgnostic();
            GivenFiles(new[] { availableFile });
            GivenRejections();
            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetBestRootFolder(unavailableRoot))
                .Returns(new RootFolder { Path = unavailableRoot });

            Subject.Execute(new RescanFoldersCommand(
                new List<string> { unavailableRoot, _author.Path },
                FilterFilesType.Matched,
                false,
                null));

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 && files[0].Path == availableFile)), Times.Once);
        }

        [Test]
        public void should_keep_records_for_a_partially_readable_folder_and_still_scan_other_folders()
        {
            var unreadable = Path.Combine(_rootFolder, "Unreadable");
            GivenRootFolder(_author.Path, unreadable);
            var readableFile = Path.Combine(_author.Path, "available.epub");
            var partialFile = Path.Combine(unreadable, "sibling.epub");
            GivenFiles(new[] { readableFile, partialFile });
            GivenRejections();
            Mocker.GetMock<IDiskProvider>()
                .Setup(provider => provider.GetFileInfos(unreadable, true, true))
                .Returns(new List<IFileInfo> { DiskProvider.GetFileInfo(partialFile) });
            Mocker.GetMock<IDiskProvider>()
                .Setup(provider => provider.GetFileInfos(_author.Path, true, true))
                .Returns(new List<IFileInfo> { DiskProvider.GetFileInfo(readableFile) });
            Mocker.GetMock<IDiskProvider>()
                .Setup(provider => provider.GetFileInfos(_author.Path, true, false))
                .Returns(new List<IFileInfo> { DiskProvider.GetFileInfo(readableFile) });
            Mocker.GetMock<IDiskProvider>()
                .Setup(provider => provider.GetFileInfos(unreadable, true, false))
                .Throws(new UnauthorizedAccessException());

            Subject.Scan(new List<string> { unreadable, _author.Path });

            Mocker.GetMock<IMediaFileTableCleanupService>().Verify(cleanup => cleanup.Clean(unreadable, It.IsAny<List<string>>()), Times.Never);
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 && files[0].Path == readableFile)), Times.Once);
        }

        [Test]
        public void should_record_an_import_execution_failure_on_an_unlinked_file()
        {
            GivenAuthorFolder();
            var file = Path.Combine(_author.Path, "failed.epub");
            GivenFiles(new[] { file });
            var decision = new ImportDecision<LocalBook>(new LocalBook { Path = file, FileTrackInfo = new ParsedTrackInfo() });
            var decisions = new List<ImportDecision<LocalBook>> { decision };
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(service => service.GetImportDecisions(It.IsAny<List<IFileInfo>>(), null, null, It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(decisions);
            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(service => service.Import(decisions, false, null, ImportMode.Auto))
                .Returns(new List<ImportResult> { new ImportResult(decision, LibraryImportHoldReason.ImportFailed, "I/O failed") });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 && files[0].UnmappedReason == LibraryImportHoldReason.ImportFailed)), Times.Once);
        }

        [Test]
        public void should_preserve_existing_file_and_report_a_failed_read()
        {
            GivenAuthorFolder();
            var path = Path.Combine(_author.Path, "linked.m4b");
            GivenFiles(new[] { path });
            var existing = new BookFile
            {
                Id = 42,
                Path = path,
                EditionId = 19,
                Size = 1234,
                Modified = DateTime.UtcNow,
                Quality = new QualityModel(Quality.M4B)
            };
            Mocker.GetMock<IMediaFileService>()
                .Setup(service => service.GetFilesWithBasePath(_author.Path))
                .Returns(new List<BookFile> { existing });
            var failedRead = new ImportDecision<LocalBook>(
                new LocalBook { Path = path },
                new Rejection("fixture read failure", holdReason: LibraryImportHoldReason.ReadError));
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(service => service.GetImportDecisions(It.IsAny<List<IFileInfo>>(), null, null, It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(new List<ImportDecision<LocalBook>> { failedRead });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);
            Mocker.GetMock<IMediaFileService>().Verify(service => service.UpdateScannedFileInfo(It.Is<List<BookFile>>(files => files.Count == 0)), Times.Once);
            existing.EditionId.Should().Be(19);
            existing.Size.Should().Be(1234);
            existing.Quality.Quality.Should().Be(Quality.M4B);
        }

        [TestCase(null, LibraryImportHoldReason.ReviewRequired)]
        [TestCase(LibraryImportHoldReason.LowConfidence, LibraryImportHoldReason.LowConfidence)]
        [TestCase(LibraryImportHoldReason.AuthorBibliographyUnavailable, LibraryImportHoldReason.AuthorBibliographyUnavailable)]
        [TestCase(LibraryImportHoldReason.AuthorIdentityConfirmationRequired, LibraryImportHoldReason.AuthorIdentityConfirmationRequired)]
        [TestCase(LibraryImportHoldReason.CompetingEditions, LibraryImportHoldReason.CompetingEditions)]
        public void should_record_why_new_files_remain_unmapped(LibraryImportHoldReason? holdReason, LibraryImportHoldReason expectedReason)
        {
            GivenAuthorFolder();
            GivenFiles(new[] { Path.Combine(_author.Path, "unmatched.epub") });
            GivenRejections(holdReason: holdReason);

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMediaFileService>()
                .Verify(service => service.AddMany(It.Is<List<BookFile>>(files =>
                    files.Count == 1 && files[0].UnmappedReason == expectedReason)));
        }

        [TestCase(LibraryImportHoldReason.AuthorBibliographyUnavailable, false)]
        [TestCase(LibraryImportHoldReason.ReadError, true)]
        public void should_keep_the_prior_audiobook_slot_hold_across_an_unsuccessful_retry(
            LibraryImportHoldReason currentReason, bool shouldReportFailure)
        {
            GivenAuthorFolder();
            var path = Path.Combine(_author.Path, "held.m4b");
            GivenFiles(new[] { path });
            GivenRejections(holdReason: currentReason);
            var held = new BookFile { Id = 41, Path = path, Part = 2, UnmappedReason = LibraryImportHoldReason.DuplicateInScan };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetFilesWithBasePath(_author.Path)).Returns(new List<BookFile> { held });

            Subject.Scan(new List<string> { _author.Path }, FilterFilesType.Matched);

            held.UnmappedReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            mediaFiles.Verify(service => service.SetUnmappedReasons(It.Is<List<BookFile>>(files => files.Count == 0)), Times.Once());
            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful),
                shouldReportFailure ? Times.Once() : Times.Never());
        }

        [TestCase(LibraryImportHoldReason.LowConfidence, LibraryImportHoldReason.NoAcceptedWork, FilterFilesType.Matched, false, 0, 0L, true)]
        [TestCase(LibraryImportHoldReason.AuthorBibliographyUnavailable, LibraryImportHoldReason.NoAcceptedWork, FilterFilesType.Matched, false, 0, 0L, true)]
        [TestCase(LibraryImportHoldReason.LowConfidence, LibraryImportHoldReason.NoAcceptedWork, FilterFilesType.Matched, false, 0, 7L, false)]
        [TestCase(LibraryImportHoldReason.LowConfidence, LibraryImportHoldReason.NoAcceptedWork, FilterFilesType.Matched, false, 0, 20000000L, false)]
        [TestCase(LibraryImportHoldReason.AuthorBibliographyUnavailable, LibraryImportHoldReason.NoAcceptedWork, FilterFilesType.Matched, false, 1, 0L, false)]
        [TestCase(LibraryImportHoldReason.AuthorBibliographyUnavailable, LibraryImportHoldReason.ConflictingIdentity, FilterFilesType.Matched, false, 0, 0L, false)]
        [TestCase(LibraryImportHoldReason.LowConfidence, LibraryImportHoldReason.NoAcceptedWork, FilterFilesType.Matched, true, 0, 0L, false)]
        [TestCase(LibraryImportHoldReason.LowConfidence, LibraryImportHoldReason.NoAcceptedWork, FilterFilesType.Known, false, 0, 0L, false)]
        [TestCase(LibraryImportHoldReason.LowConfidence, LibraryImportHoldReason.NoAcceptedWork, FilterFilesType.None, true, 0, 0L, false)]
        [TestCase(LibraryImportHoldReason.ExistingFilePreferred, LibraryImportHoldReason.NoAcceptedWork, FilterFilesType.Matched, false, 0, 0L, false)]
        public void should_keep_an_actionable_hold_only_for_an_unchanged_file_with_a_generic_matched_scan_miss(
            LibraryImportHoldReason priorReason,
            LibraryImportHoldReason currentReason,
            FilterFilesType filter,
            bool addNewAuthors,
            int addedSize,
            long addedTicks,
            bool preserveReason)
        {
            GivenAuthorFolder();
            var path = Path.Combine(_author.Path, "held.m4b");
            var savedModification = new DateTime(2024, 4, 2, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234560);
            var currentModification = savedModification.AddTicks(addedTicks);
            GivenFiles(new[] { path }, new DateTimeOffset(currentModification));
            var held = new BookFile
            {
                Id = 41,
                Path = path,
                Size = 1234,
                Modified = savedModification,
                UnmappedReason = priorReason
            };
            var decision = new ImportDecision<LocalBook>(
                new LocalBook { Path = path, Size = 1234 + addedSize, Modified = currentModification },
                new Rejection("fixture match outcome", holdReason: currentReason));
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetFilesWithBasePath(_author.Path)).Returns(new List<BookFile> { held });

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(service => service.GetImportDecisions(
                    It.IsAny<List<IFileInfo>>(),
                    null,
                    null,
                    It.Is<ImportDecisionMakerConfig>(config => config.Filter == filter && config.AddNewAuthors == addNewAuthors)))
                .Returns(new List<ImportDecision<LocalBook>> { decision });

            var progress = new MemoryTarget { Layout = "${message}" };
            var logging = new LoggingConfiguration();
            logging.AddRule(LogLevel.Info, LogLevel.Info, progress);
            using var logFactory = new LogFactory { Configuration = logging };
            Mocker.SetConstant(logFactory.GetLogger(nameof(DiskScanService)));

            Subject.Execute(new RescanFoldersCommand(new List<string> { _author.Path }, filter, addNewAuthors, null));

            var expectedReason = preserveReason ? priorReason : currentReason;
            held.UnmappedReason.Should().Be(expectedReason);
            progress.Logs.Where(message => message.StartsWith("Library scan left ", StringComparison.Ordinal))
                .Should().Equal($"Library scan left 1 evaluated files unmapped. {expectedReason}=1");
            mediaFiles.Verify(service => service.SetUnmappedReasons(It.Is<List<BookFile>>(files =>
                preserveReason ? files.Count == 0 : files.Count == 1 && files[0].Id == held.Id && files[0].UnmappedReason == currentReason)),
                Times.Once());
            if (addedSize > 0 || addedTicks > TimeSpan.TicksPerSecond)
            {
                mediaFiles.Verify(service => service.UpdateScannedFileInfo(It.Is<List<BookFile>>(files =>
                    files.Count == 1 && files[0].Modified == currentModification && files[0].Size == 1234 + addedSize)), Times.Once());
            }
        }

        [TestCase(2024, 0L, 7L, DateTimeKind.Utc, false, true)]
        [TestCase(2024, 0L, 10L, DateTimeKind.Utc, false, false)]
        [TestCase(1995, 10L, 7L, DateTimeKind.Utc, false, true)]
        [TestCase(1995, 0L, 7L, DateTimeKind.Utc, false, false)]
        [TestCase(2024, 0L, 0L, DateTimeKind.Unspecified, false, false)]
        [TestCase(2024, 0L, 0L, DateTimeKind.Utc, true, false)]
        public void should_compare_postgres_timestamps_at_their_stored_precision(
            int year, long savedTicks, long currentTicks, DateTimeKind savedKind, bool conflictingObservation, bool preserveReason)
        {
            GivenAuthorFolder();
            var path = Path.Combine(_author.Path, "held.m4b");
            var start = new DateTime(year, 4, 2, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234560);
            GivenFiles(new[] { path });
            Mocker.GetMock<IConnectionStringFactory>()
                .Setup(factory => factory.MainDbConnection)
                .Returns(new DatabaseConnectionInfo(DatabaseType.PostgreSQL, string.Empty));

            var held = new BookFile
            {
                Id = 41,
                Path = path,
                Size = 1234,
                Modified = DateTime.SpecifyKind(start.AddTicks(savedTicks), savedKind),
                UnmappedReason = LibraryImportHoldReason.AuthorBibliographyUnavailable
            };
            var decisions = new List<ImportDecision<LocalBook>>
            {
                new ImportDecision<LocalBook>(
                    new LocalBook { Path = path, Size = 1234, Modified = start.AddTicks(currentTicks) },
                    new Rejection("fixture match outcome", holdReason: LibraryImportHoldReason.NoAcceptedWork))
            };
            if (conflictingObservation)
            {
                decisions.Add(new ImportDecision<LocalBook>(
                    new LocalBook { Path = path, Size = 1235, Modified = start },
                    new Rejection("fixture observations disagree", holdReason: LibraryImportHoldReason.NoAcceptedWork)));
            }

            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetFilesWithBasePath(_author.Path)).Returns(new List<BookFile> { held });
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(service => service.GetImportDecisions(It.IsAny<List<IFileInfo>>(), null, null, It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(decisions);

            Subject.Execute(new RescanFoldersCommand(new List<string> { _author.Path }, FilterFilesType.Matched, false, null));

            held.UnmappedReason.Should().Be(preserveReason
                ? LibraryImportHoldReason.AuthorBibliographyUnavailable
                : LibraryImportHoldReason.NoAcceptedWork);
            mediaFiles.Verify(service => service.SetUnmappedReasons(It.Is<List<BookFile>>(files =>
                files.Count == (preserveReason ? 0 : 1))), Times.Once());
        }

        [Test]
        public void should_not_restore_a_previous_hold_after_a_matched_scan_links_the_file()
        {
            GivenAuthorFolder();
            var path = Path.Combine(_author.Path, "linked.m4b");
            GivenFiles(new[] { path });
            GivenRejections(holdReason: LibraryImportHoldReason.NoAcceptedWork);

            // The post-import read observes a committed link for the same path.
            var linked = new BookFile { Id = 41, Path = path, EditionId = 9, UnmappedReason = null };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetFilesWithBasePath(_author.Path)).Returns(new List<BookFile> { linked });

            Subject.Execute(new RescanFoldersCommand(new List<string> { _author.Path }, FilterFilesType.Matched, false, null));

            linked.EditionId.Should().Be(9);
            linked.UnmappedReason.Should().BeNull();
            mediaFiles.Verify(service => service.SetUnmappedReasons(It.Is<List<BookFile>>(files => files.Count == 0)), Times.Once());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_preserve_a_newer_reason_after_a_stale_occupied_retry_is_denied(bool retainFreshReason)
        {
            GivenAuthorFolder();
            var path = Path.Combine(_author.Path, "stale.m4b");
            GivenFiles(new[] { path });
            var latest = new BookFile { Id = 41, Path = path, Part = 2, UnmappedReason = LibraryImportHoldReason.ConflictingIdentity };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetFilesWithBasePath(_author.Path)).Returns(new List<BookFile> { latest });
            var decision = new ImportDecision<LocalBook>(new LocalBook { Path = path });
            var decisions = new List<ImportDecision<LocalBook>> { decision };
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(service => service.GetImportDecisions(It.IsAny<List<IFileInfo>>(), null, null, It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(decisions);
            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(service => service.Import(decisions, false, null, ImportMode.Auto))
                .Returns(new List<ImportResult>
                {
                    new ImportResult(decision, LibraryImportHoldReason.DuplicateInScan, "Occupied part was not linked")
                    {
                        PreserveExistingUnmappedReason = retainFreshReason
                    }
                });

            Subject.Scan(new List<string> { _author.Path }, FilterFilesType.Matched);

            latest.UnmappedReason.Should().Be(retainFreshReason
                ? LibraryImportHoldReason.ConflictingIdentity
                : LibraryImportHoldReason.DuplicateInScan);
            mediaFiles.Verify(service => service.SetUnmappedReasons(It.Is<List<BookFile>>(files =>
                files.Count == (retainFreshReason ? 0 : 1))), Times.Once());
        }

        [Test]
        public void should_preserve_both_rows_when_a_held_audiobook_path_is_ambiguous()
        {
            GivenAuthorFolder();
            var path = Path.Combine(_author.Path, "ambiguous.m4b");
            GivenFiles(new[] { path });
            GivenRejections(holdReason: LibraryImportHoldReason.DuplicateInScan);
            var legacy = new BookFile { Id = 41, Path = path, Size = 1, UnmappedReason = LibraryImportHoldReason.LowConfidence };
            var occupied = new BookFile { Id = 42, Path = Path.Combine(_author.Path, ".", "ambiguous.m4b"), Size = 2, UnmappedReason = LibraryImportHoldReason.DuplicateInScan };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetFilesWithBasePath(_author.Path))
                .Returns(new List<BookFile> { legacy, occupied });

            Subject.Scan(new List<string> { _author.Path });

            mediaFiles.Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
            mediaFiles.Verify(service => service.UpdateScannedFileInfo(It.Is<List<BookFile>>(files => files.Count == 0)), Times.Once());
            mediaFiles.Verify(service => service.SetUnmappedReasons(It.Is<List<BookFile>>(files => files.Count == 0)), Times.Once());
            legacy.Size.Should().Be(1);
            occupied.Size.Should().Be(2);
            legacy.UnmappedReason.Should().Be(LibraryImportHoldReason.LowConfidence);
            occupied.UnmappedReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
        }

        [Test]
        public void should_update_info_for_changed_known_files()
        {
            GivenAuthorFolder();

            var files = new List<string>
            {
                Path.Combine(_author.Path, "Season 1", "file1.mobi"),
                Path.Combine(_author.Path, "Season 1", "s01e01.mobi")
            };

            GivenFiles(files, new DateTime(2019, 2, 1));
            GivenKnownFiles(files);
            GivenRejections();

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.UpdateScannedFileInfo(It.Is<List<BookFile>>(l => l.Count == 2)),
                        Times.Once());
        }

        [Test]
        public void should_update_fields_for_updated_files()
        {
            GivenAuthorFolder();

            var files = new List<string>
            {
                Path.Combine(_author.Path, "Season 1", "file1.mobi"),
            };

            GivenKnownFiles(files);

            FileSystem.AddFile(files[0], new MockFileData("".PadRight(100)) { LastWriteTime = new DateTime(2019, 2, 1) });
            Mocker.GetMock<IDiskProvider>()
                .Setup(provider => provider.GetFileInfos(_author.Path, true, false))
                .Returns(new List<IFileInfo> { DiskProvider.GetFileInfo(files[0]) });

            var localTrack = Builder<LocalBook>.CreateNew()
                .With(x => x.Path = files[0])
                .With(x => x.Modified = new DateTime(2019, 2, 1))
                .With(x => x.Size = 100)
                .With(x => x.Quality = new QualityModel(Quality.MOBI))
                .With(x => x.FileTrackInfo = new ParsedTrackInfo
                {
                    MediaInfo = Builder<MediaInfoModel>.CreateNew().Build()
                })
                .Build();

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(x => x.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(new List<ImportDecision<LocalBook>> { new ImportDecision<LocalBook>(localTrack, new Rejection("Reject")) });

            Subject.Scan(new List<string> { _author.Path });

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.UpdateScannedFileInfo(It.Is<List<BookFile>>(
                                          l => l.Count == 1 &&
                                          l[0].Path == localTrack.Path &&
                                          l[0].Modified == localTrack.Modified &&
                                          l[0].Size == localTrack.Size &&
                                          l[0].Quality.Equals(localTrack.Quality) &&
                                          l[0].MediaInfo.AudioFormat == localTrack.FileTrackInfo.MediaInfo.AudioFormat)),
                        Times.Once());
        }
    }
}
