using System;

namespace MreDecay
{
    // Pure policy only: caller decides zones, proximity, persistence and scheduling.
    public static class DecayModel
    {
        public const int CycleSeconds = 300;

        public static bool SameOwnerOrGroup(ulong owner, ulong group, ulong safeOwner, ulong safeGroup)
        {
            return (owner != 0 && safeOwner != 0 && owner == safeOwner) ||
                   (group != 0 && safeGroup != 0 && group == safeGroup);
        }

        public static Result Evaluate(double health, double maxHealth, bool? inCity,
            ulong owner, ulong group, ulong safeOwner, ulong safeGroup,
            bool? safePresent, ulong generatorOwner, ulong generatorGroup,
            bool? generatorPresent, bool? generatorPowered, bool? raidBlocked)
        {
            if (double.IsNaN(health) || double.IsInfinity(health) ||
                double.IsNaN(maxHealth) || double.IsInfinity(maxHealth) ||
                maxHealth <= 0 || health < 0 || health > maxHealth)
                return new Result(false, health, Action.Invalid);

            // Destruction is irreversible: a dead object is never repaired or resurrected.
            if (health == 0) return new Result(true, 0, Action.Destroyed);

            // Unknown world state or identity cannot justify either damage or repair.
            if (!inCity.HasValue || !safePresent.HasValue || !generatorPresent.HasValue ||
                !generatorPowered.HasValue || !raidBlocked.HasValue || owner == 0 ||
                (safePresent.Value && safeOwner == 0) || (generatorPresent.Value && generatorOwner == 0))
                return new Result(false, health, Action.Invalid);

            bool protectedBySafe = safePresent.Value && SameOwnerOrGroup(owner, group, safeOwner, safeGroup);
            bool ownGenerator = generatorPresent.Value && SameOwnerOrGroup(owner, group, generatorOwner, generatorGroup);
            if (protectedBySafe)
            {
                if (!ownGenerator || !generatorPowered.Value || raidBlocked.Value || health == maxHealth)
                    return new Result(true, health, Action.Protected);
                return new Result(true, Math.Min(maxHealth, health + maxHealth * 0.01), Action.Repair);
            }

            // Provisional policy pending owner confirmation: city 2%, outside 1% per cycle.
            return new Result(true, Math.Max(0, health - maxHealth * (inCity.Value ? 0.02 : 0.01)), Action.Decay);
        }
    }

    public enum Action { Invalid, Destroyed, Protected, Repair, Decay }

    public readonly struct Result
    {
        public readonly bool Valid;
        public readonly double Health;
        public readonly Action Action;
        public Result(bool valid, double health, Action action) { Valid = valid; Health = health; Action = action; }
    }
}
