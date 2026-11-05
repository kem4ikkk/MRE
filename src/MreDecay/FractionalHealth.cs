using System;

namespace MreDecay
{
    // Remainders are hundredths of one HP. Caller persists this state by stable object ID.
    public sealed class FractionalHealth
    {
        public ushort MaxHealth { get; private set; }
        public int DamageRemainder { get; private set; }
        public int RepairRemainder { get; private set; }

        private FractionalHealth(ushort max, int damage, int repair)
        { MaxHealth = max; DamageRemainder = damage; RepairRemainder = repair; }

        public static FractionalHealth Restore(ushort max, int damage, int repair)
        {
            if (max == 0 || damage < 0 || damage >= 100 || repair < 0 || repair >= 100) return null;
            return new FractionalHealth(max, damage, repair);
        }

        // Invalid inputs return false and leave health and both remainders unchanged.
        // A max-health change requires an explicit policy/migration, never implicit reset.
        public bool Apply(ushort health, ushort observedMax, bool repair, int percent, out ushort nextHealth)
        {
            nextHealth = health;
            if (observedMax != MaxHealth || health > MaxHealth || (percent != 1 && percent != 2) || (repair && percent != 1)) return false;
            // No resurrection. At full health no repair debt may accumulate.
            if (health == 0 || (repair && health == MaxHealth)) return true;
            int numerator = MaxHealth * percent + (repair ? RepairRemainder : DamageRemainder);
            int whole = numerator / 100;
            int remainder = numerator % 100;
            if (repair) {
                int target = Math.Min(MaxHealth, health + whole);
                nextHealth = (ushort)target;
                RepairRemainder = target == MaxHealth ? 0 : remainder;
            } else {
                int target = Math.Max(0, health - whole);
                nextHealth = (ushort)target;
                DamageRemainder = target == 0 ? 0 : remainder;
            }
            return true;
        }
    }
}
