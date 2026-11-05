using System;
using System.Collections.Generic;
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
                BarricadeManager.regions == null || StructureManager.regions == null) return false;
            try
            {
                if (BarricadeManager.regions.GetLength(0) != Regions.WORLD_SIZE || BarricadeManager.regions.GetLength(1) != Regions.WORLD_SIZE ||
                    StructureManager.regions.GetLength(0) != Regions.WORLD_SIZE || StructureManager.regions.GetLength(1) != Regions.WORLD_SIZE) return false;
                var found = new List<ObjectObservation>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var region in BarricadeManager.regions)
                {
                    if (region == null || region.drops == null) return false;
                    foreach (var drop in region.drops)
                    {
                        if (drop == null || drop.asset == null || drop.model == null || drop.IsChildOfVehicle || drop.instanceID == 0 || drop.asset.GUID == Guid.Empty || !ids.Add("b" + drop.instanceID)) return false;
                        var data = drop.GetServersideData();
                        if (data == null || data.barricade == null) return false;
                        var p = drop.model.position;
                        if (!Valid(p.x, p.y, p.z, data.owner, data.barricade.health, drop.asset.health)) return false;
                        found.Add(new ObjectObservation { Kind = "barricade", InstanceId = drop.instanceID, AssetId = drop.asset.id,
                            AssetGuid = drop.asset.GUID, IsStorage = drop.asset is ItemStorageAsset, IsGenerator = drop.asset is ItemGeneratorAsset,
                            Health = data.barricade.health, MaxHealth = drop.asset.health, Owner = data.owner, Group = data.group, X = p.x, Y = p.y, Z = p.z });
                    }
                }
                foreach (var region in StructureManager.regions)
                {
                    if (region == null || region.drops == null) return false;
                    foreach (var drop in region.drops)
                    {
                        if (drop == null || drop.asset == null || drop.model == null || drop.instanceID == 0 || drop.asset.GUID == Guid.Empty || !ids.Add("s" + drop.instanceID)) return false;
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

        private static bool Valid(float x, float y, float z, ulong owner, ushort health, ushort max)
        {
            return owner != 0 && max != 0 && health <= max && !float.IsNaN(x) && !float.IsInfinity(x) &&
                !float.IsNaN(y) && !float.IsInfinity(y) && !float.IsNaN(z) && !float.IsInfinity(z);
        }
    }
}
