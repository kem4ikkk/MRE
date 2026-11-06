using System;
using System.Collections.Generic;
using MreDecay.Adapter;

namespace MreDecay.IsolatedRuntime
{
    // Converts every observed object from the diagnostic snapshot, not a selected
    // subset. Vehicle objects are intentionally rejected before any mutation plan.
    internal sealed class GroundDeviceSet
    {
        internal readonly List<ProtectionObject> Safes=new List<ProtectionObject>();
        internal readonly List<ProtectionObject> Generators=new List<ProtectionObject>();
        internal readonly List<ObjectObservation> Buildings=new List<ObjectObservation>();
    }
    internal static class GroundDeviceCollector
    {
        internal static GroundDeviceSet Collect(ObjectObservation[] observations, ISet<Guid> safeGuids)
        {
            if (!ObservationAudit.Valid(observations) || safeGuids==null || safeGuids.Count==0 || safeGuids.Contains(Guid.Empty)) return null;
            var result=new GroundDeviceSet();
            foreach (var o in observations) {
                // Vehicle attachment is observed for coverage diagnostics, but remains
                // outside the supported mutation policy until a vehicle-safe backend exists.
                if (o.IsVehicleAttached) return null;
                bool safe=safeGuids.Contains(o.AssetGuid);
                if (safe && (!o.IsStorage || o.Kind!="barricade" || o.IsVehicleAttached)) return null;
                var position=new Position3(o.X,o.Y,o.Z);
                if(safe) result.Safes.Add(new ProtectionObject { Owner=o.Owner,Group=o.Group,IdentityKnown=true,Position=position });
                if(o.IsGenerator) {
                    if(o.IsVehicleAttached || !o.GeneratorPowered.HasValue) return null;
                    result.Generators.Add(new ProtectionObject { Owner=o.Owner,Group=o.Group,IdentityKnown=true,Position=position,Powered=o.GeneratorPowered });
                }
                if (!PlacementPolicy.Included(o.Kind,o.BuildType)) continue;
                result.Buildings.Add(new ObjectObservation { Kind=o.Kind, BuildType=o.BuildType, InstanceId=o.InstanceId, VehicleInstanceId=o.VehicleInstanceId,
                    VehicleSubvehicleIndex=o.VehicleSubvehicleIndex, IsVehicleAttached=o.IsVehicleAttached, AssetId=o.AssetId,
                    AssetGuid=o.AssetGuid, IsStorage=o.IsStorage, IsGenerator=o.IsGenerator, GeneratorPowered=o.GeneratorPowered,
                    Health=o.Health, MaxHealth=o.MaxHealth, Owner=o.Owner, Group=o.Group, X=o.X, Y=o.Y, Z=o.Z });
            }
            return result;
        }
    }
}
