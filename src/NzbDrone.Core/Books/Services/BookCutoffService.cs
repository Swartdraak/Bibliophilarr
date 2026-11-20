using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    public interface IBookCutoffService
    {
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec);
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, FormatType? formatType);
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, FormatType? formatType, bool? monitored);
    }

    public class BookCutoffService : IBookCutoffService
    {
        private readonly IBookRepository _bookRepository;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IConfigService _configService;

        public BookCutoffService(IBookRepository bookRepository, IQualityProfileService qualityProfileService, IConfigService configService)
        {
            _bookRepository = bookRepository;
            _qualityProfileService = qualityProfileService;
            _configService = configService;
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec)
        {
            return BooksWhereCutoffUnmet(pagingSpec, null);
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, FormatType? formatType)
        {
            return BooksWhereCutoffUnmet(pagingSpec, formatType, null);
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, FormatType? formatType, bool? monitored)
        {
            var qualitiesBelowCutoff = new List<QualitiesBelowCutoff>();
            var profiles = _qualityProfileService.All();

            //Get all items less than the cutoff
            foreach (var profile in profiles)
            {
                var cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirstAllowedQuality().Id;
                var cutoffIndex = profile.GetIndex(cutoff);
                var belowCutoff = profile.Items.Take(cutoffIndex.Index)
                    .SelectMany(item => item.GetQualities())
                    .Where(quality => !formatType.HasValue || Quality.GetFormatType(quality) == formatType.Value)
                    .Select(quality => quality.Id)
                    .ToList();

                if (belowCutoff.Count != 0)
                {
                    qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, belowCutoff));
                }
            }

            if (qualitiesBelowCutoff.Empty())
            {
                pagingSpec.Records = new List<Book>();
                pagingSpec.TotalRecords = 0;

                return pagingSpec;
            }

            if (formatType is FormatType format && _configService.EnableDualFormatTracking)
            {
                return _bookRepository.BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff, format, monitored);
            }

            if (monitored is true)
            {
                pagingSpec.FilterExpressions.Add(book => book.Monitored == true && book.Author.Value.Monitored == true);
            }
            else if (monitored is false)
            {
                pagingSpec.FilterExpressions.Add(book => book.Monitored == false || book.Author.Value.Monitored == false);
            }

            return _bookRepository.BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff, formatType);
        }
    }
}
