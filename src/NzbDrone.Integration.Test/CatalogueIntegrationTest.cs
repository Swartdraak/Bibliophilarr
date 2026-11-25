using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Runtime.CompilerServices;
using Bibliophilarr.Api.V1.Config;
using NLog;
using Npgsql;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test
{
    public abstract class CatalogueIntegrationTest : IntegrationTest
    {
        protected const string CatalogueAuthor = "Andrew Hunter Murray";
        protected const string CatalogueTitle = "The Last Day";

        protected void UseLegacyMonitoring()
        {
            // These cases exercise the book/author monitored filter without format profiles.
            var client = new ClientBase<MediaManagementConfigResource>(RestClient, ApiKey, "config/mediamanagement");
            var config = client.GetSingle();
            config.EnableDualFormatTracking = false;
            client.Put(config);
        }

        protected Author SeedAuthor(string authorName, bool monitored = true)
        {
            var database = OpenCatalogue();
            var authors = new AuthorRepository(database, null);
            var authorSlug = authorName.ToUrlSlug();
            var foreignId = $"test:author:{authorSlug}";
            var author = authors.FindById(foreignId);

            if (author != null)
            {
                author.Monitored = monitored;
                return authors.Update(author);
            }

            var metadata = new AuthorMetadataRepository(database, null, LogManager.GetCurrentClassLogger()).Insert(new AuthorMetadata
            {
                ForeignAuthorId = foreignId,
                TitleSlug = authorSlug,
                Name = authorName,
                NameLastFirst = authorName.ToLastFirst(),
                SortName = authorName.ToLowerInvariant(),
                SortNameLastFirst = authorName.ToLastFirst().ToLowerInvariant()
            });

            return authors.Insert(new Author
            {
                Metadata = metadata,
                AuthorMetadataId = metadata.Id,
                CleanName = Core.Parser.Parser.CleanAuthorName(authorName),
                Path = Path.Combine(AuthorRootFolder, authorSlug),
                QualityProfileId = 1,
                MetadataProfileId = 1,
                Added = DateTime.UtcNow,
                Monitored = monitored
            });
        }

        protected Book SeedCatalogueBook(bool authorMonitored, Quality fileQuality = null)
        {
            // Wanted filters these fixtures by author monitoring on an otherwise monitored book.
            var author = SeedAuthor(CatalogueAuthor, authorMonitored);
            var database = OpenCatalogue();
            var books = new BookRepository(database, null, new EditionRepository(database, null));
            const string foreignBookId = "test:book:last-day";
            const string foreignEditionId = "test:edition:last-day";

            var releaseDate = new DateTime(2020, 2, 4, 0, 0, 0, DateTimeKind.Utc);
            var book = books.FindById(foreignBookId) ?? books.Insert(new Book
            {
                Author = author,
                AuthorMetadata = author.Metadata,
                AuthorMetadataId = author.AuthorMetadataId,
                ForeignBookId = foreignBookId,
                ForeignEditionId = foreignEditionId,
                Title = CatalogueTitle,
                TitleSlug = CatalogueTitle.ToUrlSlug(),
                CleanTitle = Core.Parser.Parser.CleanAuthorName(CatalogueTitle),
                Added = DateTime.UtcNow,
                ReleaseDate = releaseDate,
                Monitored = true
            });

            var editions = new EditionRepository(database, null);
            var edition = editions.FindByForeignEditionId(foreignEditionId) ?? editions.Insert(new Edition
            {
                BookId = book.Id,
                ForeignEditionId = foreignEditionId,
                Title = book.Title,
                TitleSlug = book.TitleSlug,
                ReleaseDate = releaseDate,
                IsEbook = true,
                Monitored = true
            });

            if (fileQuality != null)
            {
                var files = new MediaFileRepository(database, null);
                var path = Path.Combine(author.Path, $"{book.Title}.mobi");
                var file = files.GetFileWithPath(path);
                if (file == null)
                {
                    files.Insert(new BookFile
                    {
                        EditionId = edition.Id,
                        Path = path,
                        DateAdded = DateTime.UtcNow,
                        Modified = DateTime.UtcNow,
                        Quality = new QualityModel(fileQuality)
                    });
                }
                else if (file.Quality.Quality != fileQuality)
                {
                    file.Quality = new QualityModel(fileQuality);
                    files.Update(file);
                }
            }

            return book;
        }

        private MainDatabase OpenCatalogue()
        {
            // Use the existing migration/table mapping and only this fixture's disposable database.
            RuntimeHelpers.RunClassConstructor(typeof(DbFactory).TypeHandle);
            return new MainDatabase(new Database("IntegrationCatalogue", () =>
            {
                IDbConnection connection;

                if (PostgresOptions?.Host != null)
                {
                    connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
                    {
                        Host = PostgresOptions.Host,
                        Port = PostgresOptions.Port,
                        Database = PostgresOptions.MainDb,
                        Username = PostgresOptions.User,
                        Password = PostgresOptions.Password
                    }.ConnectionString);
                }
                else
                {
                    connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(_runner.AppData, "bibliophilarr.db"),
                        FailIfMissing = true,
                        DefaultTimeout = 5
                    }.ConnectionString);
                }

                connection.Open();
                return connection;
            }));
        }
    }
}
