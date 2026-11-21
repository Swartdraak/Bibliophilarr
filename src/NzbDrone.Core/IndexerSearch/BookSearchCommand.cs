using System.Collections.Generic;
using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class BookSearchCommand : Command
    {
        public List<int> BookIds { get; set; }

        // When set, limit the search and grabs to this monitored format if it is still missing.
        public FormatType? MissingFormat { get; set; }

        public override bool SendUpdatesToClient => true;

        public BookSearchCommand()
        {
        }

        public BookSearchCommand(List<int> bookIds)
        {
            BookIds = bookIds;
        }
    }
}
