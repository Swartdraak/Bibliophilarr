using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IAuthorFormatProfileRepository : IBasicRepository<AuthorFormatProfile>
    {
        List<AuthorFormatProfile> GetByAuthorId(int authorId);
        AuthorFormatProfile GetByAuthorIdAndFormat(int authorId, FormatType formatType);
        List<AuthorFormatProfile> GetPendingDefaults(int? authorId);
        bool TryResolvePendingDefault(AuthorFormatProfile expected, RootFolder root);
        void DeleteByAuthorId(int authorId);
    }

    public class AuthorFormatProfileRepository : BasicRepository<AuthorFormatProfile>, IAuthorFormatProfileRepository
    {
        public AuthorFormatProfileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<AuthorFormatProfile> GetByAuthorId(int authorId)
        {
            return Query(x => x.AuthorId == authorId);
        }

        public AuthorFormatProfile GetByAuthorIdAndFormat(int authorId, FormatType formatType)
        {
            return Query(x => x.AuthorId == authorId && x.FormatType == formatType).SingleOrDefault();
        }

        public List<AuthorFormatProfile> GetPendingDefaults(int? authorId)
        {
            const string pending = "\"AuthorFormatProfiles\".\"PendingTypedDefault\" = TRUE";
            var builder = Builder().Where(pending);
            if (authorId.HasValue)
            {
                builder.Where<AuthorFormatProfile>(profile => profile.AuthorId == authorId.Value);
            }

            return Query(builder);
        }

        public bool TryResolvePendingDefault(AuthorFormatProfile expected, RootFolder root)
        {
            const string sql = """
                UPDATE "AuthorFormatProfiles"
                SET "RootFolderPath" = @TargetPath, "QualityProfileId" = @TargetQualityProfile, "PendingTypedDefault" = FALSE
                WHERE "Id" = @Id AND "AuthorId" = @AuthorId AND "FormatType" = @FormatType
                    AND "PendingTypedDefault" = TRUE AND "RootFolderPath" = @RootFolderPath AND "QualityProfileId" = @QualityProfileId
                    AND EXISTS (SELECT 1 FROM "RootFolders" WHERE "Id" = @RootId AND "Path" = @TargetPath
                                AND "DefaultQualityProfileId" = @TargetQualityProfile AND NOT "IsCalibreLibrary")
                """;
            using var connection = _database.OpenConnection();
            return connection.Execute(sql, new
            {
                expected.Id,
                expected.AuthorId,
                expected.FormatType,
                expected.RootFolderPath,
                expected.QualityProfileId,
                RootId = root.Id,
                TargetPath = root.Path,
                TargetQualityProfile = root.DefaultQualityProfileId
            }) == 1;
        }

        public void DeleteByAuthorId(int authorId)
        {
            Delete(x => x.AuthorId == authorId);
        }
    }
}
