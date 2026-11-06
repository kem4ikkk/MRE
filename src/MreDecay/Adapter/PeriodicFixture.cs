// Test-only periodic integration. Never deploy to the working server.
using System;
using System.Diagnostics;
using System.IO;
using MreDecay;
using MreDecay.Adapter;
using MreDecay.Engine;
using MreDecay.Zones;
using MreDecay.IsolatedRuntime;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreDecay.IsolatedFixture
{
    public sealed class PeriodicFixture : RocketPlugin
    {
        private bool armed, started;
        private ProbeTimer timer;
        private StructureDrop floor;
        private BarricadeDrop safeDrop, generatorDrop;
        private Guid safeGuid, generatorGuid, floorGuid, farmGuid, spikeGuid;
        private uint safeInstance, generatorInstance, farmInstance, spikeInstance;
        private ushort maxHealth, safeMaxHealth, generatorMaxHealth, farmMaxHealth, spikeMaxHealth, initialDamage;
        protected override void Unload() { armed=false; timer=null; }
        protected override void Load() {
            try {
                string root=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                string exe=Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                armed=exe.StartsWith(root,StringComparison.OrdinalIgnoreCase) && Provider.serverID=="MREDecayPeriodicRun962e3e9f" && Environment.GetEnvironmentVariable("MRE_DECAY_PERIODIC_FIXTURE")=="YES";
            } catch { armed=false; }
        }
        private void Update() {
            if(!armed || !Provider.isServer || !Level.isLoaded) return;
            try {
                if(Provider.clients==null || Provider.clients.Count!=0) { Stop("players"); return; }
                if(!started) { StartFixture(); return; }
                if(!timer.Poll(Time.realtimeSinceStartupAsDouble)) { if(timer.Faulted) Stop("clock"); return; }
                Cycle();
            } catch(Exception e) { Logger.LogWarning("[MRE periodic fixture] FAILED " + e.GetType().Name); Stop("exception"); }
        }
        private void StartFixture() {
            ObjectObservation[] before;
            if(!NoVehicleBarricades() || !ReadOnlyWorld.TryObserve(true,out before) || before.Length!=0) { Stop("world not empty"); return; }
            if (NoVehicleBarricades(new int?[]{0,1}) || !NoVehicleBarricades(new int?[]{0,0}) || NoVehicleBarricades(new int?[]{null})) { Stop("vehicle guard controls"); return; }
            Logger.Log("[MRE periodic fixture] vehicle guard: actual regions empty; synthetic nonempty/unknown refused");
            var asset=Assets.find(EAssetType.ITEM,51) as ItemStructureAsset;
            var safe=Assets.find(new Guid("bf233036b60d403c836deb8ac0f03292")) as ItemStorageAsset;
            ItemGeneratorAsset gen=null; ItemBarricadeAsset farm=null, spike=null;
            foreach(var item in Assets.find(EAssetType.ITEM)) {
                var candidate=item as ItemBarricadeAsset;
                if(gen==null && item is ItemGeneratorAsset) gen=(ItemGeneratorAsset)item;
                if(candidate!=null && candidate.build==EBuild.FARM && farm==null) farm=candidate;
                if(candidate!=null && candidate.build==EBuild.SPIKE && spike==null) spike=candidate;
            }
            if(asset==null || safe==null || gen==null || farm==null || spike==null || asset.health<10) { Stop("assets"); return; }
            if(!StructureManager.dropStructure(new Structure(asset,asset.health),new Vector3(0,300,0),0,0,0,1,99)) { Stop("floor spawn"); return; }
            var s=BarricadeManager.dropBarricade(new Barricade(safe),null,new Vector3(3,300,0),0,0,0,2,99);
            var g=BarricadeManager.dropBarricade(new Barricade(gen),null,new Vector3(6,300,0),0,0,0,3,99);
            if(s==null || g==null) { Stop("device spawn"); return; }
            safeDrop=BarricadeManager.FindBarricadeByRootTransform(s);
            generatorDrop=BarricadeManager.FindBarricadeByRootTransform(g);
            if(safeDrop==null || generatorDrop==null || safeDrop.asset.GUID!=safe.GUID || generatorDrop.asset.GUID!=gen.GUID) { Stop("device identity"); return; }
            safeInstance=safeDrop.instanceID; generatorInstance=generatorDrop.instanceID;
            safeMaxHealth=safeDrop.asset.health; generatorMaxHealth=generatorDrop.asset.health;
            if(BarricadeManager.dropBarricade(new Barricade(farm),null,new Vector3(9,300,0),0,0,0,4,99)==null ||
               BarricadeManager.dropBarricade(new Barricade(spike),null,new Vector3(12,300,0),0,0,0,5,99)==null) { Stop("negative spawn"); return; }
            farmGuid=farm.GUID; spikeGuid=spike.GUID;
            foreach(var region in BarricadeManager.regions) foreach(var drop in region.drops) {
                if(drop.asset.GUID==farmGuid) { farmInstance=drop.instanceID; farmMaxHealth=drop.asset.health; }
                if(drop.asset.GUID==spikeGuid) { spikeInstance=drop.instanceID; spikeMaxHealth=drop.asset.health; }
            }
            if(farmInstance==0 || spikeInstance==0 || farmMaxHealth==0 || spikeMaxHealth==0) { Stop("negative identity"); return; }
            safeGuid=safe.GUID; generatorGuid=gen.GUID; floorGuid=asset.GUID; maxHealth=asset.health; initialDamage=(ushort)(asset.health / 100 + 3);
            var component=generatorDrop.model.GetComponent<InteractableGenerator>();
            if(component==null) { Stop("generator"); return; }
            BarricadeManager.sendFuel(g,100);
            if(!BarricadeManager.ServerSetGeneratorPowered(component,true) || !component.isPowered || component.fuel==0) { Stop("power"); return; }
            if (safeMaxHealth <= 100 || generatorMaxHealth <= 100) { Stop("device health too low"); return; }
            ushort safeDamage=(ushort)(safeMaxHealth/100+3), generatorDamage=(ushort)(generatorMaxHealth/100+3);
            if (!ProbeHealthMutation.ApplyBarricade(safeDrop,safeMaxHealth,(ushort)(safeMaxHealth-safeDamage)) ||
                !ProbeHealthMutation.ApplyBarricade(generatorDrop,generatorMaxHealth,(ushort)(generatorMaxHealth-generatorDamage))) { Stop("initial device damage"); return; }
            foreach(var region in StructureManager.regions) foreach(var d in region.drops) if(d.asset.GUID==asset.GUID) floor=d;
            if(floor==null || !ProbeHealthMutation.ApplyStructure(floor,asset.health,(ushort)(asset.health-initialDamage))) { Stop("initial damage"); return; }
            timer=new ProbeTimer(); if(!timer.Start(Time.realtimeSinceStartupAsDouble)) { Stop("clock start"); return; }
            started=true; Logger.Log("[MRE periodic fixture] created floor, safe and powered generator; waiting 300 seconds; mock zone/raid/state");
        }
        private void Cycle() {
            ObjectObservation[] observed;
            if(!NoVehicleBarricades() || !ReadOnlyWorld.TryObserve(true,out observed) || observed.Length!=5 || ProbePlanning.Prepare(observed)!=null) { Stop("observation or fail-closed"); return; }
            ObjectObservation b=null,s=null,g=null;
            foreach(var o in observed) { if(o.Kind=="structure") b=o; else if(o.AssetGuid==safeGuid) s=o; else if(o.AssetGuid==generatorGuid) g=o; }
            if(b==null || s==null || g==null || b.Owner!=1 || s.Owner!=2 || g.Owner!=3 || b.Group!=99 || s.Group!=99 || g.Group!=99 || g.GeneratorPowered!=true || floor.instanceID!=b.InstanceId || b.AssetGuid!=floorGuid || b.MaxHealth!=maxHealth || s.InstanceId!=safeInstance || s.MaxHealth!=safeMaxHealth || g.InstanceId!=generatorInstance || g.MaxHealth!=generatorMaxHealth) { Stop("identity"); return; }
            var devices=GroundDeviceCollector.Collect(observed,new System.Collections.Generic.HashSet<Guid>{safeGuid});
            if(devices==null || devices.Buildings.Count!=3 || devices.Safes.Count!=1 || devices.Generators.Count!=1) { Stop("device collection"); return; }
            bool farmSeen=false,spikeSeen=false;
            foreach(var o in observed) {
                if(o.AssetGuid==farmGuid && o.BuildType=="FARM" && o.InstanceId==farmInstance && o.Health==farmMaxHealth) farmSeen=true;
                if(o.AssetGuid==spikeGuid && o.BuildType=="SPIKE" && o.InstanceId==spikeInstance && o.Health==spikeMaxHealth) spikeSeen=true;
            }
            if(!farmSeen || !spikeSeen) { Stop("negative observation"); return; }
            foreach(var o in devices.Buildings) if(o.AssetGuid==farmGuid || o.AssetGuid==spikeGuid) { Stop("negative included"); return; }
            var zone=new ZoneSet(new[]{new PolygonZone("mock",new[]{new ZonePoint(-1,-1),new ZonePoint(1,-1),new ZonePoint(1,1),new ZonePoint(-1,1)})});
            var buildings=new System.Collections.Generic.List<Building>();
            foreach(var o in devices.Buildings) buildings.Add(new Building { Id=o.Kind+":"+o.InstanceId,Health=o.Health,MaxHealth=o.MaxHealth,PersistedMaxHealth=o.MaxHealth,Owner=o.Owner,Group=o.Group,IdentityKnown=true,Position=new Position3(o.X,o.Y,o.Z),RaidBlocked=false });
            var snapshot=new MreDecay.Engine.Snapshot { Confirmed=true,BuildingsComplete=true,ZonesConfirmed=true,SafesComplete=true,GeneratorsComplete=true,
                Buildings=buildings, Safes=devices.Safes, Generators=devices.Generators, Zones=zone };
            var plan=StepEngine.Prepare(snapshot); Change floorChange=null, safeChange=null, generatorChange=null;
            if(plan!=null) foreach(var change in plan.Results) {
                if(change.Id=="structure:"+b.InstanceId) floorChange=change;
                else if(change.Id=="barricade:"+s.InstanceId) safeChange=change;
                else if(change.Id=="barricade:"+g.InstanceId) generatorChange=change;
            }
            ushort expectedFloor=(ushort)(b.Health + b.MaxHealth/100);
            ushort expectedSafe=(ushort)(s.Health + s.MaxHealth/100);
            ushort expectedGenerator=(ushort)(g.Health + g.MaxHealth/100);
            if(plan==null || plan.Results.Count!=3 || floorChange==null || safeChange==null || generatorChange==null ||
                floorChange.Health!=expectedFloor || safeChange.Health!=expectedSafe || generatorChange.Health!=expectedGenerator ||
                !ProbeHealthMutation.ApplyStructure(floor,b.Health,floorChange.Health) ||
                !ProbeHealthMutation.ApplyBarricade(safeDrop,s.Health,safeChange.Health) ||
                !ProbeHealthMutation.ApplyBarricade(generatorDrop,g.Health,generatorChange.Health)) { Stop("plan or mutation"); return; }
            ObjectObservation[] after;
            if(!ReadOnlyWorld.TryObserve(true,out after) || after.Length!=observed.Length) { Stop("post mutation observation"); return; }
            bool floorOk=false,safeOk=false,generatorOk=false,farmOk=false,spikeOk=false;
            foreach(var item in after) {
                if(item.Kind=="structure" && item.InstanceId==b.InstanceId && item.AssetGuid==floorGuid && item.Health==floorChange.Health) floorOk=true;
                if(item.Kind=="barricade" && item.InstanceId==safeInstance && item.AssetGuid==safeGuid && item.Health==safeChange.Health) safeOk=true;
                if(item.Kind=="barricade" && item.InstanceId==generatorInstance && item.AssetGuid==generatorGuid && item.Health==generatorChange.Health) generatorOk=true;
                if(item.Kind=="barricade" && item.InstanceId==farmInstance && item.AssetGuid==farmGuid && item.BuildType=="FARM" && item.Health==farmMaxHealth && item.MaxHealth==farmMaxHealth) farmOk=true;
                if(item.Kind=="barricade" && item.InstanceId==spikeInstance && item.AssetGuid==spikeGuid && item.BuildType=="SPIKE" && item.Health==spikeMaxHealth && item.MaxHealth==spikeMaxHealth) spikeOk=true;
            }
            if(!floorOk || !safeOk || !generatorOk || !farmOk || !spikeOk) { Stop("post mutation health mismatch"); return; }
            Logger.Log("[MRE periodic fixture] PASS after 300 seconds: all 3 approved objects repaired; farm and spike observed and excluded; grouped safe and powered generator; floor HP " + b.Health + " -> " + floorChange.Health + "; safe HP " + s.Health + " -> " + safeChange.Health + "; generator HP " + g.Health + " -> " + generatorChange.Health + "; mock zone/raid/state; unverified probe refused");
            Stop("done");
        }
        private static bool NoVehicleBarricades() {
            if (BarricadeManager.vehicleRegions == null) return false;
            var counts=new System.Collections.Generic.List<int?>();
            foreach (var region in BarricadeManager.vehicleRegions)
                counts.Add(region == null || region.drops == null ? (int?)null : region.drops.Count);
            return NoVehicleBarricades(counts);
        }
        private static bool NoVehicleBarricades(System.Collections.Generic.IEnumerable<int?> counts) {
            if (counts == null) return false;
            foreach (var count in counts) if (!count.HasValue || count.Value != 0) return false;
            return true;
        }
        private void Stop(string reason) { armed=false; Logger.Log("[MRE periodic fixture] stop="+reason); Commander.execute(CSteamID.Nil,"shutdown"); }
    }
}
