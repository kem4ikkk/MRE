// Isolated-only test mutation API. No production scheduler or configuration switch.
using System;
using SDG.Unturned;
using UnityEngine;

namespace MreDecay.IsolatedRuntime
{
    internal static class ProbeHealthMutation
    {
        // The caller must already have validated isolated-server guards, current identity,
        // raid and durable transaction. This experiment cannot satisfy those requirements.
        internal static bool ApplyStructure(StructureDrop drop, ushort expected, ushort target)
        {
            if (!Provider.isServer || !Level.isLoaded || drop == null || drop.asset == null || drop.model == null || drop.GetServersideData() == null ||
                drop.GetServersideData().structure == null || expected == 0 || target == 0 ||
                drop.GetServersideData().structure.health != expected || target > drop.asset.health) return false;
            if (!ReferenceEquals(StructureManager.FindStructureByRootTransform(drop.model), drop)) return false;
            if (target < expected) StructureManager.damage(drop.model, Vector3.zero, expected - target, 1, false);
            else if (target > expected) StructureManager.repair(drop.model, target - expected, 1);
            var current = drop.GetServersideData();
            return ReferenceEquals(StructureManager.FindStructureByRootTransform(drop.model), drop) && current != null && current.structure != null && current.structure.health == target;
        }
        internal static bool ApplyBarricade(BarricadeDrop drop, ushort expected, ushort target)
        {
            if (!Provider.isServer || !Level.isLoaded || drop == null || drop.asset == null || drop.model == null || drop.GetServersideData() == null ||
                drop.GetServersideData().barricade == null || expected == 0 || target == 0 ||
                drop.GetServersideData().barricade.health != expected || target > drop.asset.health) return false;
            if (drop.IsChildOfVehicle || !ReferenceEquals(BarricadeManager.FindBarricadeByRootTransform(drop.model), drop)) return false;
            if (target < expected) BarricadeManager.damage(drop.model, expected - target, 1, false);
            else if (target > expected) BarricadeManager.repair(drop.model, target - expected, 1);
            var current = drop.GetServersideData();
            return ReferenceEquals(BarricadeManager.FindBarricadeByRootTransform(drop.model), drop) && current != null && current.barricade != null && current.barricade.health == target;
        }
    }
}
