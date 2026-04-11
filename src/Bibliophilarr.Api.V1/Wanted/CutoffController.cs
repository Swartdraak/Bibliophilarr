using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Http;
using Bibliophilarr.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.SignalR;

namespace Bibliophilarr.Api.V1.Wanted
{
    [V1ApiController("wanted/cutoff")]
    public class CutoffController : BookControllerWithSignalR
    {
        private readonly IBookCutoffService _bookCutoffService;
        private readonly IConfigService _configService;

        public CutoffController(IBookCutoffService bookCutoffService,
                            IBookService bookService,
                            ISeriesBookLinkService seriesBookLinkService,
                            IAuthorStatisticsService authorStatisticsService,
                            IMapCoversToLocal coverMapper,
                            IUpgradableSpecification upgradableSpecification,
                            IBroadcastSignalRMessage signalRBroadcaster,
                            IAuthorFormatProfileService formatProfileService,
                            NzbDrone.Core.Profiles.Qualities.IQualityProfileService qualityProfileService,
                            IConfigService configService)
        : base(bookService, seriesBookLinkService, authorStatisticsService, coverMapper, upgradableSpecification, signalRBroadcaster, formatProfileService, qualityProfileService)
        {
            _bookCutoffService = bookCutoffService;
            _configService = configService;
        }

        [HttpGet]
        public PagingResource<BookResource> GetCutoffUnmetBooks([FromQuery] PagingRequestResource paging, bool includeAuthor = false, bool monitored = true, FormatType? formatType = null)
        {
            var pagingResource = new PagingResource<BookResource>(paging);
            var pagingSpec = new PagingSpec<Book>
            {
                Page = pagingResource.Page,
                PageSize = pagingResource.PageSize,
                SortKey = pagingResource.SortKey,
                SortDirection = pagingResource.SortDirection
            };

            var dualFormatEnabled = _configService.EnableDualFormatTracking;

            if (monitored)
            {
                if (dualFormatEnabled)
                {
                    pagingSpec.FilterExpressions.Add(v => v.Author.Value.Monitored == true);
                }
                else
                {
                    pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Author.Value.Monitored == true);
                }
            }
            else
            {
                if (dualFormatEnabled)
                {
                    pagingSpec.FilterExpressions.Add(v => v.Author.Value.Monitored == false);
                }
                else
                {
                    pagingSpec.FilterExpressions.Add(v => v.Monitored == false || v.Author.Value.Monitored == false);
                }
            }

            return pagingSpec.ApplyToPage(spec => _bookCutoffService.BooksWhereCutoffUnmet(spec, formatType), v => MapToResource(v, includeAuthor));
        }
    }
}
