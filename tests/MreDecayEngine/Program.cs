using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using MreDecay;
using MreDecay.Engine;
using MreDecay.Adapter;
using MreDecay.Zones;

static class Program
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--check-lock") { long n; byte[] data; using (var competing = new DurableJournal(args[1])) Environment.Exit(competing.Load(out n, out data) ? 1 : 0); }
        var observation = new ObjectObservation { Kind="structure", InstanceId=1, AssetId=51, AssetGuid=Guid.NewGuid(), Owner=1, Health=100, MaxHealth=100, X=1, Y=2, Z=3 };
        Check(ObservationAudit.Valid(new ObjectObservation[0]) && ObservationAudit.Valid(new[]{observation}), "valid observations accepted");
        Check(!ObservationAudit.Valid(null) && !ObservationAudit.Valid(new ObjectObservation[]{null}) && !ObservationAudit.Valid(new[]{observation,observation}), "null and duplicate observations rejected");
        observation.X=float.NaN; Check(!ObservationAudit.Valid(new[]{observation}), "nonfinite position rejected"); observation.X=1;
        observation.Health=101; Check(!ObservationAudit.Valid(new[]{observation}), "invalid health rejected"); observation.Health=100;
        observation.IsStorage=true; Check(!ObservationAudit.Valid(new[]{observation}), "structure cannot be storage"); observation.IsStorage=false;
        observation.AssetId=0; Check(!ObservationAudit.Valid(new[]{observation}), "missing asset rejected"); observation.AssetId=51;
        observation.Owner=0; Check(!ObservationAudit.Valid(new[]{observation}), "missing owner rejected"); observation.Owner=1;
        var building = new Building { Id="a", Health=50, MaxHealth=100, PersistedMaxHealth=100, Owner=1, Group=2, IdentityKnown=true, Position=new Position3(0,0,0), RaidBlocked=false };
        var safe = new ProtectionObject { Owner=1, IdentityKnown=true, Position=new Position3(30,40,0) };
        var gen = new ProtectionObject { Owner=3, Group=2, IdentityKnown=true, Position=new Position3(0,0,50), Powered=true };
        var city = new PolygonZone("city",new[]{new ZonePoint(-1,-1),new ZonePoint(1,-1),new ZonePoint(1,1),new ZonePoint(-1,1)});
        var s = new MreDecay.Engine.Snapshot { Confirmed=true, BuildingsComplete=true, ZonesConfirmed=true, SafesComplete=true, GeneratorsComplete=true, Buildings=new[]{building}, Safes=new[]{safe}, Generators=new[]{gen}, Zones=new ZoneSet(new[]{city}) };
        var plan=StepEngine.Prepare(s); Check(plan!=null && plan.Results[0].Health==51 && building.Health==50,"repair and pure planning");
        var game=new FakeGame(0); Check(game.Apply(plan) && game.Get("a").Health==51,"fake apply"); Check(!game.Apply(plan),"replay rejected");
        building.RaidBlocked=true; Check(StepEngine.Prepare(s).Results[0].Health==50,"raid prevents repair"); building.RaidBlocked=false;
        gen.Powered=false; Check(StepEngine.Prepare(s).Results[0].Health==50,"safe without powered generator protects"); gen.Powered=true;
        safe.Owner=9; Check(StepEngine.Prepare(s).Results[0].Health==48,"city decay foreign safe");
        building.Position=new Position3(2,0,0); Check(StepEngine.Prepare(s).Results[0].Health==49,"outside decay");
        building.Health=50; building.MaxHealth=50; Check(StepEngine.Prepare(s)==null,"changed maximum rejects persisted remainder"); building.PersistedMaxHealth=50; var fractional=StepEngine.Prepare(s); Check(fractional.Results[0].Health==50 && fractional.Results[0].DamageRemainder==50,"fraction accumulated");
        building.DamageRemainder=50; Check(StepEngine.Prepare(s).Results[0].Health==49,"fraction becomes damage");
        building.DamageRemainder=100; Check(StepEngine.Prepare(s)==null,"invalid remainder"); building.DamageRemainder=0;
        s.Buildings=new[]{building,building}; Check(StepEngine.Prepare(s)==null,"duplicate id"); s.Buildings=new[]{building};
        s.Confirmed=false; Check(StepEngine.Prepare(s)==null,"unconfirmed snapshot"); s.Confirmed=true;
        s.SafesComplete=false; Check(StepEngine.Prepare(s)==null,"incomplete list"); s.SafesComplete=true;
        s.ZonesConfirmed=false; Check(StepEngine.Prepare(s)==null,"unconfirmed zones"); s.ZonesConfirmed=true;
        building.RaidBlocked=null; Check(StepEngine.Prepare(s)==null,"unknown raid"); building.RaidBlocked=false;
        gen.Position=null; Check(StepEngine.Prepare(s)==null,"unknown device");
        gen.Position=new Position3(0,0,0);
        var bad=new Building { Id="bad", Health=50, MaxHealth=100, Owner=1, IdentityKnown=true, Position=new Position3(0,0,0), RaidBlocked=null };
        s.Buildings=new[]{building,bad};
        ushort before=building.Health; int remainderBefore=building.DamageRemainder;
        Check(StepEngine.Prepare(s)==null && building.Health==before && building.DamageRemainder==remainderBefore && bad.Health==50,"invalid second object rejects whole plan without mutation");
        s.Buildings=new[]{building}; s.Step=2; s.Revision=0;
        var skipped=StepEngine.Prepare(s); var fresh=new FakeGame(0);
        Check(!fresh.Apply(skipped) && fresh.Get("a")==null,"skip step rejected without apply");
        s.Step=0; s.Revision=1; var stale=StepEngine.Prepare(s);
        Check(!fresh.Apply(stale) && fresh.Get("a")==null,"stale revision rejected without apply");
        s.Revision=0; var frozen=StepEngine.Prepare(s); ushort planned=frozen.Results[0].Health;
        building.Health=1; s.Revision=1;
        Check(frozen.Results[0].Health==planned,"plan isolated from later snapshot mutation");
        var changedGame=new FakeGame(1);
        Check(!changedGame.Apply(frozen) && changedGame.Get("a")==null,"changed revision rejects old plan");
        building.Health=50; s.Revision=0;
        var first=StepEngine.Prepare(s); Check(fresh.Apply(first) && !fresh.Apply(first),"duplicate plan rejected after successful apply");
        s.Step=1; s.Revision=1; var next=StepEngine.Prepare(s); Check(fresh.Apply(next),"sequential step and revision accepted");
        var evidence = new RuntimeEvidence { ZonesVerified=true, AssetsVerified=true, WorldSnapshotVerified=true, RaidEventsVerified=true, DurableStateVerified=true, WorldStateTransactionVerified=true, RecoveryVerified=true };
        var missing = new RuntimeEvidence();
        Check(!new RuntimeGate().Start(missing, 0, false), "missing evidence blocks start");
        Check(!new RuntimeGate().Start(evidence, 0, true), "unresolved transaction blocks restart");
        var gate = new RuntimeGate(); Check(gate.Start(evidence, 1000, false), "verified gate starts");
        Check(!gate.Poll(evidence, 1299) && gate.Poll(evidence, 1300), "step after 300 active seconds");
        Check(gate.Complete(true) && !gate.Poll(evidence, 1300), "no replay");
        Check(gate.Poll(evidence, 2300) && gate.Complete(true) && !gate.Poll(evidence, 2300), "no catch up burst");
        Check(!gate.Poll(evidence, 2299) && !gate.Poll(evidence, 2600), "clock rollback fences gate");
        var failure = new RuntimeGate(); Check(failure.Start(evidence, 0, false) && failure.Poll(evidence, 300) && !failure.Complete(false) && !failure.Poll(evidence, 600), "unknown commit fences gate");
        var restart = new RuntimeGate(); Check(restart.Start(evidence, 100000, false) && !restart.Poll(evidence, 100000), "restart does not count downtime");
        var stored=new StoredState { Cycle=3, ActiveSeconds=1000, CycleSeconds=100 };
        stored.Objects.Add(new StoredObject { Kind=1, InstanceId=42, AssetId=7, AssetGuid=Guid.NewGuid(), MaxHealth=150, DamageRemainder=50, RepairRemainder=25 });
        var raid=new RaidBlock(); Check(raid.Record(123,456,true,0),"record raid for persistence"); stored.Raid=raid.Capture(10);
        var encoded=StateCodec.Encode(stored); var decoded=StateCodec.Decode(encoded);
        Check(decoded!=null && decoded.Objects[0].DamageRemainder==50 && decoded.Raid.Owners[123]==590 && decoded.CycleSeconds==100,"state codec round trip");
        var restoredRaid=new RaidBlock(); Check(restoredRaid.Restore(decoded.Raid,10000) && restoredRaid.IsBlocked(123,456,10000)==true,"raid remaining survives offline time");
        stored.Objects.Add(stored.Objects[0]); Check(StateCodec.Encode(stored)==null,"duplicate object rejected"); stored.Objects.RemoveAt(1);
        stored.Objects[0].DamageRemainder=100; Check(StateCodec.Encode(stored)==null,"invalid fraction rejected"); stored.Objects[0].DamageRemainder=50;
        Check(StateCodec.Decode(new byte[]{1,2})==null,"truncated codec rejected");
        string dir=Path.Combine(Path.GetTempPath(), "mre-decay-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try {
            string path=Path.Combine(dir,"state.dat"); long sequence; byte[] state;
            using (var missingJournal=new DurableJournal(path)) Check(!missingJournal.Load(out sequence,out state),"missing state fails closed");
            var journal=new DurableJournal(path); Check(journal.Bootstrap(encoded),"explicit bootstrap");
            using (var competitor=new DurableJournal(path)) Check(!competitor.Load(out sequence,out state),"exclusive writer lock");
            var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--check-lock \"" + path + "\"") { UseShellExecute=false });
            Check(child.WaitForExit(10000) && child.ExitCode==0,"exclusive writer lock across processes");
            Check(journal.Load(out sequence,out state) && sequence==0 && StateCodec.Decode(state).Cycle==3,"load bootstrap");
            stored.Cycle=4; byte[] nextState=StateCodec.Encode(stored); Check(journal.Begin(0,nextState),"durable intent");
            Check(journal.Commit(1),"originating writer commits intent");
            journal.Dispose(); journal=new DurableJournal(path);
            Check(journal.Load(out sequence,out state) && sequence==1 && StateCodec.Decode(state).Cycle==4,"committed state survives reopen");
            Check(!journal.Begin(0,nextState),"replayed sequence blocks");
            journal.Dispose(); journal=new DurableJournal(path);
            Check(journal.Begin(1,nextState),"second durable intent");
            journal.Dispose();
            using (var recovery=new DurableJournal(path)) Check(!recovery.Commit(2),"new writer cannot commit orphaned intent");
            using (var recovery=new DurableJournal(path)) Check(!recovery.Load(out sequence,out state),"crash after intent blocks recovery");
            File.WriteAllBytes(path+".tmp",new byte[]{1});
            journal.Dispose(); using (var recovery=new DurableJournal(path)) Check(!recovery.Load(out sequence,out state),"interrupted publication blocks");
            File.Delete(path+".tmp"); var corrupt=File.ReadAllBytes(path); corrupt[0]^=1; File.WriteAllBytes(path,corrupt);
            using (var recovery=new DurableJournal(path)) Check(!recovery.Load(out sequence,out state),"corruption blocks");
            using (var recovery=new DurableJournal(path)) Check(!recovery.Bootstrap(new byte[]{0}),"existing state cannot bootstrap");
            File.Delete(path); using (var recovery=new DurableJournal(path)) Check(!recovery.Load(out sequence,out state),"deleted state blocks");
        } finally { Directory.Delete(dir,true); }
        string concurrentDir=Path.Combine(Path.GetTempPath(), "mre-decay-concurrent-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(concurrentDir);
        try {
            string concurrentPath=Path.Combine(concurrentDir,"state.dat");
            using (var concurrent=new DurableJournal(concurrentPath)) {
                Check(concurrent.Bootstrap(encoded),"concurrent test bootstrap");
                var a=Task.Run(() => concurrent.Begin(0, encoded)); var b=Task.Run(() => concurrent.Begin(0, encoded));
                Task.WaitAll(a,b); Check(a.Result != b.Result,"only one concurrent intent succeeds");
                Check(!concurrent.Commit(1),"conflicting call fences writer");
            }
        } finally { Directory.Delete(concurrentDir,true); }
        Console.WriteLine("PASS: " + count + " engine checks");
    }
}
