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
        private const int AuthorPageSize = 64;
        private const int AttachedBookPageSize = 48;
        private const int BibliographyPageSize = 16;
        private const int RequestLimit = 4;
        private static readonly TimeSpan TickLimit = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
        private static readonly Lane[] LaneOrder = { Lane.AttachedBook, Lane.Bibliography, Lane.AttachedBook, Lane.Portrait };

        private readonly IConfigService _configService;
        private readonly IBookRepository _books;
        private readonly IEditionService _editions;
        private readonly IOpenLibraryBookCoverCache _covers;
        private readonly IAuthorRepository _authors;
        private readonly IOpenLibraryAuthorPortraitCache _portraits;
        private readonly ICached<bool> _unavailable;
        private readonly ICatalogueIdentityRepository _catalogueIdentity;
        private readonly TimeProvider _timeProvider;
        private readonly BookCursor _attachedBooks = new BookCursor(true, AttachedBookPageSize);
        private readonly BookCursor _bibliography = new BookCursor(false, BibliographyPageSize);
        private int _afterAuthorId;
        private int _executing;
        private int _firstLaneIndex;

        private enum Lane
        {
            AttachedBook,
            Bibliography,
            Portrait
        }

        private sealed class BookCursor(bool fileAttached, int pageSize)
        {
            public readonly bool FileAttached = fileAttached;
            public readonly int PageSize = pageSize;
            public int AfterId;
        }

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

            // Rotate which lane starts, even when the first eligible request consumes the tick.
            var laneIndex = _firstLaneIndex;
            _firstLaneIndex = (_firstLaneIndex + 1) % LaneOrder.Length;
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

            var attemptedBooks = new HashSet<int>();
            using var attached = GetBookCoverAttempts(_attachedBooks, attemptedBooks, ReserveRedirect).GetEnumerator();
            using var bibliography = GetBookCoverAttempts(_bibliography, attemptedBooks, ReserveRedirect).GetEnumerator();
            using var portraits = GetAuthorPortraitAttempts(ReserveRedirect).GetEnumerator();
            var lanes = new[] { attached, bibliography, portraits };
            var retired = new HashSet<Lane>();
            while (retired.Count < lanes.Length && requests < RequestLimit && _configService.EnableOpenLibraryProvider)
            {
                if (_timeProvider.GetElapsedTime(start) >= TickLimit)
                {
                    return;
                }

                var lane = LaneOrder[laneIndex];
                laneIndex = (laneIndex + 1) % LaneOrder.Length;
                if (retired.Contains(lane))
                {
                    continue;
                }

                var attempts = lanes[(int)lane];
                if (!attempts.MoveNext())
                {
                    retired.Add(lane);
                    continue;
                }

                if (lane == Lane.Portrait)
                {
                    retired.Add(lane);
                }

                var remaining = TickLimit - _timeProvider.GetElapsedTime(start);
                if (remaining <= TimeSpan.Zero || !_configService.EnableOpenLibraryProvider)
                {
                    return;
                }

                requests++;
                var result = attempts.Current(remaining < RequestTimeout ? remaining : RequestTimeout);
                if (result is OpenLibraryCoverRecovery.Deferred or OpenLibraryCoverRecovery.DeferredAfterRequest)
                {
                    return;
                }
            }
        }

        private IEnumerable<Func<TimeSpan, OpenLibraryCoverRecovery>> GetBookCoverAttempts(
            BookCursor cursor, HashSet<int> attemptedBooks, Func<bool> reserveRedirect)
        {
            var page = _books.GetArtworkRetryPage(cursor.AfterId, cursor.PageSize, cursor.FileAttached);
            if (page.Count == 0)
            {
                cursor.AfterId = 0;
                yield break;
            }

            var editions = _editions.GetEditionsByBook(page.Select(book => book.Id).ToArray()).ToLookup(edition => edition.BookId);

            foreach (var book in page)
            {
                if (!attemptedBooks.Contains(book.Id) &&
                    OpenLibraryMissingBookCover.SelectArtwork(book, editions[book.Id].ToList()) is { } artwork &&
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
                        // A file can be linked or unlinked between the two partition reads.
                        attemptedBooks.Add(book.Id);
                        var result = _covers.TryRecover(book.Id, artwork.CoverUrl, artwork.FileSuffix, IsStillSelected, timeout, reserveRedirect);
                        RecordResult(artwork.FileSuffix, result, book.Id, ref cursor.AfterId);
                        if (result is not OpenLibraryCoverRecovery.Deferred and not OpenLibraryCoverRecovery.DeferredAfterRequest &&
                            page.Count < cursor.PageSize && book.Id == page[^1].Id)
                        {
                            // Reaching the end with the last available request still completes the page.
                            cursor.AfterId = 0;
                        }

                        return result;
                    };
                }

                cursor.AfterId = book.Id;
            }

            if (page.Count < cursor.PageSize)
            {
                cursor.AfterId = 0;
            }
        }

        private IEnumerable<Func<TimeSpan, OpenLibraryCoverRecovery>> GetAuthorPortraitAttempts(Func<bool> reserveRedirect)
        {
            var page = _authors.GetArtworkRetryPage(_afterAuthorId, AuthorPageSize);
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

            if (page.Count < AuthorPageSize)
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
