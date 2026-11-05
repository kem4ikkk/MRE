using System;
using MreDecay;
using Action = MreDecay.Action;

static class Program
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    static Result Run(double h = 100, double max = 100, bool? city = false, ulong owner = 1, ulong group = 2,
        ulong safeOwner = 1, ulong safeGroup = 0, bool? safe = true, bool? powered = true, bool? raid = false,
        ulong generatorOwner = 1, ulong generatorGroup = 0, bool? generator = true)
        => DecayModel.Evaluate(h, max, city, owner, group, safeOwner, safeGroup, safe, generatorOwner, generatorGroup, generator, powered, raid);

    static void Main()
    {
        Check(DecayModel.CycleSeconds == 300, "cycle");
        Check(Run(safe: false).Health == 99, "outside decay");
        Check(Run(city: true, safe: false).Health == 98, "city decay");
        Check(Run(h: 99).Health == 100, "owner repair");
        Check(Run(h: 50, safeOwner: 3, safeGroup: 2).Health == 51, "group repair");
        Check(Run(h: 50, safeOwner: 3, safeGroup: 3).Health == 49, "foreign safe");
        Check(!Run(h: 50, owner: 0, group: 0, safeOwner: 0, safeGroup: 0).Valid, "unknown owner fails closed");
        Check(Run(h: 50, generatorOwner: 3, generatorGroup: 3).Action == Action.Protected, "foreign generator cannot repair");
        Check(Run(h: 50, generatorOwner: 3, generatorGroup: 2).Health == 51, "group generator repairs");
        Check(Run(h: 50, safeOwner: 3, safeGroup: 2, generatorOwner: 4, generatorGroup: 3).Action == Action.Protected, "overlap with foreign generator");
        Check(!Run(h: 50, safeOwner: 0).Valid && !Run(h: 50, generatorOwner: 0).Valid, "unknown identities fail closed");
        Check(!Run(h: 50, city: null).Valid && !Run(h: 50, raid: null).Valid, "unknown zone or raid fails closed");
        Check(!Run(h: 50, safe: null).Valid && !Run(h: 50, generator: null).Valid && !Run(h: 50, powered: null).Valid, "unknown presence or power fails closed");
        Check(Run(h: 50, powered: false).Action == Action.Protected, "no generator protects only");
        Check(Run(h: 50, raid: true).Action == Action.Protected, "raid blocks repair");
        Check(Run(h: 50, safe: false, powered: true).Action == Action.Decay, "generator alone insufficient");
        Check(Run(h: 99.9).Health == 100, "repair capped");
        Check(Run(h: 100).Action == Action.Protected, "full health");
        Check(Run(h: 0).Action == Action.Destroyed, "zero cannot repair");
        Check(Run(h: 0.5, safe: false).Health == 0, "damage capped");
        Check(!Run(h: -1).Valid && !Run(h: 101).Valid, "invalid health");
        Check(!Run(max: 0).Valid && !Run(max: -1).Valid, "invalid max");
        Check(!Run(h: double.NaN).Valid && !Run(max: double.PositiveInfinity).Valid, "nonfinite values");
        var fake = new FakeBuilding { Health = 0.5, MaxHealth = 100 };
        fake.Tick(false, false); fake.Tick(true, true);
        Check(fake.Destroyed && fake.Health == 0, "fake destruction irreversible");
        var blocks = new RaidBlock();
        Check(blocks.Record(1, 2, true, 10), "record confirmed player hit");
        Check(blocks.IsBlocked(1, 2, 609) == true, "last second blocked");
        Check(blocks.IsBlocked(3, 2, 609) == true, "group base blocked");
        Check(blocks.IsBlocked(3, 4, 609) == false, "foreign base isolated");
        Check(blocks.IsBlocked(1, 2, 610) == false, "exact boundary");
        Check(blocks.Record(1, 2, true, 700) && blocks.Record(1, 2, true, 900), "repeated damage");
        Check(blocks.IsBlocked(1, 2, 1499) == true && blocks.IsBlocked(1, 2, 1500) == false, "last hit extends ttl");
        Check(blocks.Record(1, 2, false, 1501) && blocks.IsBlocked(1, 2, 1501) == false, "unconfirmed does not start ttl");
        Check(blocks.IsBlocked(1, 2, 1500) == null && blocks.IsBlocked(1, 2, 2000) == null, "rollback latches closed");
        Check(new RaidBlock().IsBlocked(1, 0, -1) == null, "negative clock");
        Check(new RaidBlock().IsBlocked(1, 0, null) == null, "unknown clock");
        Check(new RaidBlock().IsBlocked(0, 0, 1) == null, "unknown owner");
        Check(!new RaidBlock().Record(1, 0, null, 1), "unknown event fails closed");
        var before = new RaidBlock();
        Check(before.Record(7, 8, true, 100), "snapshot event");
        var saved = before.Capture(250);
        Check(saved.Owners[7] == 450 && saved.Groups[8] == 450, "snapshot stores remaining duration");
        var after = new RaidBlock();
        Check(after.Restore(saved, 5), "restore with new clock epoch");
        Check(after.IsBlocked(7, 0, 454) == true && after.IsBlocked(7, 0, 455) == false, "offline time excluded and exact boundary");
        Check(after.IsBlocked(9, 8, 455) == false, "group exact boundary");
        var renewed = new RaidBlock();
        Check(renewed.Restore(saved, 0) && renewed.Record(7, 8, true, 100) && renewed.IsBlocked(7, 0, 699) == true && renewed.IsBlocked(7, 0, 700) == false, "new event extends restored timer");
        var expired = new RaidBlock();
        Check(expired.Record(1, 0, true, 0) && expired.Capture(600).Owners.Count == 0, "expired entries omitted");
        var invalid = new Snapshot(); invalid.Owners.Add(1, 601);
        var failed = new RaidBlock();
        Check(!failed.Restore(invalid, 0) && failed.IsBlocked(1, 0, 0) == null, "invalid snapshot latches closed");
        invalid.Owners.Clear(); invalid.Groups.Add(0, 1);
        Check(!new RaidBlock().Restore(invalid, 0), "zero snapshot id invalid");
        Check(!new RaidBlock().Restore(null, 0), "missing snapshot invalid");
        var inUse = new RaidBlock();
        Check(inUse.IsBlocked(1, 0, 1) == false && !inUse.Restore(saved, 2) && inUse.IsBlocked(1, 0, 2) == null, "restore only fresh instance");
        var fractional = FractionalHealth.Restore(50, 0, 0);
        ushort hp;
        Check(fractional.Apply(50, 50, false, 1, out hp) && hp == 50 && fractional.DamageRemainder == 50, "half hp retained");
        Check(fractional.Apply(hp, 50, false, 1, out hp) && hp == 49 && fractional.DamageRemainder == 0, "two halves deal one hp");
        Check(fractional.Apply(hp, 50, true, 1, out hp) && hp == 49 && fractional.RepairRemainder == 50, "repair half retained");
        Check(fractional.Apply(hp, 50, false, 1, out hp) && hp == 49 && fractional.DamageRemainder == 50 && fractional.RepairRemainder == 50, "remainders independent across mode");
        Check(fractional.Apply(hp, 50, true, 1, out hp) && hp == 50 && fractional.RepairRemainder == 0 && fractional.DamageRemainder == 50, "full repair clears only repair fraction");
        Check(fractional.Apply(hp, 50, true, 1, out hp) && fractional.RepairRemainder == 0, "full health accumulates no repair");
        Check(fractional.Apply(hp, 51, false, 1, out hp) == false && hp == 50 && fractional.DamageRemainder == 50, "max change fails without mutation");
        Check(!fractional.Apply(51, 50, false, 1, out hp) && !fractional.Apply(50, 50, true, 2, out hp), "invalid hp and rate");
        Check(FractionalHealth.Restore(0, 0, 0) == null && FractionalHealth.Restore(50, 100, 0) == null && FractionalHealth.Restore(50, 0, -1) == null, "invalid persisted state");
        var tiny = FractionalHealth.Restore(1, 0, 0); hp = 1;
        for (int i = 0; i < 99; i++) Check(tiny.Apply(hp, 1, false, 1, out hp) && hp == 1, "tiny fractional step");
        Check(tiny.Apply(hp, 1, false, 1, out hp) && hp == 0 && tiny.DamageRemainder == 0, "tiny eventually destroyed");
        Check(tiny.Apply(0, 1, true, 1, out hp) && hp == 0, "zero cannot resurrect");
        var huge = FractionalHealth.Restore(ushort.MaxValue, 99, 99);
        Check(huge.Apply(ushort.MaxValue, ushort.MaxValue, false, 2, out hp) && hp == 64224 && huge.DamageRemainder == 69, "maximum exact without overflow");
        var safeObject = new ProtectionObject { Position = new Position3(30, 40, 0), Owner = 1, IdentityKnown = true };
        var generatorObject = new ProtectionObject { Position = new Position3(0, 0, 50), Owner = 3, Group = 2, IdentityKnown = true, Powered = true };
        var safes = new[] { safeObject }; var generators = new[] { generatorObject };
        var near = ProtectionSearch.Find(new Position3(0, 0, 0), 1, 2, true, safes, true, generators, true);
        Check(near.SafePresent == true && near.GeneratorPresent == true && near.GeneratorPowered == true, "3d boundary and group generator");
        var integrated = DecayModel.Evaluate(50, 100, false, 1, 2, near.SafeOwner, near.SafeGroup, near.SafePresent, near.GeneratorOwner, near.GeneratorGroup, near.GeneratorPresent, near.GeneratorPowered, false);
        Check(integrated.Action == Action.Repair, "search result safely feeds model");
        generatorObject.Position = new Position3(0, 0, 50.001);
        Check(ProtectionSearch.Find(new Position3(0, 0, 0), 1, 2, true, safes, true, generators, true).GeneratorPresent == false, "outside boundary");
        generatorObject.Position = new Position3(0, 0, 0); generatorObject.Group = 0;
        Check(ProtectionSearch.Find(new Position3(0, 0, 0), 1, 2, true, safes, true, generators, true).GeneratorPresent == false, "foreign generator");
        generatorObject.Owner = 1; generatorObject.Powered = false;
        Check(ProtectionSearch.Find(new Position3(0, 0, 0), 1, 2, true, safes, true, generators, true).GeneratorPresent == false, "unpowered ignored");
        var second = new ProtectionObject { Position = new Position3(0, 0, 0), Owner = 1, IdentityKnown = true, Powered = true };
        Check(ProtectionSearch.Find(new Position3(0, 0, 0), 1, 2, true, safes, true, new[] { generatorObject, second }, true).GeneratorPresent == true, "multiple generators");
        second.Position = null;
        Check(ProtectionSearch.Find(new Position3(0, 0, 0), 1, 2, true, safes, true, new[] { generatorObject, second }, true).SafePresent == null, "unknown candidate fails closed");
        Check(ProtectionSearch.Find(new Position3(0, 0, 0), 1, 2, true, safes, false, generators, true).SafePresent == null, "incomplete list fails closed");
        Check(ProtectionSearch.Find(null, 1, 2, true, safes, true, generators, true).SafePresent == null, "unknown building position");
        Check(ProtectionSearch.Find(new Position3(double.NaN, 0, 0), 1, 2, true, safes, true, generators, true).SafePresent == null, "nonfinite building position");
        Check(ProtectionSearch.Find(new Position3(0, 0, 0), 0, 2, true, safes, true, generators, true).SafePresent == null, "zero owner");
        Check(ProtectionSearch.Find(new Position3(0, 0, 0), 1, 2, true, safes, true, null, true).SafePresent == null, "null list not empty list");
        Console.WriteLine("PASS: " + count + " checks");
    }

    sealed class FakeBuilding
    {
        public double Health, MaxHealth;
        public bool Destroyed;
        public void Tick(bool safe, bool powered)
        {
            if (Destroyed) return;
            var result = DecayModel.Evaluate(Health, MaxHealth, false, 1, 0, 1, 0, safe, 1, 0, true, powered, false);
            if (!result.Valid) throw new Exception("invalid fake");
            Health = result.Health;
            Destroyed = Health == 0;
        }
    }
}
