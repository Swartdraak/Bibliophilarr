using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    public class ReconcileCatalogueWorksCommand : Command
    {
        public int SourceBookId { get; set; }
        public int TargetBookId { get; set; }
        public bool DryRun { get; set; } = true;

        public override bool RequiresDiskAccess => true;
        public override string CompletionMessage => "Catalogue work reconciliation completed";
    }
}
