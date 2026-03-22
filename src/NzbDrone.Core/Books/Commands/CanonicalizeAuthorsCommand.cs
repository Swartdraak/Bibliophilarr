using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    public class CanonicalizeAuthorsCommand : Command
    {
        public bool DryRun { get; set; } = true;
        public double MinConfidence { get; set; } = 0.95;
        public int MaxMerges { get; set; } = 200;

        public override bool SendUpdatesToClient => false;

        public override string CompletionMessage => "Author canonicalization completed";
    }
}
