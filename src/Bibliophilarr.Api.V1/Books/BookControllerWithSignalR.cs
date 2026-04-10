using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Http.REST;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.SignalR;

namespace Bibliophilarr.Api.V1.Books
{
    public abstract class BookControllerWithSignalR : RestControllerWithSignalR<BookResource, Book>
    {
        protected readonly IBookService _bookService;
        protected readonly ISeriesBookLinkService _seriesBookLinkService;
        protected readonly IAuthorStatisticsService _authorStatisticsService;
        protected readonly IUpgradableSpecification _qualityUpgradableSpecification;
        protected readonly IMapCoversToLocal _coverMapper;
        protected readonly IAuthorFormatProfileService _formatProfileService;
        protected readonly IQualityProfileService _qualityProfileService;

        protected BookControllerWithSignalR(IBookService bookService,
                                        ISeriesBookLinkService seriesBookLinkService,
                                        IAuthorStatisticsService authorStatisticsService,
                                        IMapCoversToLocal coverMapper,
                                        IUpgradableSpecification qualityUpgradableSpecification,
                                        IBroadcastSignalRMessage signalRBroadcaster,
                                        IAuthorFormatProfileService formatProfileService,
                                        IQualityProfileService qualityProfileService)
            : base(signalRBroadcaster)
        {
            _bookService = bookService;
            _seriesBookLinkService = seriesBookLinkService;
            _authorStatisticsService = authorStatisticsService;
            _coverMapper = coverMapper;
            _qualityUpgradableSpecification = qualityUpgradableSpecification;
            _formatProfileService = formatProfileService;
            _qualityProfileService = qualityProfileService;
        }

        protected override BookResource GetResourceById(int id)
        {
            var book = _bookService.GetBook(id);
            var resource = MapToResource(book, true);
            return resource;
        }

        protected override BookResource GetResourceByIdForBroadcast(int id)
        {
            var book = _bookService.GetBook(id);
            var resource = MapToResource(book, false);
            return resource;
        }

        protected BookResource MapToResource(Book book, bool includeAuthor)
        {
            var resource = book.ToResource();

            if (includeAuthor)
            {
                var author = book.Author.Value;

                resource.Author = author.ToResource();
            }

            EnrichFormatStatuses(resource);
            FetchAndLinkBookStatistics(resource);
            MapCoversToLocal(resource);

            return resource;
        }

        protected List<BookResource> MapToResource(List<Book> books, bool includeAuthor)
        {
            var seriesLinks = _seriesBookLinkService.GetLinksByBook(books.Select(x => x.Id).ToList())
                .GroupBy(x => x.BookId)
                .ToDictionary(x => x.Key, y => y.ToList());

            foreach (var book in books)
            {
                if (seriesLinks.TryGetValue(book.Id, out var links))
                {
                    book.SeriesLinks = links;
                }
                else
                {
                    book.SeriesLinks = new List<SeriesBookLink>();
                }
            }

            var result = books.ToResource();

            if (includeAuthor)
            {
                var authorDict = new Dictionary<int, NzbDrone.Core.Books.Author>();
                for (var i = 0; i < books.Count; i++)
                {
                    var book = books[i];
                    var resource = result[i];
                    var author = authorDict.GetValueOrDefault(books[i].AuthorMetadataId) ?? book.Author?.Value;
                    authorDict[author.AuthorMetadataId] = author;

                    resource.Author = author.ToResource();
                }
            }

            EnrichFormatStatuses(result);

            var authorStats = _authorStatisticsService.AuthorStatistics();
            LinkAuthorStatistics(result, authorStats);
            MapCoversToLocal(result.ToArray());

            return result;
        }

        private void EnrichFormatStatuses(BookResource resource)
        {
            if (resource == null)
            {
                return;
            }

            var formatProfiles = _formatProfileService.GetByAuthorId(resource.AuthorId);
            if (formatProfiles == null || !formatProfiles.Any())
            {
                return;
            }

            resource.FormatStatuses ??= new List<BookFormatStatusResource>();
            var profileCache = new Dictionary<int, string>();

            foreach (var fp in formatProfiles)
            {
                var fs = resource.FormatStatuses.FirstOrDefault(s => s.FormatType == fp.FormatType);
                if (fs == null)
                {
                    // Author has a format profile for this type but no status entry exists yet
                    // (no files of this type and edition not classified). Add a placeholder entry.
                    fs = new BookFormatStatusResource
                    {
                        FormatType = fp.FormatType,
                        Monitored = fp.Monitored,
                        HasFile = false,
                        FileCount = 0
                    };
                    resource.FormatStatuses.Add(fs);
                }

                fs.QualityProfileId = fp.QualityProfileId;
                if (!profileCache.TryGetValue(fp.QualityProfileId, out var name))
                {
                    name = _qualityProfileService.Get(fp.QualityProfileId)?.Name;
                    profileCache[fp.QualityProfileId] = name;
                }

                fs.QualityProfileName = name;
            }
        }

        private void EnrichFormatStatuses(List<BookResource> resources)
        {
            if (resources == null || !resources.Any())
            {
                return;
            }

            var authorIds = resources.Select(r => r.AuthorId).Distinct().ToList();
            var allProfiles = authorIds.SelectMany(id => _formatProfileService.GetByAuthorId(id)).ToList();
            var profilesByAuthor = allProfiles.GroupBy(p => p.AuthorId).ToDictionary(g => g.Key, g => g.ToList());
            var qpCache = new Dictionary<int, string>();

            foreach (var resource in resources)
            {
                if (!profilesByAuthor.TryGetValue(resource.AuthorId, out var formatProfiles))
                {
                    continue;
                }

                resource.FormatStatuses ??= new List<BookFormatStatusResource>();

                foreach (var fp in formatProfiles)
                {
                    var fs = resource.FormatStatuses.FirstOrDefault(s => s.FormatType == fp.FormatType);
                    if (fs == null)
                    {
                        fs = new BookFormatStatusResource
                        {
                            FormatType = fp.FormatType,
                            Monitored = fp.Monitored,
                            HasFile = false,
                            FileCount = 0
                        };
                        resource.FormatStatuses.Add(fs);
                    }

                    fs.QualityProfileId = fp.QualityProfileId;
                    if (!qpCache.TryGetValue(fp.QualityProfileId, out var name))
                    {
                        name = _qualityProfileService.Get(fp.QualityProfileId)?.Name;
                        qpCache[fp.QualityProfileId] = name;
                    }

                    fs.QualityProfileName = name;
                }
            }
        }

        private void FetchAndLinkBookStatistics(BookResource resource)
        {
            LinkAuthorStatistics(resource, _authorStatisticsService.AuthorStatistics(resource.AuthorId));
        }

        private void LinkAuthorStatistics(List<BookResource> resources, List<AuthorStatistics> authorStatistics)
        {
            var bookStatsDict = authorStatistics.SelectMany(x => x.BookStatistics).ToDictionary(x => x.BookId);

            foreach (var book in resources)
            {
                if (bookStatsDict.TryGetValue(book.Id, out var stats))
                {
                    book.Statistics = stats.ToResource();
                }
            }
        }

        private void LinkAuthorStatistics(BookResource resource, AuthorStatistics authorStatistics)
        {
            if (authorStatistics?.BookStatistics != null)
            {
                var dictBookStats = authorStatistics.BookStatistics.ToDictionary(v => v.BookId);

                resource.Statistics = dictBookStats.GetValueOrDefault(resource.Id).ToResource();
            }
        }

        private void MapCoversToLocal(params BookResource[] books)
        {
            foreach (var bookResource in books)
            {
                _coverMapper.ConvertToLocalUrls(bookResource.Id, MediaCoverEntity.Book, bookResource.Images);
            }
        }
    }
}
