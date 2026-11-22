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
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, FormatType format, bool monitored);
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

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, FormatType format, bool monitored)
        {
            return BooksWhereCutoffUnmet(pagingSpec, format, (bool?)monitored);
        }

        private PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, FormatType? formatType, bool? monitored)
        {
            var qualitiesBelowCutoff = new List<QualitiesBelowCutoff>();
            var profiles = _qualityProfileService.All();
            var scopedFormat = monitored.HasValue ? formatType : null;

            //Get all items less than the cutoff
            foreach (var profile in profiles)
            {
                var cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirstAllowedQuality().Id;
                var cutoffIndex = profile.GetIndex(cutoff);
                var belowCutoff = GetQualityIds(profile.Items.Take(cutoffIndex.Index), scopedFormat).ToList();
                if (belowCutoff.Count != 0)
                {
                    var qualityRange = new QualitiesBelowCutoff(profile.Id, belowCutoff);
                    if (scopedFormat.HasValue)
                    {
                        qualityRange.QualityIdsMeetingCutoff = GetQualityIds(profile.Items.Skip(cutoffIndex.Index), scopedFormat).ToList();
                    }

                    qualitiesBelowCutoff.Add(qualityRange);
                }
            }

            if (qualitiesBelowCutoff.Empty())
            {
                pagingSpec.Records = new List<Book>();
                pagingSpec.TotalRecords = 0;

                return pagingSpec;
            }

            return scopedFormat is FormatType format
                ? _bookRepository.BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff, format, monitored, _configService.EnableDualFormatTracking)
                : _bookRepository.BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff, formatType);
        }

        private static IEnumerable<int> GetQualityIds(IEnumerable<QualityProfileQualityItem> items, FormatType? format)
        {
            return items.SelectMany(item => item.GetQualities())
                .Where(quality => !format.HasValue || (quality != Quality.Unknown && Quality.GetFormatType(quality) == format.Value))
                .Select(quality => quality.Id);
        }
    }
}
