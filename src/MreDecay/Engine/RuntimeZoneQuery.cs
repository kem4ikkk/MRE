using System;
using MreDecay.Zones;

namespace MreDecay.Engine
{
    // Read-only status view. The real runtime must own the gate, evidence and zones,
    // run this on its game thread and revoke evidence immediately on lost readiness.
    // This class does not validate the supplied evidence or draft geometry itself.
    public sealed class RuntimeZoneQuery
    {
        private readonly RuntimeGate gate;
        private readonly RuntimeEvidence evidence;
        private readonly ZoneSet zones;
        private bool faulted;
        public RuntimeZoneQuery(RuntimeGate gate, RuntimeEvidence evidence, ZoneSet zones)
        {
            this.gate = gate ?? throw new ArgumentNullException("gate");
            this.evidence = evidence ?? throw new ArgumentNullException("evidence");
            this.zones = zones ?? throw new ArgumentNullException("zones");
        }
        // false means unknown or inactive; callers must hide the hint.
        public bool TryGetAcceleratedZone(double x, double z, out bool accelerated)
        {
            accelerated = false;
            if (faulted || !gate.IsOperational) return false;
            if (!evidence.Ready) { faulted = true; return false; }
            try { accelerated = zones.MatchingNames(x, z).Length > 0; return true; }
            catch (Exception) { faulted = true; return false; }
        }
    }
}
