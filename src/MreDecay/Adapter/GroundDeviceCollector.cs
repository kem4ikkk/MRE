using System;
using System.Collections.Generic;
using MreDecay.Adapter;

namespace MreDecay.IsolatedRuntime
{
    // Converts supported ground objects from a validated diagnostic snapshot.
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
                // Mobile barricades are observed and provenance-checked above, but they
                // are never decay targets or protection devices for ground structures.
                if (o.IsVehicleAttached) continue;
                bool safe=safeGuids.Contains(o.AssetGuid);
                if (safe && (!o.IsStorage || o.Kind!="barricade")) return null;
                var position=new Position3(o.X,o.Y,o.Z);
                if(safe) result.Safes.Add(new ProtectionObject { Owner=o.Owner,Group=o.Group,IdentityKnown=true,Position=position });
                if(o.IsGenerator) {
                    if(!o.GeneratorPowered.HasValue) return null;
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
