using System;

namespace MreDecay.Adapter
{
    public sealed class ObjectObservation
    {
        public string Kind;
        public string BuildType;
        public uint InstanceId;
        public uint VehicleInstanceId;
        public int VehicleSubvehicleIndex;
        public ushort AssetId, Health, MaxHealth;
        public Guid AssetGuid;
        public bool IsStorage, IsGenerator, IsVehicleAttached;
        public bool? GeneratorPowered;
        public ulong Owner, Group;
        public float X, Y, Z;
    }
}
