using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    public class BackfillOpenLibraryIdsCommand : Command
    {
        public int MaxLookups { get; set; } = 200;

        public override bool SendUpdatesToClient => false;

        public override string CompletionMessage => "Open Library ID backfill completed";
    }
}
