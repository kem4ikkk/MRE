using System;
using System.Collections.Generic;

namespace MreDecay
{
    public readonly struct Position3
    {
        public readonly double X, Y, Z;
        public Position3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public bool Valid => Finite(X) && Finite(Y) && Finite(Z);
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    }
    public sealed class ProtectionObject
    {
        public Position3? Position;
        public ulong Owner, Group;
        // false means group/owner provenance is unavailable, not that group is zero.
        public bool IdentityKnown;
        public bool? Powered;
    }
    public readonly struct ProtectionResult
    {
        public readonly bool? SafePresent, GeneratorPresent, GeneratorPowered;
        public readonly ulong SafeOwner, SafeGroup, GeneratorOwner, GeneratorGroup;
        public ProtectionResult(bool? safe, ulong safeOwner, ulong safeGroup, bool? generator, ulong generatorOwner, ulong generatorGroup, bool? powered)
        { SafePresent = safe; SafeOwner = safeOwner; SafeGroup = safeGroup; GeneratorPresent = generator; GeneratorOwner = generatorOwner; GeneratorGroup = generatorGroup; GeneratorPowered = powered; }
    }
    public static class ProtectionSearch
    {
        public const double Radius = 50;
        public static ProtectionResult Find(Position3? building, ulong owner, ulong group, bool identityKnown,
            IReadOnlyList<ProtectionObject> safes, bool safeListComplete,
            IReadOnlyList<ProtectionObject> generators, bool generatorListComplete)
        {
            if (!building.HasValue || !building.Value.Valid || !identityKnown || owner == 0 ||
                !safeListComplete || !generatorListComplete || safes == null || generators == null)
                return Unknown();
            ProtectionObject safe, generator;
            if (!FindOne(building.Value, owner, group, safes, false, out safe) ||
                !FindOne(building.Value, owner, group, generators, true, out generator)) return Unknown();
            return new ProtectionResult(safe != null, safe?.Owner ?? 0, safe?.Group ?? 0,
                generator != null, generator?.Owner ?? 0, generator?.Group ?? 0, generator != null);
        }
        private static ProtectionResult Unknown() => new ProtectionResult(null, 0, 0, null, 0, 0, null);
        private static bool FindOne(Position3 origin, ulong owner, ulong group, IReadOnlyList<ProtectionObject> objects, bool requirePower, out ProtectionObject chosen)
        {
            chosen = null;
            foreach (var item in objects) {
                // Even an invalid unrelated candidate prevents an absence conclusion.
                if (item == null || !item.Position.HasValue || !item.Position.Value.Valid || !item.IdentityKnown || item.Owner == 0 || (requirePower && !item.Powered.HasValue)) return false;
                double dx = origin.X - item.Position.Value.X, dy = origin.Y - item.Position.Value.Y, dz = origin.Z - item.Position.Value.Z;
                // Bounded comparison prevents overflow when coordinates are finite but extreme.
                if (Math.Abs(dx) > Radius || Math.Abs(dy) > Radius || Math.Abs(dz) > Radius) continue;
                if (dx * dx + dy * dy + dz * dz > Radius * Radius) continue;
                if (!DecayModel.SameOwnerOrGroup(owner, group, item.Owner, item.Group)) continue;
                if (requirePower && !item.Powered.Value) continue;
                if (chosen == null) chosen = item;
            }
            return true;
        }
    }
}
