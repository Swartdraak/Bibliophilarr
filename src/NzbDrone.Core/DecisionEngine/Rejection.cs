namespace NzbDrone.Core.DecisionEngine
{
    public class Rejection
    {
        public string Reason { get; set; }
        public RejectionType Type { get; set; }

        internal LibraryImportHoldReason? HoldReason { get; }

        public Rejection(string reason, RejectionType type = RejectionType.Permanent, LibraryImportHoldReason? holdReason = null)
        {
            Reason = reason;
            Type = type;
            HoldReason = holdReason;
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1}", Type, Reason);
        }
    }
}
