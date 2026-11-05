using System;
using System.Collections.Generic;
using MreDecay.Zones;

namespace MreDecay.Engine
{
    public sealed class Building
    {
        public string Id;
        public ushort Health, MaxHealth, PersistedMaxHealth;
        public int DamageRemainder, RepairRemainder;
        public ulong Owner, Group;
        public bool IdentityKnown;
        public Position3? Position;
        public bool? RaidBlocked;
    }
    public sealed class Snapshot
    {
        public bool Confirmed, BuildingsComplete, ZonesConfirmed, SafesComplete, GeneratorsComplete;
        public long Revision, Step;
        public IReadOnlyList<Building> Buildings;
        public IReadOnlyList<ProtectionObject> Safes, Generators;
        public ZoneSet Zones;
    }
    public sealed class Change
    {
        public string Id { get; internal set; }
        public ushort Health { get; internal set; }
        public int DamageRemainder { get; internal set; }
        public int RepairRemainder { get; internal set; }
    }
    public sealed class Plan
    {
        internal long Revision, Step;
        internal Change[] Changes;
        public IReadOnlyList<Change> Results => Array.AsReadOnly(Changes);
    }
    public static class StepEngine
    {
        // All-or-nothing planning: no supplied state or game object is mutated.
        public static Plan Prepare(Snapshot snapshot)
        {
            if (snapshot == null || !snapshot.Confirmed || !snapshot.BuildingsComplete || !snapshot.ZonesConfirmed ||
                !snapshot.SafesComplete || !snapshot.GeneratorsComplete || snapshot.Buildings == null || snapshot.Zones == null ||
                snapshot.Safes == null || snapshot.Generators == null || snapshot.Revision < 0 || snapshot.Step < 0) return null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var changes = new List<Change>();
            foreach (var b in snapshot.Buildings) {
                if (b == null || String.IsNullOrWhiteSpace(b.Id) || !ids.Add(b.Id) || !b.IdentityKnown || b.Owner == 0 ||
                    !b.RaidBlocked.HasValue || !b.Position.HasValue || !b.Position.Value.Valid) return null;
                var fraction = FractionalHealth.Restore(b.PersistedMaxHealth, b.DamageRemainder, b.RepairRemainder);
                if (fraction == null || b.PersistedMaxHealth != b.MaxHealth || b.Health > b.MaxHealth) return null;
                bool city;
                try { city = snapshot.Zones.MatchingNames(b.Position.Value.X, b.Position.Value.Z).Length > 0; }
                catch (ArgumentException) { return null; }
                var p = ProtectionSearch.Find(b.Position, b.Owner, b.Group, b.IdentityKnown, snapshot.Safes, true, snapshot.Generators, true);
                if (!p.SafePresent.HasValue || !p.GeneratorPresent.HasValue || !p.GeneratorPowered.HasValue) return null;
                var decision = DecayModel.Evaluate(b.Health, b.MaxHealth, city, b.Owner, b.Group, p.SafeOwner, p.SafeGroup,
                    p.SafePresent, p.GeneratorOwner, p.GeneratorGroup, p.GeneratorPresent, p.GeneratorPowered, b.RaidBlocked);
                if (!decision.Valid) return null;
                ushort hp = b.Health;
                if (decision.Action == MreDecay.Action.Decay && !fraction.Apply(hp, b.MaxHealth, false, city ? 2 : 1, out hp)) return null;
                if (decision.Action == MreDecay.Action.Repair && !fraction.Apply(hp, b.MaxHealth, true, 1, out hp)) return null;
                changes.Add(new Change { Id = b.Id, Health = hp, DamageRemainder = fraction.DamageRemainder, RepairRemainder = fraction.RepairRemainder });
            }
            return new Plan { Revision = snapshot.Revision, Step = snapshot.Step, Changes = changes.ToArray() };
        }
    }
    // Test-only in-memory adapter: application checks revision and strictly increasing step.
    public sealed class FakeGame
    {
        private long revision, lastStep = -1;
        private readonly Dictionary<string, Change> state = new Dictionary<string, Change>(StringComparer.Ordinal);
        public FakeGame(long revision) { if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision)); this.revision = revision; }
        public bool Apply(Plan plan)
        {
            if (plan == null || plan.Revision != revision || plan.Step != lastStep + 1 || revision == long.MaxValue) return false;
            foreach (var item in plan.Changes) state[item.Id] = new Change { Id = item.Id, Health = item.Health, DamageRemainder = item.DamageRemainder, RepairRemainder = item.RepairRemainder };
            lastStep = plan.Step; revision++; return true;
        }
        public Change Get(string id) { Change value; return state.TryGetValue(id, out value) ? new Change { Id = value.Id, Health = value.Health, DamageRemainder = value.DamageRemainder, RepairRemainder = value.RepairRemainder } : null; }
    }
}
