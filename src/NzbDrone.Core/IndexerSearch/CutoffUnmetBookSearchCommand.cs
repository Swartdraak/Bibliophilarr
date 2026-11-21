using System.Collections.Generic;
using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class CutoffUnmetBookSearchCommand : Command
    {
        public int? AuthorId { get; set; }
        public FormatType? FormatType { get; set; }
        public List<int> BookIds { get; set; }

        public override bool SendUpdatesToClient => true;

        public CutoffUnmetBookSearchCommand()
        {
        }

        public CutoffUnmetBookSearchCommand(int authorId)
        {
            AuthorId = authorId;
        }
    }
}
