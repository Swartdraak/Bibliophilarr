using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.CustomScript
{
    public class CustomScript : NotificationBase<CustomScriptSettings>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;

        public CustomScript(IDiskProvider diskProvider, IProcessProvider processProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _processProvider = processProvider;
            _logger = logger;
        }

        public override string Name => "Custom Script";

        public override string Link => "https://github.com/Swartdraak/Bibliophilarr/wiki/settings#connections";

        public override ProviderMessage Message => new ProviderMessage("Testing will execute the script with the EventType set to Test, ensure your script handles this correctly", ProviderMessageType.Warning);

        public override void OnGrab(GrabMessage message)
        {
            var author = message.Author;
            var remoteBook = message.RemoteBook;
            var releaseGroup = remoteBook.ParsedBookInfo.ReleaseGroup;
            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "Grab");
            AddScriptVariable(environmentVariables, "Author_Id", author.Id.ToString());
            AddScriptVariable(environmentVariables, "Author_Name", author.Metadata.Value.Name);
            AddScriptVariable(environmentVariables, "Author_GRId", author.Metadata.Value.ForeignAuthorId);
            AddScriptVariable(environmentVariables, "Release_BookCount", remoteBook.Books.Count.ToString());
            AddScriptVariable(environmentVariables, "Release_BookReleaseDates", string.Join(",", remoteBook.Books.Select(e => e.ReleaseDate)));
            AddScriptVariable(environmentVariables, "Release_BookTitles", string.Join("|", remoteBook.Books.Select(e => e.Title)));
            AddScriptVariable(environmentVariables, "Release_BookIds", string.Join("|", remoteBook.Books.Select(e => e.Id.ToString())));
            AddScriptVariable(environmentVariables, "Release_GRIds", remoteBook.Books
                .Select(GetPreferredForeignEditionId)
                .Where(x => x.IsNotNullOrWhiteSpace())
                .ConcatToString("|"));
            AddScriptVariable(environmentVariables, "Release_Title", remoteBook.Release.Title);
            AddScriptVariable(environmentVariables, "Release_Indexer", remoteBook.Release.Indexer ?? string.Empty);
            AddScriptVariable(environmentVariables, "Release_Size", remoteBook.Release.Size.ToString());
            AddScriptVariable(environmentVariables, "Release_Quality", remoteBook.ParsedBookInfo.Quality.Quality.Name);
            AddScriptVariable(environmentVariables, "Release_QualityVersion", remoteBook.ParsedBookInfo.Quality.Revision.Version.ToString());
            AddScriptVariable(environmentVariables, "Release_ReleaseGroup", releaseGroup ?? string.Empty);
            AddScriptVariable(environmentVariables, "Release_IndexerFlags", remoteBook.Release.IndexerFlags.ToString());
            AddScriptVariable(environmentVariables, "Download_Client", message.DownloadClientName ?? string.Empty);
            AddScriptVariable(environmentVariables, "Download_Client_Type", message.DownloadClientType ?? string.Empty);
            AddScriptVariable(environmentVariables, "Download_Id", message.DownloadId ?? string.Empty);

            ExecuteScript(environmentVariables);
        }

        public override void OnReleaseImport(BookDownloadMessage message)
        {
            var author = message.Author;
            var book = message.Book;
            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "Download");
            AddScriptVariable(environmentVariables, "Author_Id", author.Id.ToString());
            AddScriptVariable(environmentVariables, "Author_Name", author.Metadata.Value.Name);
            AddScriptVariable(environmentVariables, "Author_Path", author.Path);
            AddScriptVariable(environmentVariables, "Author_GRId", author.Metadata.Value.ForeignAuthorId);
            AddScriptVariable(environmentVariables, "Book_Id", book.Id.ToString());
            AddScriptVariable(environmentVariables, "Book_Title", book.Title);
            AddScriptVariable(environmentVariables, "Book_GRId", GetPreferredForeignEditionId(book));
            AddScriptVariable(environmentVariables, "Book_ReleaseDate", book.ReleaseDate.ToString());
            AddScriptVariable(environmentVariables, "Download_Client", message.DownloadClientInfo?.Name ?? string.Empty);
            AddScriptVariable(environmentVariables, "Download_Client_Type", message.DownloadClientInfo?.Type ?? string.Empty);
            AddScriptVariable(environmentVariables, "Download_Id", message.DownloadId ?? string.Empty);

            if (message.BookFiles.Any())
            {
                AddScriptVariable(environmentVariables, "AddedBookPaths", string.Join("|", message.BookFiles.Select(e => e.Path)));
            }

            if (message.OldFiles.Any())
            {
                AddScriptVariable(environmentVariables, "DeletedPaths", string.Join("|", message.OldFiles.Select(e => e.Path)));
                AddScriptVariable(environmentVariables, "DeletedDateAdded", string.Join("|", message.OldFiles.Select(e => e.DateAdded)));
            }

            ExecuteScript(environmentVariables);
        }

        public override void OnRename(Author author, List<RenamedBookFile> renamedFiles)
        {
            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "Rename");
            AddScriptVariable(environmentVariables, "Author_Id", author.Id.ToString());
            AddScriptVariable(environmentVariables, "Author_Name", author.Metadata.Value.Name);
            AddScriptVariable(environmentVariables, "Author_Path", author.Path);
            AddScriptVariable(environmentVariables, "Author_GRId", author.Metadata.Value.ForeignAuthorId);

            ExecuteScript(environmentVariables);
        }

        public override void OnAuthorAdded(Author author)
        {
            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "AuthorAdded");
            AddScriptVariable(environmentVariables, "Author_Id", author.Id.ToString());
            AddScriptVariable(environmentVariables, "Author_Name", author.Metadata.Value.Name);
            AddScriptVariable(environmentVariables, "Author_Path", author.Path);
            AddScriptVariable(environmentVariables, "Author_GRId", author.Metadata.Value.ForeignAuthorId);

            ExecuteScript(environmentVariables);
        }

        public override void OnAuthorDelete(AuthorDeleteMessage deleteMessage)
        {
            var author = deleteMessage.Author;
            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "AuthorDelete");
            AddScriptVariable(environmentVariables, "Author_Id", author.Id.ToString());
            AddScriptVariable(environmentVariables, "Author_Name", author.Name);
            AddScriptVariable(environmentVariables, "Author_Path", author.Path);
            AddScriptVariable(environmentVariables, "Author_OpenLibraryId", author.ForeignAuthorId);
            AddScriptVariable(environmentVariables, "Author_DeletedFiles", deleteMessage.DeletedFiles.ToString());

            ExecuteScript(environmentVariables);
        }

        public override void OnBookDelete(BookDeleteMessage deleteMessage)
        {
            var author = deleteMessage.Book.Author.Value;
            var book = deleteMessage.Book;

            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "BookDelete");
            AddScriptVariable(environmentVariables, "Author_Id", author.Id.ToString());
            AddScriptVariable(environmentVariables, "Author_Name", author.Name);
            AddScriptVariable(environmentVariables, "Author_Path", author.Path);
            AddScriptVariable(environmentVariables, "Author_OpenLibraryId", author.ForeignAuthorId);
            AddScriptVariable(environmentVariables, "Book_Id", book.Id.ToString());
            AddScriptVariable(environmentVariables, "Book_Title", book.Title);
            AddScriptVariable(environmentVariables, "Book_OpenLibraryId", book.ForeignBookId);
            AddScriptVariable(environmentVariables, "Book_DeletedFiles", deleteMessage.DeletedFiles.ToString());

            ExecuteScript(environmentVariables);
        }

        public override void OnBookFileDelete(BookFileDeleteMessage deleteMessage)
        {
            var author = deleteMessage.Book.Author.Value;
            var book = deleteMessage.Book;
            var bookFile = deleteMessage.BookFile;
            var edition = bookFile.Edition.Value;

            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "BookFileDelete");
            AddScriptVariable(environmentVariables, "Delete_Reason", deleteMessage.Reason.ToString());
            AddScriptVariable(environmentVariables, "Author_Id", author.Id.ToString());
            AddScriptVariable(environmentVariables, "Author_Name", author.Name);
            AddScriptVariable(environmentVariables, "Author_OpenLibraryId", author.ForeignAuthorId);
            AddScriptVariable(environmentVariables, "Book_Id", book.Id.ToString());
            AddScriptVariable(environmentVariables, "Book_Title", book.Title);
            AddScriptVariable(environmentVariables, "Book_OpenLibraryId", book.ForeignBookId);
            AddScriptVariable(environmentVariables, "BookFile_Id", bookFile.Id.ToString());
            AddScriptVariable(environmentVariables, "BookFile_Path", bookFile.Path);
            AddScriptVariable(environmentVariables, "BookFile_Quality", bookFile.Quality.Quality.Name);
            AddScriptVariable(environmentVariables, "BookFile_QualityVersion", bookFile.Quality.Revision.Version.ToString());
            AddScriptVariable(environmentVariables, "BookFile_ReleaseGroup", bookFile.ReleaseGroup ?? string.Empty);
            AddScriptVariable(environmentVariables, "BookFile_SceneName", bookFile.SceneName ?? string.Empty);
            AddScriptVariable(environmentVariables, "BookFile_Edition_Id", edition.Id.ToString());
            AddScriptVariable(environmentVariables, "BookFile_Edition_Name", edition.Title);
            AddScriptVariable(environmentVariables, "BookFile_Edition_OpenLibraryId", edition.ForeignEditionId);
            AddScriptVariable(environmentVariables, "BookFile_Edition_Isbn13", edition.Isbn13);
            AddScriptVariable(environmentVariables, "BookFile_Edition_Asin", edition.Asin);

            ExecuteScript(environmentVariables);
        }

        public override void OnBookRetag(BookRetagMessage message)
        {
            var author = message.Author;
            var book = message.Book;
            var bookFile = message.BookFile;
            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "TrackRetag");
            AddScriptVariable(environmentVariables, "Author_Id", author.Id.ToString());
            AddScriptVariable(environmentVariables, "Author_Name", author.Metadata.Value.Name);
            AddScriptVariable(environmentVariables, "Author_Path", author.Path);
            AddScriptVariable(environmentVariables, "Author_GRId", author.Metadata.Value.ForeignAuthorId);
            AddScriptVariable(environmentVariables, "Book_Id", book.Id.ToString());
            AddScriptVariable(environmentVariables, "Book_Title", book.Title);
            AddScriptVariable(environmentVariables, "Book_GRId", GetPreferredForeignEditionId(book));
            AddScriptVariable(environmentVariables, "Book_ReleaseDate", book.ReleaseDate.ToString());
            AddScriptVariable(environmentVariables, "BookFile_Id", bookFile.Id.ToString());
            AddScriptVariable(environmentVariables, "BookFile_Path", bookFile.Path);
            AddScriptVariable(environmentVariables, "BookFile_Quality", bookFile.Quality.Quality.Name);
            AddScriptVariable(environmentVariables, "BookFile_QualityVersion", bookFile.Quality.Revision.Version.ToString());
            AddScriptVariable(environmentVariables, "BookFile_ReleaseGroup", bookFile.ReleaseGroup ?? string.Empty);
            AddScriptVariable(environmentVariables, "BookFile_SceneName", bookFile.SceneName ?? string.Empty);
            AddScriptVariable(environmentVariables, "Tags_Diff", message.Diff.ToJson());
            AddScriptVariable(environmentVariables, "Tags_Scrubbed", message.Scrubbed.ToString());

            ExecuteScript(environmentVariables);
        }

        public override void OnHealthIssue(HealthCheck.HealthCheck healthCheck)
        {
            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "HealthIssue");
            AddScriptVariable(environmentVariables, "Health_Issue_Level", Enum.GetName(typeof(HealthCheckResult), healthCheck.Type));
            AddScriptVariable(environmentVariables, "Health_Issue_Message", healthCheck.Message);
            AddScriptVariable(environmentVariables, "Health_Issue_Type", healthCheck.Source.Name);
            AddScriptVariable(environmentVariables, "Health_Issue_Wiki", healthCheck.WikiUrl.ToString() ?? string.Empty);

            ExecuteScript(environmentVariables);
        }

        public override void OnApplicationUpdate(ApplicationUpdateMessage updateMessage)
        {
            var environmentVariables = new StringDictionary();

            AddScriptVariable(environmentVariables, "EventType", "ApplicationUpdate");
            AddScriptVariable(environmentVariables, "Update_Message", updateMessage.Message);
            AddScriptVariable(environmentVariables, "Update_NewVersion", updateMessage.NewVersion.ToString());
            AddScriptVariable(environmentVariables, "Update_PreviousVersion", updateMessage.PreviousVersion.ToString());

            ExecuteScript(environmentVariables);
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            if (!_diskProvider.FileExists(Settings.Path))
            {
                failures.Add(new NzbDroneValidationFailure("Path", "File does not exist"));
            }

            if (failures.Empty())
            {
                try
                {
                    var environmentVariables = new StringDictionary();
                    AddScriptVariable(environmentVariables, "EventType", "Test");

                    var processOutput = ExecuteScript(environmentVariables);

                    if (processOutput.ExitCode != 0)
                    {
                        failures.Add(new NzbDroneValidationFailure(string.Empty, $"Script exited with code: {processOutput.ExitCode}"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                    failures.Add(new NzbDroneValidationFailure(string.Empty, ex.Message));
                }
            }

            return new ValidationResult(failures);
        }

        private static void AddScriptVariable(StringDictionary environmentVariables, string key, string value)
        {
            environmentVariables.Add($"Bibliophilarr_{key}", value);
            environmentVariables.Add($"Bibliophilarr_{key}", value);
        }

        private static string GetPreferredForeignEditionId(Book book)
        {
            return book.GetPreferredEdition()?.ForeignEditionId ?? string.Empty;
        }

        private ProcessOutput ExecuteScript(StringDictionary environmentVariables)
        {
            _logger.Debug("Executing external script: {0}", Settings.Path);

            var processOutput = _processProvider.StartAndCapture(Settings.Path, Settings.Arguments, environmentVariables);

            _logger.Debug("Executed external script: {0} - Status: {1}", Settings.Path, processOutput.ExitCode);
            _logger.Debug($"Script Output: {System.Environment.NewLine}{string.Join(System.Environment.NewLine, processOutput.Lines)}");

            return processOutput;
        }
    }
}
