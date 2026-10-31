using System;
using System.Collections.Generic;

namespace MreInterface
{
    public sealed class CombatRecord
    {
        public DateTime AtUtc { get; internal set; }
        public ulong AttackerId { get; internal set; }
        public string AttackerName { get; internal set; }
        public ulong VictimId { get; internal set; }
        public string VictimName { get; internal set; }
        public byte Damage { get; internal set; }
        public string Cause { get; internal set; }
        public string Limb { get; internal set; }
        public ushort WeaponId { get; internal set; }
        public bool IsKill { get; internal set; }
    }

    public sealed class RecentRecords<T>
    {
        private readonly int capacity;
        private readonly Queue<T> records = new Queue<T>();
        public RecentRecords(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException("capacity"); this.capacity = capacity; }
        public void Add(T item) { records.Enqueue(item); while (records.Count > capacity) records.Dequeue(); }
        public T[] Snapshot() { T[] values = records.ToArray(); Array.Reverse(values); return values; }
    }
}
