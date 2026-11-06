using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using MreDecay;
using MreDecay.Engine;
using MreDecay.Adapter;
using MreDecay.IsolatedRuntime;
using MreDecay.Zones;
using EngineSnapshot = MreDecay.Engine.Snapshot;

static class Program
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    sealed class CheckpointFake : ICheckpointBackend {
        public int Calls, FailAt, ReenterAt; public bool Throw, ReenterStart; public long Generation; public CheckpointCoordinator Owner;
        bool Next() { Calls++; if (Calls == ReenterAt) { if (ReenterStart) Owner.Start(0); else Owner.Execute(new byte[]{9}); } if (Calls != FailAt) return true; if (Throw) throw new Exception(); return false; }
        public bool VerifyCurrent(long generation) { return Next() && generation == Generation; }
        public bool WriteIntent(long generation, byte[] state) { return Next(); }
        public bool ApplyWorld() { return Next(); }
        public bool SaveAndVerifyWorld() { return Next(); }
        public bool PublishStateAndAnchor(long generation, byte[] state) { if (!Next()) return false; Generation=generation; return true; }
    }
    sealed class RuntimePipelineFake : IDecayRuntimeAdapter
    {
        public RuntimeEvidence Evidence;
        public EngineSnapshot Snapshot;
        public Plan LastPlan;
        public DecayRuntimePipeline Pipeline;
        public bool RevokeDuringObserve, RevokeDuringAllowlist, ReenterOnReadiness;
        public bool CommitResult = true, OnServerThread = true, Busy, Faulted;
        public int ReadinessCalls, ObserveCalls, AllowlistCalls, CommitCalls;
        public int ThreadChecks, PostRefreshThreadChecks;
        public System.Action OnThreadCheck;
        public bool IsOnServerThread { get { ThreadChecks++; if(ReadinessCalls>=2) PostRefreshThreadChecks++; if(OnThreadCheck!=null) OnThreadCheck(); return OnServerThread; } }
        public bool IsBusy { get { return Busy; } }
        public bool IsFaulted { get { return Faulted; } }
        public RuntimeEvidence GetVerifiedReadiness()
        {
            ReadinessCalls++;
            if (ReenterOnReadiness) { ReenterOnReadiness=false; Pipeline.Execute(true,this); }
            return Evidence;
        }
        public bool TryObserve(out EngineSnapshot snapshot)
        {
            ObserveCalls++;
            if (RevokeDuringObserve) Evidence.VehicleCoverageVerified=false;
            snapshot=Snapshot;
            return true;
        }
        public bool IsAllowedTarget(Building building)
        {
            AllowlistCalls++;
            if (RevokeDuringAllowlist) Evidence.WorldSnapshotVerified=false;
            return true;
        }
        public bool CommitCheckedAtomically(EngineSnapshot snapshot, Plan plan) { CommitCalls++; LastPlan=plan; return CommitResult; }
    }

    static RuntimeEvidence ReadyRuntimeEvidence() => new RuntimeEvidence {
        ZonesVerified=true, AssetsVerified=true, WorldSnapshotVerified=true, RaidEventsVerified=true,
        DurableStateVerified=true, WorldStateTransactionVerified=true, RecoveryVerified=true,
        PersistentIdentityVerified=true, VehicleCoverageVerified=true
    };

    static EngineSnapshot RuntimePipelineSnapshot() => new EngineSnapshot {
        Confirmed=true, BuildingsComplete=true, ZonesConfirmed=true, SafesComplete=true, GeneratorsComplete=true,
        Buildings=new[]{ new Building { Id="structure:1", Health=100, MaxHealth=100, PersistedMaxHealth=100,
            Owner=1, Group=0, IdentityKnown=true, Position=new Position3(100,0,100), RaidBlocked=false } },
        Safes=Array.Empty<ProtectionObject>(), Generators=Array.Empty<ProtectionObject>(),
        Zones=new ZoneSet(new[]{ new PolygonZone("zone",new[]{new ZonePoint(-5,-5),new ZonePoint(5,-5),new ZonePoint(5,5),new ZonePoint(-5,5)}) })
    };

    static void RuntimePipelineChecks()
    {
        var disabledPipeline=new DecayRuntimePipeline();
        var disabledAdapter=new RuntimePipelineFake { Evidence=ReadyRuntimeEvidence(), Snapshot=RuntimePipelineSnapshot(), Pipeline=disabledPipeline };
        Check(disabledPipeline.Execute(false,disabledAdapter)==RuntimePipelineStatus.Disabled && disabledAdapter.ReadinessCalls==0 && disabledAdapter.ObserveCalls==0 && disabledAdapter.CommitCalls==0,
            "disabled pipeline calls no adapter");

        var gates=new Action<RuntimeEvidence>[] {
            e=>e.ZonesVerified=false, e=>e.AssetsVerified=false, e=>e.WorldSnapshotVerified=false,
            e=>e.RaidEventsVerified=false, e=>e.DurableStateVerified=false, e=>e.WorldStateTransactionVerified=false,
            e=>e.RecoveryVerified=false, e=>e.PersistentIdentityVerified=false, e=>e.VehicleCoverageVerified=false
        };
        foreach(var removeGate in gates) {
            var pipeline=new DecayRuntimePipeline(); var adapter=new RuntimePipelineFake { Evidence=ReadyRuntimeEvidence(), Snapshot=RuntimePipelineSnapshot(), Pipeline=pipeline };
            removeGate(adapter.Evidence);
            Check(pipeline.Execute(true,adapter)==RuntimePipelineStatus.BlockedByReadiness && adapter.ReadinessCalls==1 && adapter.ObserveCalls==0 && adapter.AllowlistCalls==0 && adapter.CommitCalls==0,
                "missing readiness gate blocks before observation and mutation");
        }

        foreach(var invalidHost in new[]{ new RuntimePipelineFake { OnServerThread=false }, new RuntimePipelineFake { Busy=true }, new RuntimePipelineFake { Faulted=true } }) {
            invalidHost.Evidence=ReadyRuntimeEvidence(); invalidHost.Snapshot=RuntimePipelineSnapshot(); invalidHost.Pipeline=new DecayRuntimePipeline();
            Check(invalidHost.Pipeline.Execute(true,invalidHost)==RuntimePipelineStatus.BlockedByReadiness && invalidHost.ReadinessCalls==0 && invalidHost.ObserveCalls==0 && invalidHost.CommitCalls==0,
                "wrong thread, busy host, or faulted host blocks before callbacks");
        }

        foreach(bool revokeDuringObserve in new[]{true,false}) {
            var pipeline=new DecayRuntimePipeline(); var adapter=new RuntimePipelineFake { Evidence=ReadyRuntimeEvidence(), Snapshot=RuntimePipelineSnapshot(), Pipeline=pipeline,
                RevokeDuringObserve=revokeDuringObserve, RevokeDuringAllowlist=!revokeDuringObserve };
            Check(pipeline.Execute(true,adapter)==RuntimePipelineStatus.BlockedByReadiness && adapter.ObserveCalls==1 && adapter.CommitCalls==0,
                "readiness revoked during observe or allowlist blocks commit");
        }

        var reentrantPipeline=new DecayRuntimePipeline(); var reentrantAdapter=new RuntimePipelineFake { Evidence=ReadyRuntimeEvidence(), Snapshot=RuntimePipelineSnapshot(), Pipeline=reentrantPipeline, ReenterOnReadiness=true };
        Check(reentrantPipeline.Execute(true,reentrantAdapter)==RuntimePipelineStatus.BlockedByReadiness && reentrantPipeline.IsFaulted && reentrantAdapter.ObserveCalls==0,
            "reentrant readiness callback fences coordinator");

        var propertyReentryPipeline=new DecayRuntimePipeline(); var propertyReentryAdapter=new RuntimePipelineFake { Evidence=ReadyRuntimeEvidence(), Snapshot=RuntimePipelineSnapshot(), Pipeline=propertyReentryPipeline };
        propertyReentryAdapter.OnThreadCheck=()=>{ if(propertyReentryAdapter.ReadinessCalls==2 && propertyReentryAdapter.PostRefreshThreadChecks==2) { propertyReentryAdapter.OnThreadCheck=null; propertyReentryPipeline.Execute(true,propertyReentryAdapter); } };
        var propertyReentryStatus=propertyReentryPipeline.Execute(true,propertyReentryAdapter);
        Check(propertyReentryStatus==RuntimePipelineStatus.BlockedByReadiness && propertyReentryPipeline.IsFaulted && propertyReentryAdapter.ReadinessCalls==2 && propertyReentryAdapter.PostRefreshThreadChecks==2 &&
            propertyReentryAdapter.ObserveCalls==1 && propertyReentryAdapter.AllowlistCalls==1 && propertyReentryAdapter.CommitCalls==0,
            "reentry in server-thread check immediately before commit fences write: status="+propertyReentryStatus+", checks="+propertyReentryAdapter.ThreadChecks+", readiness="+propertyReentryAdapter.ReadinessCalls+", observed="+propertyReentryAdapter.ObserveCalls+", allowlist="+propertyReentryAdapter.AllowlistCalls+", commits="+propertyReentryAdapter.CommitCalls);

        var appliedPipeline=new DecayRuntimePipeline(); var appliedAdapter=new RuntimePipelineFake { Evidence=ReadyRuntimeEvidence(), Snapshot=RuntimePipelineSnapshot(), Pipeline=appliedPipeline };
        Check(appliedPipeline.Execute(true,appliedAdapter)==RuntimePipelineStatus.Applied && appliedAdapter.CommitCalls==1 && appliedAdapter.LastPlan.Results.Count==appliedAdapter.Snapshot.Buildings.Count,
            "all evidence and full plan reach single atomic adapter callback");

        var failedPipeline=new DecayRuntimePipeline(); var failedAdapter=new RuntimePipelineFake { Evidence=ReadyRuntimeEvidence(), Snapshot=RuntimePipelineSnapshot(), Pipeline=failedPipeline, CommitResult=false };
        Check(failedPipeline.Execute(true,failedAdapter)==RuntimePipelineStatus.CommitFailed && failedPipeline.IsFaulted && failedAdapter.CommitCalls==1,
            "uncertain commit fences coordinator");
        Check(failedPipeline.Execute(true,failedAdapter)==RuntimePipelineStatus.BlockedByReadiness && failedAdapter.ObserveCalls==1 && failedAdapter.CommitCalls==1,
            "failed commit cannot be automatically retried");
    }
    static void Main(string[] args)
    {
        RuntimePipelineChecks();
        if (args.Length == 3 && args[0] == "--crash-rehearsal") {
            string root=args[1], point=args[2], world=Path.Combine(root,"world"), path=Path.Combine(root,"journal","state.dat");
            byte[] state=File.ReadAllBytes(Path.Combine(root,"state.bin")); var fingerprint=IsolatedWorldFingerprint.Compute(world);
            System.Action<string> mark = stage => { using (var f=new FileStream(Path.Combine(root,"marker"),FileMode.Create,FileAccess.Write,FileShare.ReadWrite)) { byte[] b=System.Text.Encoding.ASCII.GetBytes(stage); f.Write(b,0,b.Length); f.Flush(true); } };
            using (var backend=new IsolatedCheckpointBackend(world,path,0,state,fingerprint,
                () => { if (point=="intent") { mark("intent"); System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite); } File.WriteAllBytes(Path.Combine(world,"Structures.dat"),new byte[]{3}); if (point=="apply") { mark("apply"); System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite); } return true; },
                () => { using (var f=new FileStream(Path.Combine(world,"Structures.dat"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)) f.Flush(true); if (point=="save") { mark("save"); System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite); } return true; })) {
                var transaction=new CheckpointCoordinator(backend); if (!transaction.Start(0)) Environment.Exit(4); transaction.Execute(state);
            }
            Environment.Exit(5);
        }
        if (args.Length == 2 && args[0] == "--check-lock") { long n; byte[] data; using (var competing = new DurableJournal(args[1])) Environment.Exit(competing.Load(out n, out data) ? 1 : 0); }
        var sourceBytes = System.Text.Encoding.UTF8.GetBytes("synthetic PEI"); var chartBytes = System.Text.Encoding.UTF8.GetBytes("synthetic chart");
        Func<byte[],string> digest = bytes => { using (var sha=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); };
        string document = "{\"map\":\"PEI\",\"status\":\"VERIFIED\",\"source_sha256\":\"" + digest(sourceBytes) + "\",\"chart_sha256\":\"" + digest(chartBytes) + "\",\"zones\":[{\"instance_id\":1,\"name\":\"Holman Isle\",\"polygons\":[[{\"x\":0,\"z\":0},{\"x\":2,\"z\":0},{\"x\":2,\"z\":2},{\"x\":0,\"z\":2}],[{\"x\":4,\"z\":0},{\"x\":6,\"z\":0},{\"x\":6,\"z\":2},{\"x\":4,\"z\":2}]]}]}";
        var expectedLocations = new System.Collections.Generic.Dictionary<long,string> { [1] = "Holman Isle" };
        var docBytes=System.Text.Encoding.UTF8.GetBytes(document); var reviewed=ReviewedZoneLoader.Load(docBytes,digest(docBytes),sourceBytes,chartBytes,expectedLocations);
        Check(reviewed != null && reviewed.MatchingNames(1,1).Length == 1 && reviewed.MatchingNames(5,1).Length == 1 && reviewed.MatchingNames(3,1).Length == 0, "reviewed multipolygon and gap");
        Check(ReviewedZoneLoader.Load(docBytes,digest(sourceBytes),sourceBytes,chartBytes,expectedLocations)==null, "untrusted reviewed digest rejected");
        Check(ReviewedZoneLoader.Load(docBytes,digest(docBytes),chartBytes,chartBytes,expectedLocations)==null && ReviewedZoneLoader.Load(docBytes,digest(docBytes),sourceBytes,sourceBytes,expectedLocations)==null, "changed map and chart rejected");
        Check(ReviewedZoneLoader.Load(docBytes,digest(docBytes),sourceBytes,chartBytes,new System.Collections.Generic.Dictionary<long,string> { [1]="Holman Isle", [2]="Missing" })==null, "missing location rejected");
        Check(ReviewedZoneLoader.Load(docBytes,digest(docBytes),sourceBytes,chartBytes,new System.Collections.Generic.Dictionary<long,string> { [1]="Other" })==null, "map name mismatch rejected");
        var draftBytes=System.Text.Encoding.UTF8.GetBytes(document.Replace("VERIFIED","DRAFT_NOT_FOR_AUTOMATIC_APPLICATION"));
        Check(ReviewedZoneLoader.Load(draftBytes,digest(draftBytes),sourceBytes,chartBytes,expectedLocations)==null, "draft never loads even with matching document hash");
        var duplicateBytes=System.Text.Encoding.UTF8.GetBytes(document.Replace("\"status\":\"VERIFIED\"", "\"status\":\"VERIFIED\",\"status\":\"VERIFIED\""));
        Check(ReviewedZoneLoader.Load(duplicateBytes,digest(duplicateBytes),sourceBytes,chartBytes,expectedLocations)==null, "duplicate json property rejected");
        var realDraft = File.ReadAllBytes("planning/pei-decay-zones.draft.json");
        Check(ReviewedZoneLoader.Load(realDraft,digest(realDraft),File.ReadAllBytes("runtime/server/Maps/PEI/Level.hierarchy"),File.ReadAllBytes("runtime/server/Maps/PEI/Chart.png"),expectedLocations) == null, "actual PEI draft rejected with matching trust hash");
        var peiSource=File.ReadAllBytes("runtime/server/Maps/PEI/Level.hierarchy"); var peiChart=File.ReadAllBytes("runtime/server/Maps/PEI/Chart.png");
        var manifestNames=new[]{"Summerside Military Base","Belfast Airport","Stratford","Alberton","Wiltshire Farm","Fernwood Farm","Wellington Farm","Charlottetown","Montague","Kensington Campground","Confederation Bridge","O'Leary Prison","Souris Campground","Cape Rock","Holman Isle","Oulton's Isle","Courtin Isle","Liberation Bridge","Taylor Beach","Pirate Cove","Tignish Farm"};
        var syntheticFull = Newtonsoft.Json.Linq.JObject.Parse(document); var template=(Newtonsoft.Json.Linq.JObject)syntheticFull["zones"][0]; var allZones=new Newtonsoft.Json.Linq.JArray();
        for (int j=0;j<manifestNames.Length;j++) { var z=(Newtonsoft.Json.Linq.JObject)template.DeepClone(); z["instance_id"]=j+9; z["name"]=manifestNames[j]; allZones.Add(z); }
        syntheticFull["zones"]=allZones; syntheticFull["source_sha256"]=digest(peiSource); syntheticFull["chart_sha256"]=digest(peiChart);
        Func<byte[]> encodeFull = () => System.Text.Encoding.UTF8.GetBytes(syntheticFull.ToString(Newtonsoft.Json.Formatting.None));
        var fullBytes=encodeFull(); Check(ReviewedZoneLoader.LoadPei(fullBytes,digest(fullBytes),peiSource,peiChart)!=null, "PEI fixed complete manifest");
        allZones.RemoveAt(0); var missingBytes=encodeFull(); Check(ReviewedZoneLoader.LoadPei(missingBytes,digest(missingBytes),peiSource,peiChart)==null, "PEI missing location rejected");
        allZones.Insert(0,template.DeepClone()); var extraBytes=encodeFull(); Check(ReviewedZoneLoader.LoadPei(extraBytes,digest(extraBytes),peiSource,peiChart)==null, "PEI foreign location rejected");
        allZones[0]["instance_id"]=9; allZones[0]["name"]="Wrong"; var wrongBytes=encodeFull(); Check(ReviewedZoneLoader.LoadPei(wrongBytes,digest(wrongBytes),peiSource,peiChart)==null, "PEI renamed location rejected");
        syntheticFull["map"]="Other"; var otherMap=encodeFull(); Check(ReviewedZoneLoader.LoadPei(otherMap,digest(otherMap),peiSource,peiChart)==null, "wrong map rejected");
        byte[] worldCheckpoint={1,2,3}; byte[] pluginCheckpoint={4,5,6};
        using (var sha=System.Security.Cryptography.SHA256.Create()) {
            var anchor = new CheckpointAnchor { Generation=7, WorldSha256=sha.ComputeHash(worldCheckpoint), StateSha256=sha.ComputeHash(pluginCheckpoint) };
            Check(CheckpointVerifier.Matches(anchor,7,worldCheckpoint,pluginCheckpoint,false), "matching anchored checkpoint");
            Check(!CheckpointVerifier.Matches(anchor,6,worldCheckpoint,pluginCheckpoint,false), "stale generation rejected");
            Check(!CheckpointVerifier.Matches(anchor,7,worldCheckpoint,pluginCheckpoint,true), "pending transaction rejected");
            Check(!CheckpointVerifier.Matches(anchor,7,new byte[]{1,2,4},pluginCheckpoint,false) && !CheckpointVerifier.Matches(anchor,7,worldCheckpoint,new byte[]{4,5,7},false), "mismatched snapshots rejected");
            Check(!CheckpointVerifier.Matches(null,7,worldCheckpoint,pluginCheckpoint,false) && !CheckpointVerifier.Matches(anchor,7,null,pluginCheckpoint,false), "missing evidence rejected");
        }
        var checkpointBackend = new CheckpointFake(); var coordinator = new CheckpointCoordinator(checkpointBackend);
        Check(coordinator.Start(0) && coordinator.Execute(new byte[]{1}) && coordinator.Ready && checkpointBackend.Generation==1, "checkpoint ordered success");
        for (int failurePoint=2; failurePoint<=7; failurePoint++) {
            var b = new CheckpointFake { FailAt=failurePoint }; var c = new CheckpointCoordinator(b); Check(c.Start(0), "checkpoint start");
            Check(!c.Execute(new byte[]{1}) && c.Faulted && !c.Ready && !c.Execute(new byte[]{1}), "checkpoint failure fences and prevents replay " + failurePoint);
        }
        var startReentryBackend = new CheckpointFake { ReenterAt=1, ReenterStart=true }; var startReentry = new CheckpointCoordinator(startReentryBackend); startReentryBackend.Owner=startReentry;
        Check(!startReentry.Start(0) && startReentry.Faulted && startReentryBackend.Calls==1, "checkpoint start reentry fences");
        foreach (int reentry in new[]{3,5,6}) {
            var backendReentry = new CheckpointFake { ReenterAt=reentry }; var reentrant = new CheckpointCoordinator(backendReentry); backendReentry.Owner=reentrant;
            Check(reentrant.Start(0) && !reentrant.Execute(new byte[]{1}) && reentrant.Faulted && backendReentry.Calls==reentry, "checkpoint reentry fences at " + reentry);
        }
        var throwingBackend = new CheckpointFake { FailAt=4, Throw=true }; var throwingCoordinator = new CheckpointCoordinator(throwingBackend);
        Check(throwingCoordinator.Start(0) && !throwingCoordinator.Execute(new byte[]{1}) && throwingCoordinator.Faulted, "checkpoint exception fences");
        string worldDir=Path.Combine(Path.GetTempPath(),"mre-world-fingerprint-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(worldDir);
        try {
            File.WriteAllBytes(Path.Combine(worldDir,"Barricades.dat"),new byte[]{1,2});
            Check(IsolatedWorldFingerprint.Compute(worldDir)==null, "missing structure save rejects fingerprint");
            File.WriteAllBytes(Path.Combine(worldDir,"Structures.dat"),new byte[]{3,4}); var fingerprint=IsolatedWorldFingerprint.Compute(worldDir);
            Check(fingerprint!=null && fingerprint.Length==32 && digest(fingerprint)==digest(IsolatedWorldFingerprint.Compute(worldDir)), "stable isolated world fingerprint");
            File.WriteAllBytes(Path.Combine(worldDir,"Structures.dat"),new byte[]{3,5}); Check(digest(fingerprint)!=digest(IsolatedWorldFingerprint.Compute(worldDir)), "world change changes fingerprint");
            Check(IsolatedWorldFingerprint.Compute(Environment.CurrentDirectory)==null, "non TEMP world rejected");
        } finally { Directory.Delete(worldDir,true); }
        var probeTimer = new ProbeTimer(); Check(probeTimer.Start(10), "probe starts");
        Check(!probeTimer.Poll(309.9) && probeTimer.Poll(310), "probe waits 300 uptime seconds");
        Check(probeTimer.Poll(2000) && !probeTimer.Poll(2000), "probe never catches up");
        Check(!probeTimer.Poll(1999) && !probeTimer.Poll(2300), "probe clock reversal fences");
        var ledger = new DamageScopeLedger(); var target = new object();
        var hit = ledger.Begin(target, 10, true); Check(!ledger.RepairAllowed, "pending damage blocks repair");
        Check(ledger.End(hit,target,9,true,true) == DamageScopeLedger.Result.PlayerDamage && ledger.RepairAllowed, "confirmed damage");
        var cancelled = ledger.Begin(target,9,true); Check(ledger.End(cancelled,target,9,true,true) == DamageScopeLedger.Result.NoPlayerDamage, "cancelled damage");
        var system = ledger.Begin(target,9,false); Check(ledger.End(system,target,8,true,true) == DamageScopeLedger.Result.NoPlayerDamage, "known system damage");
        var outer = ledger.Begin(target,8,true); var inner = ledger.Begin(target,8,true);
        Check(ledger.End(inner,target,7,true,true) == DamageScopeLedger.Result.Unknown && !ledger.RepairAllowed, "nested same target fences attribution");
        Check(ledger.End(outer,target,7,true,true) == DamageScopeLedger.Result.Unknown, "fault remains closed");
        var distinct = new DamageScopeLedger(); var other = new object(); var firstScope = distinct.Begin(target,8,true); var second = distinct.Begin(other,5,true);
        Check(distinct.End(second,other,4,true,true) == DamageScopeLedger.Result.PlayerDamage && !distinct.RepairAllowed, "nested other target is independent");
        Check(distinct.End(firstScope,target,8,true,true) == DamageScopeLedger.Result.NoPlayerDamage && distinct.RepairAllowed, "outer scope completes");
        var ledgerFailure = new DamageScopeLedger(); var failed = ledgerFailure.Begin(target,8,true);
        Check(ledgerFailure.End(failed,target,7,false,true) == DamageScopeLedger.Result.Unknown && !ledgerFailure.RepairAllowed, "exception fences");
        var destroyed = new DamageScopeLedger(); var lethal = destroyed.Begin(target,8,true);
        Check(destroyed.End(lethal,target,0,true,false) == DamageScopeLedger.Result.Unknown, "destruction unknown");
        var unknownPlayer = new DamageScopeLedger(); Check(unknownPlayer.Begin(target,8,null) == null && !unknownPlayer.RepairAllowed, "unknown player fences");
        var ordering = new DamageScopeLedger(); var one = ordering.Begin(target,8,true); ordering.Begin(other,4,true);
        Check(ordering.End(one,target,7,true,true) == DamageScopeLedger.Result.Unknown && !ordering.RepairAllowed, "invalid scope ordering fences");
        Guid safeId=Guid.NewGuid(), genId=Guid.NewGuid(); var safeGuids=new System.Collections.Generic.HashSet<Guid>{safeId};
        var observedSafe=new ObjectObservation { Kind="barricade", BuildType="STORAGE", InstanceId=11,AssetId=1,AssetGuid=safeId,Owner=1,Health=10,MaxHealth=10,IsStorage=true };
        var observedGen=new ObjectObservation { Kind="barricade", BuildType="GENERATOR", InstanceId=12,AssetId=2,AssetGuid=genId,Owner=2,Health=10,MaxHealth=10,IsGenerator=true,GeneratorPowered=true };
        var collected=GroundDeviceCollector.Collect(new[]{observedSafe,observedGen},safeGuids);
        Check(collected!=null && collected.Buildings.Count==2 && collected.Safes.Count==1 && collected.Generators.Count==1 && collected.Generators[0].Powered==true, "ground device collection");
        observedSafe.Owner=3; Check(collected.Safes[0].Owner==1 && collected.Buildings[0].Owner==1, "collector copies observations"); observedSafe.Owner=1;
        Check(GroundDeviceCollector.Collect(new ObjectObservation[0],safeGuids)!=null && GroundDeviceCollector.Collect(new[]{observedSafe},new System.Collections.Generic.HashSet<Guid>())==null, "empty ground versus missing whitelist");
        observedGen.GeneratorPowered=null; Check(GroundDeviceCollector.Collect(new[]{observedGen},safeGuids)==null, "generator unknown rejected"); observedGen.GeneratorPowered=true;
        observedSafe.IsStorage=false; Check(GroundDeviceCollector.Collect(new[]{observedSafe},safeGuids)==null, "safe wrong type rejected"); observedSafe.IsStorage=true;
        observedSafe.InstanceId=0; Check(GroundDeviceCollector.Collect(new[]{observedSafe},safeGuids)==null, "invalid object rejected"); observedSafe.InstanceId=11;
        Check(GroundDeviceCollector.Collect(new[]{observedSafe},new System.Collections.Generic.HashSet<Guid>{Guid.NewGuid()}).Safes.Count==0, "unlisted storage is not safe");
        Check(PlacementPolicy.Included("structure",null) && PlacementPolicy.Included("barricade","DOOR") && PlacementPolicy.Included("barricade","GENERATOR"), "approved placement types included");
        Check(!PlacementPolicy.Included("barricade","FARM") && !PlacementPolicy.Included("barricade","SPIKE") && !PlacementPolicy.Included("barricade","WIRE") && !PlacementPolicy.Included("barricade","NEW_TYPE"), "farm traps and unknown excluded");
        var crop=new ObjectObservation { Kind="barricade",BuildType="FARM",InstanceId=20,AssetId=3,AssetGuid=Guid.NewGuid(),Owner=1,Health=10,MaxHealth=10 };
        var trap=new ObjectObservation { Kind="barricade",BuildType="SPIKE",InstanceId=21,AssetId=4,AssetGuid=Guid.NewGuid(),Owner=1,Health=10,MaxHealth=10 };
        var realStructure=new ObjectObservation { Kind="structure",InstanceId=22,AssetId=5,AssetGuid=Guid.NewGuid(),Owner=1,Health=10,MaxHealth=10 };
        var mixed=GroundDeviceCollector.Collect(new[]{observedSafe,observedGen,crop,trap,realStructure},safeGuids);
        Check(mixed!=null && mixed.Buildings.Count==3 && mixed.Safes.Count==1 && mixed.Generators.Count==1 && mixed.Buildings.Exists(x=>x.Kind=="structure") && !mixed.Buildings.Exists(x=>x.BuildType=="FARM" || x.BuildType=="SPIKE"), "mixed collection excludes farm and trap");
        var sentry=new ObjectObservation { Kind="barricade", BuildType="SENTRY", InstanceId=23,AssetId=6,AssetGuid=Guid.NewGuid(),Owner=1,Health=10,MaxHealth=10,IsStorage=true };
        Check(ObservationAudit.Valid(new[]{sentry}) && GroundDeviceCollector.Collect(new[]{sentry},safeGuids).Buildings.Count==0, "specialized storage sentry observed but excluded");
        var vehicleObject=new ObjectObservation { Kind="barricade", BuildType="DOOR", InstanceId=24,VehicleInstanceId=99,VehicleSubvehicleIndex=0,IsVehicleAttached=true,AssetId=7,AssetGuid=Guid.NewGuid(),Owner=1,Health=10,MaxHealth=10 };
        Check(ObservationAudit.Valid(new[]{vehicleObject}) && GroundDeviceCollector.Collect(new[]{vehicleObject},safeGuids)==null, "vehicle object is observable but mutation collector fails closed");
        vehicleObject.Kind="structure"; Check(!ObservationAudit.Valid(new[]{vehicleObject}), "vehicle structure provenance rejected"); vehicleObject.Kind="barricade";
        vehicleObject.IsVehicleAttached=false; Check(!ObservationAudit.Valid(new[]{vehicleObject}), "orphan vehicle provenance rejected"); vehicleObject.IsVehicleAttached=true;
        vehicleObject.IsVehicleAttached=false; vehicleObject.VehicleInstanceId=0; vehicleObject.VehicleSubvehicleIndex=1;
        Check(!ObservationAudit.Valid(new[]{vehicleObject}), "ground object with subregion rejected"); vehicleObject.VehicleSubvehicleIndex=0;
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
        // Synthetic disjoint contours model Holman without accepting draft map geometry.
        var islandA = new PolygonZone("holman-a", new[]{new ZonePoint(0,0),new ZonePoint(2,0),new ZonePoint(2,2),new ZonePoint(0,2)});
        var islandB = new PolygonZone("holman-b", new[]{new ZonePoint(4,0),new ZonePoint(6,0),new ZonePoint(6,2),new ZonePoint(4,2)});
        var multi = new ZoneSet(new[]{new NamedZone("Holman", new[]{islandA,islandB})});
        Check(multi.MatchingNames(1,1).Length == 1 && multi.MatchingNames(1,1)[0] == "Holman", "first Holman contour");
        Check(multi.MatchingNames(5,1).Length == 1 && multi.MatchingNames(5,1)[0] == "Holman", "second Holman contour");
        Check(multi.MatchingNames(3,1).Length == 0, "gap is outside Holman");
        var city = new PolygonZone("city",new[]{new ZonePoint(-1,-1),new ZonePoint(1,-1),new ZonePoint(1,1),new ZonePoint(-1,1)});
        var s = new MreDecay.Engine.Snapshot { Confirmed=true, BuildingsComplete=true, ZonesConfirmed=true, SafesComplete=true, GeneratorsComplete=true, Buildings=new[]{building}, Safes=new[]{safe}, Generators=new[]{gen}, Zones=new ZoneSet(new[]{city}) };
        var queryEvidence = ReadyRuntimeEvidence();
        var queryGate = new RuntimeGate(); var query = new RuntimeZoneQuery(queryGate,queryEvidence,s.Zones); bool accelerated;
        Check(!query.TryGetAcceleratedZone(0,0,out accelerated), "hint hidden before start");
        Check(queryGate.Start(queryEvidence,0,false) && query.TryGetAcceleratedZone(0,0,out accelerated) && accelerated, "confirmed zone hint");
        Check(query.TryGetAcceleratedZone(2,0,out accelerated) && !accelerated, "outside confirmed zone");
        queryEvidence.ZonesVerified=false; Check(!query.TryGetAcceleratedZone(0,0,out accelerated), "lost evidence hides hint");
        queryEvidence.ZonesVerified=true; Check(!query.TryGetAcceleratedZone(0,0,out accelerated), "lost evidence permanently fences query");
        query = new RuntimeZoneQuery(queryGate,queryEvidence,s.Zones);
        Check(queryGate.Poll(queryEvidence,300) && !query.TryGetAcceleratedZone(0,0,out accelerated), "pending step hides hint");
        Check(queryGate.Complete(true) && query.TryGetAcceleratedZone(0,0,out accelerated), "hint after committed step");
        Check(!query.TryGetAcceleratedZone(double.NaN,0,out accelerated) && !query.TryGetAcceleratedZone(0,0,out accelerated), "invalid position fences query");
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
        var evidence = ReadyRuntimeEvidence();
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
        string pendingDir=Path.Combine(Path.GetTempPath(),"mre-pending-crash-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(pendingDir);
        try {
            string path=Path.Combine(pendingDir,"state.dat");
            using (var bootstrap=new DurableJournal(path)) Check(bootstrap.Bootstrap(encoded), "pending test explicit bootstrap");
            using (var pendingStore=new IsolatedPendingStore(path)) Check(pendingStore.Open(0,encoded) && pendingStore.Begin(encoded), "pending intent durable before mutation");
            using (var crashRestart=new IsolatedPendingStore(path)) Check(!crashRestart.Open(0,encoded), "pending after interruption refuses restart");
            using (var wrongGeneration=new IsolatedPendingStore(path)) Check(!wrongGeneration.Open(1,encoded), "pending cannot be accepted as new generation");
        } finally { Directory.Delete(pendingDir,true); }
        string pendingConcurrentDir=Path.Combine(Path.GetTempPath(),"mre-pending-concurrent-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(pendingConcurrentDir);
        try {
            string path=Path.Combine(pendingConcurrentDir,"state.dat");
            using (var bootstrap=new DurableJournal(path)) Check(bootstrap.Bootstrap(encoded), "pending concurrency bootstrap");
            var store=new IsolatedPendingStore(path);
            var open1=Task.Run(() => store.Open(0,encoded)); var open2=Task.Run(() => store.Open(0,encoded)); Task.WaitAll(open1,open2);
            Check(open1.Result != open2.Result && !store.Begin(encoded), "concurrent open conflict fences store"); store.Dispose();
        } finally { Directory.Delete(pendingConcurrentDir,true); }
        string pendingRaceDir=Path.Combine(Path.GetTempPath(),"mre-pending-dispose-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(pendingRaceDir);
        try {
            string path=Path.Combine(pendingRaceDir,"state.dat"); using (var bootstrap=new DurableJournal(path)) Check(bootstrap.Bootstrap(encoded), "pending dispose bootstrap");
            var store=new IsolatedPendingStore(path); Check(store.Open(0,encoded), "pending dispose open");
            var begin=Task.Run(() => store.Begin(encoded)); var dispose=Task.Run(() => store.Dispose()); Task.WaitAll(begin,dispose);
            Check(!store.Begin(encoded), "begin and dispose serialize, disposed store fences");
            using (var retry=new IsolatedPendingStore(path)) Check(begin.Result ? !retry.Open(0,encoded) : retry.Open(0,encoded), "restart reflects completed intent");
        } finally { Directory.Delete(pendingRaceDir,true); }
        for (int failPoint=0; failPoint<3; failPoint++) {
            string root=Path.Combine(Path.GetTempPath(),"mre-checkpoint-rehearsal-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string world=Path.Combine(root,"world"); Directory.CreateDirectory(world);
            string journalDir=Path.Combine(root,"journal"); Directory.CreateDirectory(journalDir); string journalPath=Path.Combine(journalDir,"state.dat");
            try {
                File.WriteAllBytes(Path.Combine(world,"Barricades.dat"),new byte[]{1}); File.WriteAllBytes(Path.Combine(world,"Structures.dat"),new byte[]{2});
                var fingerprint=IsolatedWorldFingerprint.Compute(world); using (var bootstrap=new DurableJournal(journalPath)) Check(bootstrap.Bootstrap(encoded), "rehearsal bootstrap");
                bool applied=false, saved=false;
                using (var backend=new IsolatedCheckpointBackend(world,journalPath,0,encoded,fingerprint,
                    () => { applied=true; if (failPoint==0) return false; File.WriteAllBytes(Path.Combine(world,"Structures.dat"),new byte[]{3}); return true; },
                    () => { saved=true; return failPoint!=1; })) {
                    var transaction=new CheckpointCoordinator(backend); Check(transaction.Start(0) && !transaction.Execute(encoded) && transaction.Faulted, "rehearsal refuses incomplete commit " + failPoint);
                }
                Check(applied && (failPoint==0 || saved), "expected stage reached " + failPoint);
                using (var rehearsalRestart=new IsolatedPendingStore(journalPath)) Check(!rehearsalRestart.Open(0,encoded), "pending after interrupted rehearsal " + failPoint);
            } finally { Directory.Delete(root,true); }
        }
        string misuseDir=Path.Combine(Path.GetTempPath(),"mre-checkpoint-misuse-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(misuseDir);
        try {
            string world=Path.Combine(misuseDir,"world"); Directory.CreateDirectory(world); string journalDir=Path.Combine(misuseDir,"journal"); Directory.CreateDirectory(journalDir);
            File.WriteAllBytes(Path.Combine(world,"Barricades.dat"),new byte[]{1}); File.WriteAllBytes(Path.Combine(world,"Structures.dat"),new byte[]{2});
            string path=Path.Combine(journalDir,"state.dat"); using (var bootstrap=new DurableJournal(path)) Check(bootstrap.Bootstrap(encoded), "misuse bootstrap");
            int applies=0; var backend=new IsolatedCheckpointBackend(world,path,0,encoded,IsolatedWorldFingerprint.Compute(world),() => { applies++; return true; },() => true);
            Check(backend.VerifyCurrent(0) && backend.WriteIntent(1,encoded) && backend.ApplyWorld() && !backend.ApplyWorld() && applies==1 && !backend.SaveAndVerifyWorld(), "backend duplicate apply fences");
            backend.Dispose(); Check(!backend.VerifyCurrent(0) && !backend.ApplyWorld(), "disposed backend refuses operations");
        } finally { Directory.Delete(misuseDir,true); }
        string reentrantDir=Path.Combine(Path.GetTempPath(),"mre-checkpoint-reentrant-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(reentrantDir);
        try {
            string world=Path.Combine(reentrantDir,"world"); Directory.CreateDirectory(world); string journalDir=Path.Combine(reentrantDir,"journal"); Directory.CreateDirectory(journalDir);
            File.WriteAllBytes(Path.Combine(world,"Barricades.dat"),new byte[]{1}); File.WriteAllBytes(Path.Combine(world,"Structures.dat"),new byte[]{2});
            string path=Path.Combine(journalDir,"state.dat"); using (var bootstrap=new DurableJournal(path)) Check(bootstrap.Bootstrap(encoded), "reentrant backend bootstrap");
            IsolatedCheckpointBackend reentrantBackend=null; int saves=0;
            reentrantBackend=new IsolatedCheckpointBackend(world,path,0,encoded,IsolatedWorldFingerprint.Compute(world),() => { reentrantBackend.SaveAndVerifyWorld(); return true; },() => { saves++; return true; });
            Check(reentrantBackend.VerifyCurrent(0) && reentrantBackend.WriteIntent(1,encoded) && !reentrantBackend.ApplyWorld() && saves==0 && !reentrantBackend.SaveAndVerifyWorld(), "nested save fences backend before save");
            reentrantBackend.Dispose(); using (var afterReentrant=new IsolatedPendingStore(path)) Check(!afterReentrant.Open(0,encoded), "reentrant failure leaves pending");
        } finally { Directory.Delete(reentrantDir,true); }
        string nestedSaveDir=Path.Combine(Path.GetTempPath(),"mre-checkpoint-nested-save-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(nestedSaveDir);
        try {
            string world=Path.Combine(nestedSaveDir,"world"); Directory.CreateDirectory(world); string journalDir=Path.Combine(nestedSaveDir,"journal"); Directory.CreateDirectory(journalDir);
            File.WriteAllBytes(Path.Combine(world,"Barricades.dat"),new byte[]{1}); File.WriteAllBytes(Path.Combine(world,"Structures.dat"),new byte[]{2});
            string path=Path.Combine(journalDir,"state.dat"); using (var bootstrap=new DurableJournal(path)) Check(bootstrap.Bootstrap(encoded), "nested save bootstrap");
            IsolatedCheckpointBackend backend=null; int applies=0;
            backend=new IsolatedCheckpointBackend(world,path,0,encoded,IsolatedWorldFingerprint.Compute(world),() => { applies++; return true; },() => { backend.ApplyWorld(); return true; });
            Check(backend.VerifyCurrent(0) && backend.WriteIntent(1,encoded) && backend.ApplyWorld() && !backend.SaveAndVerifyWorld() && applies==1, "nested apply in save fences outer save");
            backend.Dispose(); using (var afterNestedSave=new IsolatedPendingStore(path)) Check(!afterNestedSave.Open(0,encoded), "nested save failure leaves pending");
        } finally { Directory.Delete(nestedSaveDir,true); }
        foreach (string crashPoint in new[]{"intent","apply","save"}) {
            string root=Path.Combine(Path.GetTempPath(),"mre-checkpoint-process-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string world=Path.Combine(root,"world"); Directory.CreateDirectory(world); string journalDir=Path.Combine(root,"journal"); Directory.CreateDirectory(journalDir); string path=Path.Combine(journalDir,"state.dat");
            try {
                File.WriteAllBytes(Path.Combine(world,"Barricades.dat"),new byte[]{1}); File.WriteAllBytes(Path.Combine(world,"Structures.dat"),new byte[]{2}); File.WriteAllBytes(Path.Combine(root,"state.bin"),encoded);
                using (var bootstrap=new DurableJournal(path)) Check(bootstrap.Bootstrap(encoded), "crash process bootstrap " + crashPoint);
                var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--crash-rehearsal \"" + root + "\" " + crashPoint) { UseShellExecute=false });
                Check(child != null, "child started " + crashPoint);
                try {
                    var deadline=DateTime.UtcNow.AddSeconds(30); string marker=Path.Combine(root,"marker");
                    string observed=null;
                    while (!child.HasExited && DateTime.UtcNow < deadline) { try { if (File.Exists(marker)) observed=File.ReadAllText(marker); } catch (IOException) { } if (observed==crashPoint) break; System.Threading.Thread.Sleep(20); }
                    Check(observed==crashPoint && !child.HasExited, "child reached intended crash point " + crashPoint);
                    child.Kill(); Check(child.WaitForExit(30000) && child.ExitCode != 0, "parent killed child at " + crashPoint);
                } finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(30000); } child.Dispose(); }
                using (var retry=new IsolatedPendingStore(path)) Check(!retry.Open(0,encoded), "restart rejects pending after process kill " + crashPoint);
                Check(File.ReadAllBytes(Path.Combine(world,"Structures.dat"))[0] == (crashPoint=="intent" ? 2 : 3), "expected world stage after kill " + crashPoint);
            } finally { Directory.Delete(root,true); }
        }
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
