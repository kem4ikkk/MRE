using System;

namespace MreDecay.Adapter
{
    public sealed class ObjectObservation
    {
        public string Kind;
        public uint InstanceId;
        public ushort AssetId, Health, MaxHealth;
        public Guid AssetGuid;
        public bool IsStorage, IsGenerator;
        public ulong Owner, Group;
        public float X, Y, Z;
    }
}
