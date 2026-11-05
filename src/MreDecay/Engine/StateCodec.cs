using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MreDecay.Engine
{
    public sealed class StoredObject
    {
        public byte Kind; // 1 structure, 2 barricade
        public uint InstanceId;
        public ushort AssetId, MaxHealth;
        public Guid AssetGuid;
        public int DamageRemainder, RepairRemainder;
    }
    public sealed class StoredState
    {
        public long Cycle, ActiveSeconds;
        public int CycleSeconds;
        public List<StoredObject> Objects = new List<StoredObject>();
        public MreDecay.Snapshot Raid = new MreDecay.Snapshot();
    }
    // IDs are only data here; their stability in the game has not been established.
    public static class StateCodec
    {
        private const int Version = 1, MaxEntries = 100000;
        public static byte[] Encode(StoredState value)
        {
            if (!Validate(value)) return null;
            using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms, Encoding.UTF8, true)) {
                w.Write(Version); w.Write(value.Cycle); w.Write(value.ActiveSeconds); w.Write(value.CycleSeconds); w.Write(value.Objects.Count);
                foreach (var x in value.Objects) { w.Write(x.Kind); w.Write(x.InstanceId); w.Write(x.AssetId); w.Write(x.AssetGuid.ToByteArray()); w.Write(x.MaxHealth); w.Write(x.DamageRemainder); w.Write(x.RepairRemainder); }
                WriteRaid(w, value.Raid.Owners); WriteRaid(w, value.Raid.Groups); w.Flush(); return ms.ToArray();
            }
        }
        public static StoredState Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 16 * 1024 * 1024) return null;
            try { using (var ms = new MemoryStream(bytes, false)) using (var r = new BinaryReader(ms)) {
                if (r.ReadInt32() != Version) return null;
                var s = new StoredState { Cycle = r.ReadInt64(), ActiveSeconds = r.ReadInt64(), CycleSeconds = r.ReadInt32() };
                int count = r.ReadInt32(); if (count < 0 || count > MaxEntries) return null;
                for (int i=0; i<count; i++) s.Objects.Add(new StoredObject { Kind=r.ReadByte(), InstanceId=r.ReadUInt32(), AssetId=r.ReadUInt16(), AssetGuid=new Guid(r.ReadBytes(16)), MaxHealth=r.ReadUInt16(), DamageRemainder=r.ReadInt32(), RepairRemainder=r.ReadInt32() });
                if (!ReadRaid(r, s.Raid.Owners) || !ReadRaid(r, s.Raid.Groups) || ms.Position != ms.Length || !Validate(s)) return null;
                return s;
            } } catch (Exception e) when (e is IOException || e is ArgumentException || e is OverflowException) { return null; }
        }
        private static bool Validate(StoredState s)
        {
            if (s == null || s.Cycle < 0 || s.ActiveSeconds < 0 || s.CycleSeconds < 0 || s.CycleSeconds >= 300 || s.Objects == null || s.Objects.Count > MaxEntries || s.Raid == null || !ValidRaid(s.Raid.Owners) || !ValidRaid(s.Raid.Groups)) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var x in s.Objects) if (x == null || (x.Kind != 1 && x.Kind != 2) || x.InstanceId == 0 || x.AssetId == 0 || x.AssetGuid == Guid.Empty || !keys.Add(x.Kind + ":" + x.InstanceId) || FractionalHealth.Restore(x.MaxHealth,x.DamageRemainder,x.RepairRemainder) == null) return false;
            return true;
        }
        private static bool ValidRaid(Dictionary<ulong,long> values) { if (values == null || values.Count > MaxEntries) return false; foreach (var pair in values) if (pair.Key == 0 || pair.Value <= 0 || pair.Value > RaidBlock.DurationSeconds) return false; return true; }
        private static void WriteRaid(BinaryWriter w, Dictionary<ulong,long> values) { w.Write(values.Count); foreach (var p in values) { w.Write(p.Key); w.Write(p.Value); } }
        private static bool ReadRaid(BinaryReader r, Dictionary<ulong,long> values) { int count=r.ReadInt32(); if (count < 0 || count > MaxEntries) return false; for(int i=0;i<count;i++) { ulong id=r.ReadUInt64(); long seconds=r.ReadInt64(); if (values.ContainsKey(id)) return false; values.Add(id,seconds); } return true; }
    }
}
