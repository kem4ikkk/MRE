using System;
using System.Runtime.CompilerServices;
using System.Globalization;
namespace MreHud
{
    // Weak keys avoid retaining destroyed world objects. A nested hit on another
    // target must not invalidate this target's independent health delta.
    public sealed class DamageTargetSequence
    {
        private sealed class Counter { public long Value; }
        private readonly ConditionalWeakTable<object, Counter> counters = new ConditionalWeakTable<object, Counter>();
        public long Begin(object target) { return ++counters.GetValue(target, key => new Counter()).Value; }
        public bool IsCurrent(object target, long version)
        {
            Counter counter;
            return target != null && counters.TryGetValue(target, out counter) && counter.Value == version;
        }
    }

    public sealed class DamageSlots
    {
        private readonly DateTime[] until;
        private int next;
        public DamageSlots(int count) { if (count < 1) throw new ArgumentOutOfRangeException("count"); until = new DateTime[count]; }
        public int Count { get { return until.Length; } }
        public int Add(DateTime now, TimeSpan lifetime)
        {
            int slot = next;
            next = (next + 1) % until.Length;
            until[slot] = now.Add(lifetime);
            return slot;
        }
        public bool IsVisible(int slot, DateTime now) { return now < until[slot]; }
        public void Clear() { Array.Clear(until, 0, until.Length); next = 0; }
    }

    public enum DamageKind { Resource, Building, PlayerBody, PlayerHead }

    public static class DamageNumber
    {
        public static string Format(int value)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException("value");
            return "-" + value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Color(DamageKind kind)
        {
            switch (kind)
            {
                case DamageKind.Resource: return "#A0A0A0";
                case DamageKind.PlayerBody: return "#40E060";
                case DamageKind.PlayerHead: return "#FF4040";
                default: return "#FFFFFF";
            }
        }

        public static int Calculate(int before, int after, bool protectRemainingHealth)
        {
            if (before <= 0 || after < 0 || after >= before || (protectRemainingHealth && after == 0)) return 0;
            return before - after;
        }
    }
}
