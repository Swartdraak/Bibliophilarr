using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    public class ReconcileCatalogueAuthorsCommand : Command
    {
        public int SourceAuthorId { get; set; }
        public int TargetAuthorId { get; set; }
        public bool DryRun { get; set; } = true;

        public override bool RequiresDiskAccess => true;
        public override bool IsExclusive => true;
        public override string CompletionMessage => "Catalogue author reconciliation completed";
    }
}
