using System;

namespace MreDecay.Engine
{
    // A prerequisite check is evidence supplied by the future adapter, not a configuration override.
    public sealed class RuntimeEvidence
    {
        public bool ZonesVerified, AssetsVerified, WorldSnapshotVerified, RaidEventsVerified;
        public bool DurableStateVerified, WorldStateTransactionVerified, RecoveryVerified;
        public bool PersistentIdentityVerified, VehicleCoverageVerified;
        public bool Ready => ZonesVerified && AssetsVerified && WorldSnapshotVerified && RaidEventsVerified &&
            DurableStateVerified && WorldStateTransactionVerified && RecoveryVerified &&
            PersistentIdentityVerified && VehicleCoverageVerified;
    }

    // Monotonic elapsed server time only. No wall-clock catch-up after restart.
    // This gate does not itself make world writes or persistence atomic.
    public sealed class RuntimeGate
    {
        public const long IntervalSeconds = 300;
        private long last, accumulated;
        private bool started, faulted, pending;

        public bool Start(RuntimeEvidence evidence, long monotonicSeconds, bool unresolvedTransaction)
        {
            if (started || faulted || evidence == null || !evidence.Ready || monotonicSeconds < 0 || unresolvedTransaction)
            { faulted = true; return false; }
            last = monotonicSeconds; started = true; return true;
        }

        public bool IsOperational { get { return started && !faulted && !pending; } }

        // Only one step can be pending. Excess elapsed time is discarded to avoid a burst
        // of damage after a stalled server thread; a failed step fences this instance.
        public bool Poll(RuntimeEvidence evidence, long monotonicSeconds)
        {
            if (!started || faulted || pending || evidence == null || !evidence.Ready || monotonicSeconds < last)
            { faulted = true; return false; }
            long elapsed = monotonicSeconds - last;
            last = monotonicSeconds;
            if (elapsed >= IntervalSeconds - accumulated) { accumulated = 0; pending = true; return true; }
            accumulated += elapsed;
            return false;
        }

        // Success means the adapter has durably committed world and plugin state together.
        // Unknown or failed outcomes must be reconciled offline before a new instance starts.
        public bool Complete(bool transactionDurablyCommitted)
        {
            if (!pending || faulted || !transactionDurablyCommitted) { faulted = true; return false; }
            pending = false; return true;
        }
    }
}
