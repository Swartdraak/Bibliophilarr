using System;
using System.Collections.Generic;
using System.Linq;
using Equ;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Books
{
    public class Edition : Entity<Edition>
    {
        internal const string PrintFormat = "Print";
        internal const string UnclassifiedFormat = "Unclassified";

        public Edition()
        {
            Overview = string.Empty;
            Images = new List<MediaCover.MediaCover>();
            Links = new List<Links>();
            Ratings = new Ratings();
        }

        // These correspond to columns in the Books table
        // These are metadata entries
        public int BookId { get; set; }
        public string ForeignEditionId { get; set; }
        public string TitleSlug { get; set; }
        public string Isbn13 { get; set; }
        public string Asin { get; set; }
        public string Title { get; set; }
        public string Language { get; set; }
        public string Overview { get; set; }
        public string Format { get; set; }
        public bool IsEbook { get; set; }
        public string Disambiguation { get; set; }
        public string Publisher { get; set; }
        public int PageCount { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public List<Links> Links { get; set; }
        public Ratings Ratings { get; set; }

        // These are Bibliophilarr generated/config
        public bool Monitored { get; set; }
        public bool ManualAdd { get; set; }

        // These are dynamically queried from other tables
        [MemberwiseEqualityIgnore]
        public LazyLoaded<Book> Book { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<List<BookFile>> BookFiles { get; set; }

        public bool MatchesFormat(FormatType format)
        {
            return format == FormatType.Ebook
                ? IsEbook
                : !IsEbook && !HasUntrackedFormat();
        }

        internal bool HasUntrackedFormat()
        {
            return Format is PrintFormat or UnclassifiedFormat;
        }

        public override string ToString()
        {
            return string.Format("[{0}][{1}]", ForeignEditionId, Title.NullSafe());
        }

        public override void UseMetadataFrom(Edition other)
        {
            var sameEdition = ForeignEditionId.IsNotNullOrWhiteSpace() &&
                              string.Equals(ForeignEditionId, other.ForeignEditionId, StringComparison.Ordinal);

            var preserveTitle = ShouldPreserveTitle(other);
            ForeignEditionId = other.ForeignEditionId;
            TitleSlug = other.TitleSlug;
            Isbn13 = sameEdition && other.Isbn13.IsNullOrWhiteSpace() ? Isbn13 : other.Isbn13;
            Asin = sameEdition && other.Asin.IsNullOrWhiteSpace() ? Asin : other.Asin;
            Title = preserveTitle ? Title : other.Title;
            Language = sameEdition && other.Language.IsNullOrWhiteSpace() ? Language : other.Language;
            Overview = other.Overview.IsNullOrWhiteSpace() ? Overview : other.Overview;
            Format = other.Format;
            IsEbook = other.IsEbook;
            Disambiguation = other.Disambiguation;
            Publisher = sameEdition && other.Publisher.IsNullOrWhiteSpace() ? Publisher : other.Publisher;
            PageCount = sameEdition && other.PageCount == 0 ? PageCount : other.PageCount;
            ReleaseDate = sameEdition ? other.ReleaseDate ?? ReleaseDate : other.ReleaseDate;
            Images = other.Images.Any() ? other.Images : Images;
            Links = other.Links;
            Ratings = other.Ratings;
        }

        internal bool ShouldPreserveTitle(Edition incoming)
        {
            return ForeignEditionId.IsNotNullOrWhiteSpace() &&
                   string.Equals(ForeignEditionId, incoming.ForeignEditionId, StringComparison.Ordinal) &&
                   Title.IsNotNullOrWhiteSpace() && incoming.Title.IsNullOrWhiteSpace();
        }

        public override void UseDbFieldsFrom(Edition other)
        {
            Id = other.Id;
            BookId = other.BookId;
            Book = other.Book;
            Monitored = other.Monitored;
            ManualAdd = other.ManualAdd;
        }

        public override void ApplyChanges(Edition other)
        {
            ForeignEditionId = other.ForeignEditionId;
            Monitored = other.Monitored;
        }
    }
}
