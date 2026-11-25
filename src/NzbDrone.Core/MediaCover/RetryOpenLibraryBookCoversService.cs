using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaCover
{
    public class RetryOpenLibraryBookCoversService : IExecute<RetryOpenLibraryBookCoversCommand>
    {
        private const int PageSize = 64;
        private const int RequestLimit = 4;
        private static readonly TimeSpan TickLimit = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

        private readonly IConfigService _configService;
        private readonly IBookRepository _books;
        private readonly IEditionService _editions;
        private readonly IOpenLibraryBookCoverCache _covers;
        private readonly IAuthorRepository _authors;
        private readonly IOpenLibraryAuthorPortraitCache _portraits;
        private readonly ICached<bool> _unavailable;
        private readonly ICatalogueIdentityRepository _catalogueIdentity;
        private readonly TimeProvider _timeProvider;
        private int _afterId;
        private int _afterAuthorId;
        private int _executing;
        private bool _startWithAuthor;

        public RetryOpenLibraryBookCoversService(IConfigService configService,
                                                 IBookRepository books,
                                                 IEditionService editions,
                                                 IOpenLibraryBookCoverCache covers,
                                                 ICacheManager cacheManager,
                                                 ICatalogueIdentityRepository catalogueIdentity,
                                                 IAuthorRepository authors,
                                                 IOpenLibraryAuthorPortraitCache portraits)
            : this(configService, books, editions, covers, cacheManager, catalogueIdentity, authors, portraits, TimeProvider.System)
        {
        }

        internal RetryOpenLibraryBookCoversService(IConfigService configService,
                                                 IBookRepository books,
                                                 IEditionService editions,
                                                 IOpenLibraryBookCoverCache covers,
                                                 ICacheManager cacheManager,
                                                 ICatalogueIdentityRepository catalogueIdentity,
                                                 IAuthorRepository authors,
                                                 IOpenLibraryAuthorPortraitCache portraits,
                                                 TimeProvider timeProvider)
        {
            _configService = configService;
            _books = books;
            _editions = editions;
            _covers = covers;
            _unavailable = cacheManager.GetCache<bool>(GetType());
            _catalogueIdentity = catalogueIdentity;
            _authors = authors;
            _portraits = portraits;
            _timeProvider = timeProvider;
        }

        public void Execute(RetryOpenLibraryBookCoversCommand message)
        {
            // A cancelled command releases its queue slot before a synchronous artwork request ends.
            if (Interlocked.Exchange(ref _executing, 1) != 0)
            {
                return;
            }

            try
            {
                RetryMissingCovers();
            }
            finally
            {
                Volatile.Write(ref _executing, 0);
            }
        }

        private void RetryMissingCovers()
        {
            if (!_configService.EnableOpenLibraryProvider || _covers.IsCoolingDown)
            {
                return;
            }

            var start = _timeProvider.GetTimestamp();
            var tryAuthor = _startWithAuthor;
            _startWithAuthor = !_startWithAuthor;
            var triedAuthor = false;
            _unavailable.ClearExpired();

            var requests = 0;
            bool ReserveRedirect()
            {
                if (requests >= RequestLimit || _timeProvider.GetElapsedTime(start) >= TickLimit || !_configService.EnableOpenLibraryProvider)
                {
                    return false;
                }

                requests++;
                return true;
            }

            using var books = GetBookCoverAttempts(ReserveRedirect).GetEnumerator();
            using var authors = GetAuthorPortraitAttempts(ReserveRedirect).GetEnumerator();
            while (requests < RequestLimit && _configService.EnableOpenLibraryProvider)
            {
                if (_timeProvider.GetElapsedTime(start) >= TickLimit)
                {
                    return;
                }

                Func<TimeSpan, OpenLibraryCoverRecovery> attempt;
                if (tryAuthor && !triedAuthor)
                {
                    triedAuthor = true;
                    if (!authors.MoveNext())
                    {
                        continue;
                    }

                    attempt = authors.Current;
                }
                else if (books.MoveNext())
                {
                    tryAuthor = true;
                    attempt = books.Current;
                }
                else if (!triedAuthor)
                {
                    tryAuthor = true;
                    continue;
                }
                else
                {
                    return;
                }

                var remaining = TickLimit - _timeProvider.GetElapsedTime(start);
                if (remaining <= TimeSpan.Zero || !_configService.EnableOpenLibraryProvider)
                {
                    return;
                }

                requests++;
                var result = attempt(remaining < RequestTimeout ? remaining : RequestTimeout);
                if (result is OpenLibraryCoverRecovery.Deferred or OpenLibraryCoverRecovery.DeferredAfterRequest)
                {
                    return;
                }
            }
        }

        private IEnumerable<Func<TimeSpan, OpenLibraryCoverRecovery>> GetBookCoverAttempts(Func<bool> reserveRedirect)
        {
            var page = _books.GetArtworkRetryPage(_afterId, PageSize);
            if (page.Count == 0)
            {
                _afterId = 0;
                yield break;
            }

            var editions = _editions.GetEditionsByBook(page.Select(book => book.Id).ToArray()).ToLookup(edition => edition.BookId);

            foreach (var book in page)
            {
                if (OpenLibraryMissingBookCover.SelectArtwork(book, editions[book.Id].ToList()) is { } artwork &&
                    !_unavailable.Find(artwork.FileSuffix) && _covers.IsMissing(book.Id, artwork.FileSuffix) &&
                    OpenLibraryMissingBookCover.HasVerifiedOwner(artwork.WorkSourceId, book.Id, _catalogueIdentity))
                {
                    bool IsStillSelected()
                    {
                        var current = _books.Find(book.Id);
                        return current != null && _configService.EnableOpenLibraryProvider &&
                            artwork == OpenLibraryMissingBookCover.SelectArtwork(current, _editions.GetEditionsByBook(current.Id)) &&
                            OpenLibraryMissingBookCover.HasVerifiedOwner(artwork.WorkSourceId, current.Id, _catalogueIdentity);
                    }

                    yield return timeout =>
                    {
                        var result = _covers.TryRecover(book.Id, artwork.CoverUrl, artwork.FileSuffix, IsStillSelected, timeout, reserveRedirect);
                        RecordResult(artwork.FileSuffix, result, book.Id, ref _afterId);
                        if (result is not OpenLibraryCoverRecovery.Deferred and not OpenLibraryCoverRecovery.DeferredAfterRequest &&
                            page.Count < PageSize && book.Id == page[^1].Id)
                        {
                            // Reaching the end with the last available request still completes the page.
                            _afterId = 0;
                        }

                        return result;
                    };
                }

                _afterId = book.Id;
            }

            if (page.Count < PageSize)
            {
                _afterId = 0;
            }
        }

        private IEnumerable<Func<TimeSpan, OpenLibraryCoverRecovery>> GetAuthorPortraitAttempts(Func<bool> reserveRedirect)
        {
            var page = _authors.GetArtworkRetryPage(_afterAuthorId, PageSize);
            foreach (var author in page)
            {
                if (OpenLibraryMissingAuthorPortrait.SelectArtwork(author) is { } artwork &&
                    !_unavailable.Find(artwork.FileSuffix) && _portraits.IsMissing(author.Id, artwork.FileSuffix) &&
                    OpenLibraryMissingAuthorPortrait.HasVerifiedOwner(_catalogueIdentity, artwork.MetadataId, artwork.SourceId))
                {
                    bool IsStillSelected()
                    {
                        var current = _authors.Find(author.Id);
                        return _configService.EnableOpenLibraryProvider &&
                            artwork == OpenLibraryMissingAuthorPortrait.SelectArtwork(current) &&
                            OpenLibraryMissingAuthorPortrait.HasVerifiedOwner(_catalogueIdentity, artwork.MetadataId, artwork.SourceId);
                    }

                    yield return timeout =>
                    {
                        var result = _portraits.TryRecover(author.Id, artwork.Url, artwork.FileSuffix, IsStillSelected, timeout, reserveRedirect);
                        RecordResult(artwork.FileSuffix, result, author.Id, ref _afterAuthorId);
                        return result;
                    };
                }

                _afterAuthorId = author.Id;
            }

            if (page.Count < PageSize)
            {
                _afterAuthorId = 0;
            }
        }

        private void RecordResult(string suffix, OpenLibraryCoverRecovery result, int id, ref int cursor)
        {
            if (result != OpenLibraryCoverRecovery.Deferred)
            {
                cursor = id;
            }

            if (result == OpenLibraryCoverRecovery.Unavailable)
            {
                _unavailable.Set(suffix, true, TimeSpan.FromHours(24));
            }
        }
    }
}
