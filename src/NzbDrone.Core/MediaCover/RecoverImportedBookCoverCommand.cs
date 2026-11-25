using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaCover
{
    public class RecoverImportedBookCoverCommand : Command
    {
        public int BookId { get; set; }
        public string CanonicalId { get; set; }
        public int FileId { get; set; }
        public int EditionId { get; set; }
        public long Size { get; set; }
        public long ModifiedTicks { get; set; }

        public override bool IsTypeExclusive => true;
        public override bool UpdateScheduledTask => false;
    }
}
