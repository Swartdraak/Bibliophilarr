using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.OpenLibrary;

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
        private int _afterId;

        public RetryOpenLibraryBookCoversService(IConfigService configService,
                                                 IBookRepository books,
                                                 IEditionService editions,
                                                 IOpenLibraryBookCoverCache covers,
                                                 ICacheManager cacheManager)
        {
            _configService = configService;
            _books = books;
            _editions = editions;
            _covers = covers;
            _unavailable = cacheManager.GetCache<bool>(GetType());
        }

        public void Execute(RetryOpenLibraryBookCoversCommand message)
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

                if (SelectArtwork(book, editions[book.Id].ToList()) is { } artwork &&
                    !_unavailable.Find(artwork.FileSuffix) && _covers.IsMissing(book.Id, artwork.FileSuffix))
                {
                    bool IsStillSelected()
                    {
                        var current = _books.Find(book.Id);
                        return current != null && _configService.EnableOpenLibraryProvider &&
                            artwork == SelectArtwork(current, _editions.GetEditionsByBook(current.Id));
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

        private static (int EditionId, string CoverUrl, string FileSuffix)? SelectArtwork(Book book, List<Edition> editions)
        {
            // Database result order is not part of the preferred edition contract.
            // Defer to the normal artwork flow if two editions could be selected.
            var monitored = editions.Where(edition => edition.Monitored).Take(2).ToList();
            var selected = monitored.Count == 1 ? monitored[0] : editions.Count == 1 ? editions[0] : null;
            if (selected is not { Id: > 0 } || selected.BookId != book.Id)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(book.OpenLibraryWorkId) &&
                book.ForeignBookId != $"openlibrary:work:{OpenLibraryIdNormalizer.NormalizeWorkId(book.OpenLibraryWorkId)}")
            {
                return null;
            }

            var cover = OpenLibraryMissingBookCover.GetSingleCover(selected.Images);
            var suffix = OpenLibraryMissingBookCover.GetRetryFileSuffix(book.ForeignBookId, selected.ForeignEditionId, cover?.Url);
            return suffix == null ? null : (selected.Id, cover.Url, suffix);
        }
    }
}
