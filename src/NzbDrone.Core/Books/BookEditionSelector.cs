using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Books
{
    public static class BookEditionSelector
    {
        public static Edition GetPreferredEdition(this Book book)
        {
            return GetPreferredEdition(book?.Editions?.Value);
        }

        public static Edition GetPreferredEdition(this IEnumerable<Edition> editions)
        {
            if (editions == null)
            {
                return null;
            }

            var editionList = editions as IList<Edition> ?? editions.ToList();
            return editionList.FirstOrDefault(x => x.Monitored) ?? editionList.FirstOrDefault();
        }

        public static Edition GetPreferredEdition(this Book book, FormatType formatType)
        {
            return GetPreferredEdition(book?.Editions?.Value, formatType);
        }

        public static Edition GetPreferredEdition(this IEnumerable<Edition> editions, FormatType formatType)
        {
            if (editions == null)
            {
                return null;
            }

            var isEbook = formatType == FormatType.Ebook;
            var formatEditions = editions.Where(e => e.IsEbook == isEbook).ToList();

            if (!formatEditions.Any())
            {
                return null;
            }

            return formatEditions.FirstOrDefault(x => x.Monitored) ?? formatEditions.FirstOrDefault();
        }
    }
}
