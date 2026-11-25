using System;
using System.Diagnostics;
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
        private readonly ICached<bool> _unavailable;
        private readonly ICatalogueIdentityRepository _catalogueIdentity;
        private int _afterId;
        private int _executing;

        public RetryOpenLibraryBookCoversService(IConfigService configService,
                                                 IBookRepository books,
                                                 IEditionService editions,
                                                 IOpenLibraryBookCoverCache covers,
                                                 ICacheManager cacheManager,
                                                 ICatalogueIdentityRepository catalogueIdentity)
        {
            _configService = configService;
            _books = books;
            _editions = editions;
            _covers = covers;
            _unavailable = cacheManager.GetCache<bool>(GetType());
            _catalogueIdentity = catalogueIdentity;
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

            var tick = Stopwatch.StartNew();
            var page = _books.GetArtworkRetryPage(_afterId, PageSize);
            if (page.Count == 0)
            {
                _afterId = 0;
                return;
            }

            var editions = _editions.GetEditionsByBook(page.Select(book => book.Id).ToArray()).ToLookup(edition => edition.BookId);
            var requests = 0;
            _unavailable.ClearExpired();

            foreach (var book in page)
            {
                var remaining = TickLimit - tick.Elapsed;
                if (remaining <= TimeSpan.Zero || requests == RequestLimit || !_configService.EnableOpenLibraryProvider)
                {
                    return;
                }

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

                    requests++;
                    var result = _covers.TryRecover(
                        book.Id,
                        artwork.CoverUrl,
                        artwork.FileSuffix,
                        IsStillSelected,
                        remaining < RequestTimeout ? remaining : RequestTimeout);

                    if (result == OpenLibraryCoverRecovery.Deferred)
                    {
                        return;
                    }

                    if (result == OpenLibraryCoverRecovery.DeferredAfterRequest)
                    {
                        // Resume at the next cover and reconsider this one when the scan wraps.
                        _afterId = book.Id;
                        return;
                    }

                    if (result == OpenLibraryCoverRecovery.Unavailable)
                    {
                        _unavailable.Set(artwork.FileSuffix, true, TimeSpan.FromHours(24));
                    }
                }

                _afterId = book.Id;
            }

            if (page.Count < PageSize)
            {
                _afterId = 0;
            }
        }
    }
}
