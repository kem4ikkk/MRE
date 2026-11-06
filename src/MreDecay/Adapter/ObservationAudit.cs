using System;
using System.Collections.Generic;

namespace MreDecay.Adapter
{
    // Diagnostic validation only; successful validation does not establish snapshot completeness
    // or identity persistence and must never authorize a world mutation.
    public static class ObservationAudit
    {
        public static bool Valid(ObjectObservation[] observations)
        {
            if (observations == null) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in observations)
            {
                if (item == null || (item.Kind != "structure" && item.Kind != "barricade") ||
                    item.InstanceId == 0 || item.AssetId == 0 || item.AssetGuid == Guid.Empty || item.Owner == 0 ||
                    item.MaxHealth == 0 || item.Health > item.MaxHealth ||
                    (item.IsVehicleAttached && (item.Kind != "barricade" || item.VehicleInstanceId == 0)) ||
                    (!item.IsVehicleAttached && (item.VehicleInstanceId != 0 || item.VehicleSubvehicleIndex != 0)) ||
                    (item.Kind == "structure" && (item.IsStorage || item.IsGenerator)) ||
                    (item.IsStorage && item.IsGenerator) ||
                    (item.IsGenerator != item.GeneratorPowered.HasValue) ||
                    (item.Kind == "barricade" && (String.IsNullOrWhiteSpace(item.BuildType) ||
                        (item.IsGenerator != (item.BuildType == "GENERATOR")) ||
                        ((item.BuildType == "STORAGE" || item.BuildType == "STORAGE_WALL") && !item.IsStorage))) ||
                    !Finite(item.X) || !Finite(item.Y) || !Finite(item.Z) ||
                    !ids.Add(item.Kind + ":" + item.InstanceId)) return false;
            }
            return true;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
