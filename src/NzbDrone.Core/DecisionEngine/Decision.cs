namespace NzbDrone.Core.DecisionEngine
{
    public class Decision
    {
        public bool Accepted { get; private set; }
        public string Reason { get; private set; }

        internal LibraryImportHoldReason? HoldReason { get; private set; }

        private static readonly Decision AcceptDecision = new Decision { Accepted = true };
        private Decision()
        {
        }

        public static Decision Accept()
        {
            return AcceptDecision;
        }

        public static Decision Reject(string reason, params object[] args)
        {
            return Reject(string.Format(reason, args));
        }

        public static Decision Reject(string reason)
        {
            return RejectWithHoldReason(reason, null);
        }

        internal static Decision RejectWithHoldReason(string reason, LibraryImportHoldReason? holdReason)
        {
            return new Decision
            {
                Accepted = false,
                Reason = reason,
                HoldReason = holdReason
            };
        }
    }
}
