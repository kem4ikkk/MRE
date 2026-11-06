using System;
using System.Collections.Generic;
using MreDecay.Adapter;
using MreDecay.Engine;
using MreDecay.Zones;

namespace MreDecay.IsolatedRuntime
{
    // Observe actual objects, but never promote incomplete observations to evidence.
    // The probe calls this and receives null until real raid, zone and persistence
    // integrations exist; fixture-only code constructs a separate synthetic snapshot.
    internal static class ProbePlanning
    {
        internal static Plan Prepare(ObjectObservation[] observations)
        {
            if (!ObservationAudit.Valid(observations)) return null;
            var buildings = new List<Building>();
            foreach (var o in observations)
            {
                if (o.IsVehicleAttached) return null;
                buildings.Add(new Building {
                    Id = o.Kind + ":" + o.InstanceId, Health = o.Health, MaxHealth = o.MaxHealth,
                    Owner = o.Owner, Group = o.Group, IdentityKnown = true,
                    Position = new Position3(o.X,o.Y,o.Z), RaidBlocked = null
                    // PersistedMaxHealth and fractional state remain unknown.
                });
            }
            return StepEngine.Prepare(new Snapshot {
                Buildings = buildings, BuildingsComplete = false, Confirmed = false,
                ZonesConfirmed = false, SafesComplete = false, GeneratorsComplete = false,
                Zones = null, Safes = null, Generators = null
            });
        }
    }
}
