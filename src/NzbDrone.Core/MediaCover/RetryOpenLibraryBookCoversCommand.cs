using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaCover
{
    public class RetryOpenLibraryBookCoversCommand : Command
    {
        public override bool IsTypeExclusive => true;
    }
}
