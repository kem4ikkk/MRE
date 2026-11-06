using System;
using System.Collections.Generic;
using System.Reflection;
using SDG.Unturned;

namespace MreDecay.Adapter
{
    // Diagnostics only. The caller must run on the Unity server thread.
    // This type has no plugin entry point, scheduler, or world mutation operations.
    public static class ReadOnlyWorld
    {
        // An observation is intentionally not an Engine.Snapshot: completeness, persistent
        // IDs, device status, assets, zone boundaries and raid state are not established.
        // Never return partial data after an exception or invalid object.
        public static bool TryObserve(bool explicitlyEnabled, out ObjectObservation[] observations)
        {
            observations = null;
            if (!explicitlyEnabled || !Provider.isServer || !Level.isLoaded ||
                BarricadeManager.regions == null || BarricadeManager.vehicleRegions == null || StructureManager.regions == null) return false;
            try
            {
                if (BarricadeManager.regions.GetLength(0) != Regions.WORLD_SIZE || BarricadeManager.regions.GetLength(1) != Regions.WORLD_SIZE ||
                    StructureManager.regions.GetLength(0) != Regions.WORLD_SIZE || StructureManager.regions.GetLength(1) != Regions.WORLD_SIZE) return false;
                var found = new List<ObjectObservation>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var region in BarricadeManager.regions)
                {
                    bool pending;
                    if (region == null || region.drops == null || !TryGetPendingDestroy(typeof(BarricadeRegion), region, out pending) || pending) return false;
                    foreach (var drop in region.drops)
                    {
                        if (!TryObserveBarricade(drop, null, ids, found)) return false;
                    }
                }
                var vehicleRegions = BarricadeManager.vehicleRegions;
                if (vehicleRegions == null) return false;
                int vehicleRegionCount = vehicleRegions.Count;
                var vehicleRegionSnapshot = new VehicleBarricadeRegion[vehicleRegionCount];
                var seenVehicleRegions = new HashSet<VehicleBarricadeRegion>();
                var seenVehicleSubregions = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < vehicleRegionCount; i++)
                {
                    var region = vehicleRegions[i];
                    vehicleRegionSnapshot[i] = region;
                    bool pending;
                    if (region == null || region.drops == null || region.vehicle == null || region.vehicle.IsPendingDestroy ||
                        region.vehicle.instanceID == 0 || region.parent == null ||
                        !TryGetPendingDestroy(typeof(BarricadeRegion), region, out pending) || pending ||
                        !seenVehicleRegions.Add(region) || !seenVehicleSubregions.Add(region.vehicle.instanceID + ":" + region.subvehicleIndex) ||
                        !ReferenceEquals(BarricadeManager.FindVehicleRegionByTransform(region.parent), region)) return false;
                    foreach (var drop in region.drops)
                        if (!TryObserveBarricade(drop, region, ids, found)) return false;
                }
                if (!ReferenceEquals(vehicleRegions, BarricadeManager.vehicleRegions) || vehicleRegions.Count != vehicleRegionCount) return false;
                for (int i = 0; i < vehicleRegionCount; i++) if (!ReferenceEquals(vehicleRegions[i], vehicleRegionSnapshot[i])) return false;
                foreach (var region in StructureManager.regions)
                {
                    bool pending;
                    if (region == null || region.drops == null || !TryGetPendingDestroy(typeof(StructureRegion), region, out pending) || pending) return false;
                    foreach (var drop in region.drops)
                    {
                        if (drop == null || drop.asset == null || drop.model == null || drop.instanceID == 0 || drop.asset.GUID == Guid.Empty ||
                            !ReferenceEquals(StructureManager.FindStructureByRootTransform(drop.model), drop) || !ids.Add("s" + drop.instanceID)) return false;
                        var data = drop.GetServersideData();
                        if (data == null || data.structure == null) return false;
                        var p = drop.model.position;
                        if (!Valid(p.x, p.y, p.z, data.owner, data.structure.health, drop.asset.health)) return false;
                        found.Add(new ObjectObservation { Kind = "structure", InstanceId = drop.instanceID, AssetId = drop.asset.id,
                            AssetGuid = drop.asset.GUID, Health = data.structure.health, MaxHealth = drop.asset.health, Owner = data.owner, Group = data.group, X = p.x, Y = p.y, Z = p.z });
                    }
                }
                var result = found.ToArray();
                if (!ObservationAudit.Valid(result)) return false;
                observations = result;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static bool TryObserveBarricade(BarricadeDrop drop, VehicleBarricadeRegion vehicleRegion,
            HashSet<string> ids, List<ObjectObservation> found)
        {
            if (drop == null || drop.asset == null || drop.model == null || drop.instanceID == 0 || drop.asset.GUID == Guid.Empty ||
                !ReferenceEquals(BarricadeManager.FindBarricadeByRootTransform(drop.model), drop) || !ids.Add("b" + drop.instanceID)) return false;
            if (vehicleRegion == null)
            {
                if (drop.IsChildOfVehicle) return false;
            }
            else if (!drop.IsChildOfVehicle || !ReferenceEquals(drop.model.parent, vehicleRegion.parent)) return false;
            var data = drop.GetServersideData();
            if (data == null || data.barricade == null) return false;
            var p = drop.model.position;
            if (!Valid(p.x, p.y, p.z, data.owner, data.barricade.health, drop.asset.health)) return false;
            bool? powered = null;
            if (drop.asset is ItemGeneratorAsset)
            {
                var generator = drop.model.GetComponent<InteractableGenerator>();
                if (generator == null) return false;
                powered = generator.isPowered && generator.fuel > 0;
            }
            found.Add(new ObjectObservation { Kind = "barricade", BuildType = drop.asset.build.ToString(), InstanceId = drop.instanceID, AssetId = drop.asset.id,
                IsVehicleAttached = vehicleRegion != null, VehicleInstanceId = vehicleRegion == null ? 0 : vehicleRegion.vehicle.instanceID,
                VehicleSubvehicleIndex = vehicleRegion == null ? 0 : vehicleRegion.subvehicleIndex,
                AssetGuid = drop.asset.GUID, IsStorage = drop.asset is ItemStorageAsset, IsGenerator = drop.asset is ItemGeneratorAsset, GeneratorPowered = powered,
                Health = data.barricade.health, MaxHealth = drop.asset.health, Owner = data.owner, Group = data.group, X = p.x, Y = p.y, Z = p.z });
            return true;
        }

        private static bool TryGetPendingDestroy(Type declaringType, object region, out bool pending)
        {
            pending = true;
            try
            {
                var field = declaringType.GetField("isPendingDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(bool)) return false;
                pending = (bool)field.GetValue(region);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static bool Valid(float x, float y, float z, ulong owner, ushort health, ushort max)
        {
            return owner != 0 && max != 0 && health <= max && !float.IsNaN(x) && !float.IsInfinity(x) &&
                !float.IsNaN(y) && !float.IsInfinity(y) && !float.IsNaN(z) && !float.IsInfinity(z);
        }
    }
}
