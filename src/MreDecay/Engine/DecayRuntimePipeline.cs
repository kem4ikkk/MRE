using System;
using System.Collections.Generic;
using System.Threading;

namespace MreDecay.Engine
{
    public enum RuntimePipelineStatus
    {
        Disabled,
        BlockedByReadiness,
        ObservationFailed,
        SnapshotRejected,
        PlanRejected,
        NoChanges,
        CommitFailed,
        Applied
    }

    // The only adapter boundary through which a runtime cycle can observe or write.
    // CommitCheckedAtomically must validate every current target and durably commit all
    // world and state changes together. An adapter that cannot prove this must return false.
    public interface IDecayRuntimeAdapter
    {
        RuntimeEvidence GetVerifiedReadiness();
        bool IsOnServerThread { get; }
        bool IsBusy { get; }
        bool IsFaulted { get; }
        bool TryObserve(out Snapshot snapshot);
        bool IsAllowedTarget(Building building);
        bool CommitCheckedAtomically(Snapshot snapshot, Plan plan);
    }

    public sealed class DecayRuntimePipeline
    {
        private int executing;
        private int faulted;

        public RuntimePipelineStatus Execute(bool enabled, IDecayRuntimeAdapter adapter)
        {
            if (!enabled) return RuntimePipelineStatus.Disabled;
            if (adapter == null) return RuntimePipelineStatus.BlockedByReadiness;
            if (Interlocked.CompareExchange(ref faulted, 0, 0) != 0) return RuntimePipelineStatus.BlockedByReadiness;
            if (Interlocked.Exchange(ref executing, 1) != 0)
            {
                Interlocked.Exchange(ref faulted, 1);
                return RuntimePipelineStatus.BlockedByReadiness;
            }
            try { return ExecuteCore(adapter); }
            finally { Interlocked.Exchange(ref executing, 0); }
        }

        public bool IsFaulted { get { return Interlocked.CompareExchange(ref faulted, 0, 0) != 0; } }

        private RuntimePipelineStatus ExecuteCore(IDecayRuntimeAdapter adapter)
        {
            RuntimeEvidence evidence;
            try
            {
                if (!SafeToCall(adapter) || IsFaulted) return RuntimePipelineStatus.BlockedByReadiness;
                evidence = adapter.GetVerifiedReadiness();
            }
            catch (Exception) { return RuntimePipelineStatus.BlockedByReadiness; }
            // This check precedes observation. Configurable settings cannot set these
            // values; runtime adapters must independently establish every prerequisite.
            if (!SafeToCall(adapter) || IsFaulted || evidence == null || !evidence.Ready) return RuntimePipelineStatus.BlockedByReadiness;

            Snapshot snapshot;
            try
            {
                if (!SafeToCall(adapter) || IsFaulted || !adapter.TryObserve(out snapshot) || !SafeToCall(adapter) || IsFaulted || snapshot == null)
                    return RuntimePipelineStatus.ObservationFailed;
            }
            catch (Exception) { return RuntimePipelineStatus.ObservationFailed; }

            if (!snapshot.Confirmed || !snapshot.BuildingsComplete || !snapshot.ZonesConfirmed ||
                !snapshot.SafesComplete || !snapshot.GeneratorsComplete || snapshot.Buildings == null ||
                snapshot.Zones == null || snapshot.Safes == null || snapshot.Generators == null)
                return RuntimePipelineStatus.SnapshotRejected;

            var source = new Dictionary<string, Building>(StringComparer.Ordinal);
            try
            {
                foreach (var building in snapshot.Buildings)
                {
                    if (building == null || String.IsNullOrWhiteSpace(building.Id) ||
                        !source.TryAdd(building.Id, building) || !SafeToCall(adapter) || IsFaulted ||
                        !adapter.IsAllowedTarget(building) || !SafeToCall(adapter) || IsFaulted)
                        return RuntimePipelineStatus.SnapshotRejected;
                }
            }
            catch (Exception) { return RuntimePipelineStatus.SnapshotRejected; }

            Plan plan;
            try { plan = StepEngine.Prepare(snapshot); }
            catch (Exception) { return RuntimePipelineStatus.PlanRejected; }
            if (plan == null || plan.Results == null || plan.Results.Count != source.Count)
                return RuntimePipelineStatus.PlanRejected;

            var planned = new HashSet<string>(StringComparer.Ordinal);
            bool hasChanges = false;
            foreach (var change in plan.Results)
            {
                Building current;
                if (change == null || String.IsNullOrWhiteSpace(change.Id) || !planned.Add(change.Id) ||
                    !source.TryGetValue(change.Id, out current) || change.Health > current.MaxHealth)
                    return RuntimePipelineStatus.PlanRejected;
                if (change.Health != current.Health || change.DamageRemainder != current.DamageRemainder ||
                    change.RepairRemainder != current.RepairRemainder) hasChanges = true;
            }
            if (planned.Count != source.Count) return RuntimePipelineStatus.PlanRejected;
            if (!hasChanges) return RuntimePipelineStatus.NoChanges;

            // One call receives the entire plan. Per-object callbacks here would permit
            // partial writes if a later target fails its identity or health recheck.
            try
            {
                if (!ReadinessStillVerified(adapter) || IsFaulted || !SafeToCall(adapter) || IsFaulted)
                    return RuntimePipelineStatus.BlockedByReadiness;
                bool committed = adapter.CommitCheckedAtomically(snapshot, plan);
                if (!committed || !SafeToCall(adapter) || IsFaulted)
                {
                    Interlocked.Exchange(ref faulted, 1);
                    return RuntimePipelineStatus.CommitFailed;
                }
                return RuntimePipelineStatus.Applied;
            }
            catch (Exception) { Interlocked.Exchange(ref faulted, 1); return RuntimePipelineStatus.CommitFailed; }
        }

        private static bool SafeToCall(IDecayRuntimeAdapter adapter)
        {
            try
            {
                bool onServerThread = adapter.IsOnServerThread;
                if (!onServerThread) return false;
                bool busy = adapter.IsBusy;
                if (busy) return false;
                return !adapter.IsFaulted;
            }
            catch (Exception) { return false; }
        }

        private bool ReadinessStillVerified(IDecayRuntimeAdapter adapter)
        {
            try
            {
                if (IsFaulted || !SafeToCall(adapter) || IsFaulted) return false;
                RuntimeEvidence latest = adapter.GetVerifiedReadiness();
                return !IsFaulted && SafeToCall(adapter) && !IsFaulted && latest != null && latest.Ready;
            }
            catch (Exception) { return false; }
        }
    }
}
