using System;
using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    internal interface IVerifyAuthorWork
    {
        string GetVerifiedAuthorWorkTitle(string authorId, string workId, string isbn);
    }

    internal interface IResolveAuthorWorkByIsbn
    {
        (string WorkId, string Title)? GetVerifiedAuthorWorkByIsbn(string authorId, string isbn);
    }

    internal interface IProvideAuthorProfileMetadata
    {
        AuthorMetadata GetAuthorProfileMetadata(string authorId);
    }

    public interface IProvideAuthorInfo
    {
        Author GetAuthorInfo(string bibliophilarrId, bool useCache = true);
        HashSet<string> GetChangedAuthors(DateTime startTime);
    }
}
