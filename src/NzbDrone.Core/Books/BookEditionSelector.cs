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
    }
}
