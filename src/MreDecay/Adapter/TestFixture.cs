// Separate test-only assembly. Never install alongside production plugins.
using System;
using System.Diagnostics;
using System.IO;
using MreDecay.Adapter;
using MreDecayZoneHud;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreDecay.IsolatedFixture
{
    public sealed class TestFixture : RocketPlugin
    {
        private bool armed;
        private static int requests;
        private static bool nestedActive;
        private static void NestedStructureRequest(CSteamID attacker, Transform target, ref ushort amount, ref bool allow, EDamageOrigin origin)
        {
            if (nestedActive) return;
            nestedActive = true;
            try { StructureManager.damage(target, Vector3.zero, 1, 1, false, attacker); allow = false; }
            finally { nestedActive = false; }
        }
        private static void ObserveRequest(CSteamID attacker, Transform target, ref ushort amount, ref bool allow, EDamageOrigin origin)
        {
            if (attacker.m_SteamID == 76561198000000000UL && amount == 1 && allow) requests++;
        }
        private static void CancelRequest(CSteamID attacker, Transform target, ref ushort amount, ref bool allow, EDamageOrigin origin)
        { allow = false; }
        protected override void Load()
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string exe = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            armed = Provider.serverID == "MREDecayFixture" && exe.StartsWith(temp, StringComparison.OrdinalIgnoreCase) &&
                Environment.GetEnvironmentVariable("MRE_DECAY_FIXTURE") == "YES";
        }
        private void Update()
        {
            if (!armed || !Level.isLoaded) return;
            armed = false;
            try
            {
                ObjectObservation[] before;
                if (Provider.clients == null || Provider.clients.Count != 0 || !ReadOnlyWorld.TryObserve(true, out before) || before.Length != 0)
                { Logger.LogWarning("[MRE fixture] REFUSED nonempty or incomplete world"); return; }
                foreach (ushort id in new ushort[] { 1764, 51, 57, 378, 16, 328 })
                    Logger.Log("[MRE fixture] classifier id=" + id + " category=" + HeldItemClassifier.Classify(Assets.find(EAssetType.ITEM, id) as ItemAsset));
                var floor = Assets.find(EAssetType.ITEM, 51) as ItemStructureAsset;
                var safe = Assets.find(new Guid("bf233036b60d403c836deb8ac0f03292")) as ItemStorageAsset;
                ItemGeneratorAsset generator = null;
                foreach (var asset in Assets.find(EAssetType.ITEM)) if (asset is ItemGeneratorAsset) { generator = (ItemGeneratorAsset)asset; break; }
                if (floor == null || safe == null || generator == null) { Logger.LogWarning("[MRE fixture] REFUSED missing test assets"); return; }
                bool structure = StructureManager.dropStructure(new Structure(floor, floor.health), new Vector3(0, 300, 0), 0, 0, 0, 1, 99);
                bool storage = BarricadeManager.dropBarricade(new Barricade(safe), null, new Vector3(3, 300, 0), 0, 0, 0, 2, 99) != null;
                bool gen = BarricadeManager.dropBarricade(new Barricade(generator), null, new Vector3(6, 300, 0), 0, 0, 0, 3, 99) != null;
                ObjectObservation[] after;
                if (!structure || !storage || !gen || !ReadOnlyWorld.TryObserve(true, out after)) { Logger.LogWarning("[MRE fixture] FAILED spawn or observation"); return; }
                if (after.Length != 3) { Logger.LogWarning("[MRE fixture] FAILED unexpected observation count"); return; }
                foreach (var o in after)
                    if (o.Group != 99 || o.Health != o.MaxHealth || o.Y != 300 || o.Z != 0 ||
                        !((o.Kind == "structure" && o.Owner == 1 && o.AssetGuid == floor.GUID && o.X == 0) ||
                          (o.Kind == "barricade" && o.Owner == 2 && o.AssetGuid == safe.GUID && o.IsStorage && o.X == 3) ||
                          (o.Kind == "barricade" && o.Owner == 3 && o.AssetGuid == generator.GUID && o.IsGenerator && o.X == 6)))
                    { Logger.LogWarning("[MRE fixture] FAILED unexpected observation fields"); return; }
                int structures = 0, safes = 0, generators = 0;
                foreach (var o in after) { if (o.Kind == "structure") structures++; if (o.AssetGuid == safe.GUID && o.IsStorage) safes++; if (o.IsGenerator) generators++; }
                Logger.Log("[MRE fixture] structures=" + structures + " safes=" + safes + " generators=" + generators);
                if (MreDecay.IsolatedRuntime.ProbePlanning.Prepare(after) != null)
                { Logger.LogWarning("[MRE fixture] FAILED unverified runtime must refuse"); return; }
                var fixtureCity = new MreDecay.Zones.ZoneSet(new[]{new MreDecay.Zones.PolygonZone("fixture", new[]{
                    new MreDecay.Zones.ZonePoint(-1,-1), new MreDecay.Zones.ZonePoint(1,-1),
                    new MreDecay.Zones.ZonePoint(1,1), new MreDecay.Zones.ZonePoint(-1,1)})});
                ObjectObservation floorObservation = null;
                foreach (var o in after) if (o.Kind == "structure") floorObservation = o;
                if (floorObservation == null) { Logger.LogWarning("[MRE fixture] FAILED missing floor"); return; }
                var syntheticSnapshot = new MreDecay.Engine.Snapshot {
                    Confirmed=true, BuildingsComplete=true, ZonesConfirmed=true, SafesComplete=true, GeneratorsComplete=true,
                    Buildings=new[]{new MreDecay.Engine.Building { Id="fixture-floor", Health=floorObservation.Health,
                        MaxHealth=floorObservation.MaxHealth, PersistedMaxHealth=floorObservation.MaxHealth,
                        Owner=floorObservation.Owner, Group=floorObservation.Group, IdentityKnown=true,
                        Position=new MreDecay.Position3(floorObservation.X,floorObservation.Y,floorObservation.Z), RaidBlocked=false }},
                    Safes=new MreDecay.ProtectionObject[0], Generators=new MreDecay.ProtectionObject[0], Zones=fixtureCity
                };
                var fixturePlan=MreDecay.Engine.StepEngine.Prepare(syntheticSnapshot);
                if (fixturePlan == null || fixturePlan.Results.Count != 1 || fixturePlan.Results[0].Health >= floorObservation.Health || fixturePlan.Results[0].Health == 0)
                { Logger.LogWarning("[MRE fixture] FAILED synthetic decay plan"); return; }
                // Actual mutation is performed below only on this fixture-owned fresh floor.

                // This fixture owns the three freshly created objects in an empty isolated world.
                // Verify server-side effects only; clients and persistence are not exercised.
                StructureDrop testStructure = null;
                BarricadeDrop testStorage = null;
                foreach (var region in StructureManager.regions)
                    foreach (var drop in region.drops) if (drop.asset.GUID == floor.GUID) testStructure = drop;
                foreach (var region in BarricadeManager.regions)
                    foreach (var drop in region.drops) if (drop.asset.GUID == safe.GUID) testStorage = drop;
                if (testStructure == null || testStorage == null || floor.health < 3 || safe.health < 3)
                { Logger.LogWarning("[MRE fixture] FAILED mutation preconditions"); return; }
                if (!MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyStructure(testStructure, floorObservation.Health, fixturePlan.Results[0].Health))
                { Logger.LogWarning("[MRE fixture] FAILED plan mutation"); return; }
                ushort plannedHealth=fixturePlan.Results[0].Health;
                if (!MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyStructure(testStructure, plannedHealth, floorObservation.Health))
                { Logger.LogWarning("[MRE fixture] FAILED restore after plan"); return; }
                Logger.Log("[MRE fixture] real observation -> synthetic zone/state plan -> checked floor mutation and restore; unverified probe refused");
                BarricadeDrop testGenerator = null;
                foreach (var region in BarricadeManager.regions)
                    foreach (var drop in region.drops) if (drop.asset.GUID == generator.GUID) testGenerator = drop;
                var generatorComponent = testGenerator == null ? null : testGenerator.model.GetComponent<InteractableGenerator>();
                if (generatorComponent == null) { Logger.LogWarning("[MRE fixture] FAILED generator component"); return; }
                BarricadeManager.sendFuel(testGenerator.model, 100);
                if (!BarricadeManager.ServerSetGeneratorPowered(generatorComponent, true) || !generatorComponent.isPowered || generatorComponent.fuel == 0 || !ReadOnlyWorld.TryObserve(true, out after))
                { Logger.LogWarning("[MRE fixture] FAILED generator power"); return; }
                ObjectObservation safeObservation=null, genObservation=null;
                foreach (var o in after) { if (o.AssetGuid == safe.GUID) safeObservation=o; if (o.AssetGuid == generator.GUID) genObservation=o; }
                if (safeObservation == null || genObservation == null || genObservation.GeneratorPowered != true)
                { Logger.LogWarning("[MRE fixture] FAILED device observation"); return; }
                var safeProtection=new MreDecay.ProtectionObject { Owner=safeObservation.Owner, Group=safeObservation.Group, IdentityKnown=true, Position=new MreDecay.Position3(safeObservation.X,safeObservation.Y,safeObservation.Z) };
                var genProtection=new MreDecay.ProtectionObject { Owner=genObservation.Owner, Group=genObservation.Group, IdentityKnown=true, Position=new MreDecay.Position3(genObservation.X,genObservation.Y,genObservation.Z), Powered=genObservation.GeneratorPowered };
                syntheticSnapshot.Safes=new[]{safeProtection}; syntheticSnapshot.Generators=new[]{genProtection};
                syntheticSnapshot.Buildings[0].Health=(ushort)(floorObservation.Health-2);
                var protectedPlan=MreDecay.Engine.StepEngine.Prepare(syntheticSnapshot);
                if (protectedPlan == null || protectedPlan.Results[0].Health <= syntheticSnapshot.Buildings[0].Health)
                { Logger.LogWarning("[MRE fixture] FAILED grouped devices repair plan"); return; }
                ushort damaged=(ushort)(floorObservation.Health-2);
                if (!MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyStructure(testStructure,floorObservation.Health,damaged) ||
                    !MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyStructure(testStructure,damaged,protectedPlan.Results[0].Health) ||
                    !MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyStructure(testStructure,protectedPlan.Results[0].Health,floorObservation.Health))
                { Logger.LogWarning("[MRE fixture] FAILED grouped repair mutation"); return; }
                if (!BarricadeManager.ServerSetGeneratorPowered(generatorComponent, false) || !ReadOnlyWorld.TryObserve(true, out after))
                { Logger.LogWarning("[MRE fixture] FAILED generator off"); return; }
                foreach (var o in after) if (o.AssetGuid==generator.GUID) genObservation=o;
                if (genObservation.GeneratorPowered != false) { Logger.LogWarning("[MRE fixture] FAILED off observation"); return; }
                genProtection.Powered=false;
                var offPlan=MreDecay.Engine.StepEngine.Prepare(syntheticSnapshot);
                if (offPlan == null || offPlan.Results[0].Health != damaged) { Logger.LogWarning("[MRE fixture] FAILED off protection"); return; }
                Logger.Log("[MRE fixture] observed grouped safe and fueled generator: repair; switched off: protected without repair");
                BarricadeManager.sendFuel(testGenerator.model, 0);
                if (!BarricadeManager.ServerSetGeneratorPowered(generatorComponent, true) || !ReadOnlyWorld.TryObserve(true, out after))
                { Logger.LogWarning("[MRE fixture] FAILED empty generator"); return; }
                foreach (var o in after) if (o.AssetGuid==generator.GUID) genObservation=o;
                if (!generatorComponent.isPowered || generatorComponent.fuel != 0 || genObservation.GeneratorPowered != false)
                { Logger.LogWarning("[MRE fixture] FAILED empty generator observation"); return; }
                genProtection.Powered=genObservation.GeneratorPowered;
                var emptyPlan=MreDecay.Engine.StepEngine.Prepare(syntheticSnapshot);
                if (emptyPlan == null || emptyPlan.Results[0].Health != damaged) { Logger.LogWarning("[MRE fixture] FAILED empty generator plan"); return; }
                syntheticSnapshot.Safes=new MreDecay.ProtectionObject[0];
                var noSafePlan=MreDecay.Engine.StepEngine.Prepare(syntheticSnapshot);
                if (noSafePlan == null || noSafePlan.Results[0].Health >= damaged) { Logger.LogWarning("[MRE fixture] FAILED no safe plan"); return; }
                syntheticSnapshot.Safes=new[]{safeProtection}; safeProtection.Group=98;
                var foreignPlan=MreDecay.Engine.StepEngine.Prepare(syntheticSnapshot);
                if (foreignPlan == null || foreignPlan.Results[0].Health >= damaged) { Logger.LogWarning("[MRE fixture] FAILED foreign safe plan"); return; }
                safeProtection.Group=99; safeProtection.Position=new MreDecay.Position3(floorObservation.X+50,floorObservation.Y,floorObservation.Z);
                var edgePlan=MreDecay.Engine.StepEngine.Prepare(syntheticSnapshot);
                safeProtection.Position=new MreDecay.Position3(floorObservation.X+50.01,floorObservation.Y,floorObservation.Z);
                var beyondPlan=MreDecay.Engine.StepEngine.Prepare(syntheticSnapshot);
                if (edgePlan == null || beyondPlan == null || edgePlan.Results[0].Health != damaged || beyondPlan.Results[0].Health >= damaged)
                { Logger.LogWarning("[MRE fixture] FAILED range boundary plan"); return; }
                Logger.Log("[MRE fixture] controls: empty fueled state=no repair; no safe/foreign safe=decay; synthetic range 50=in, 50.01=out");
                // Additional world-placed objects: observations below use actual positions and owners.
                if (BarricadeManager.dropBarricade(new Barricade(safe), null, new Vector3(0,350,0),0,0,0,4,99)==null ||
                    BarricadeManager.dropBarricade(new Barricade(safe), null, new Vector3(0,350.1f,0),0,0,0,5,99)==null ||
                    BarricadeManager.dropBarricade(new Barricade(safe), null, new Vector3(0,300,10),0,0,0,6,98)==null ||
                    BarricadeManager.dropBarricade(new Barricade(generator), null, new Vector3(0,350,0),0,0,0,7,99)==null ||
                    BarricadeManager.dropBarricade(new Barricade(generator), null, new Vector3(0,350.1f,0),0,0,0,8,99)==null ||
                    BarricadeManager.dropBarricade(new Barricade(generator), null, new Vector3(0,300,10),0,0,0,9,98)==null ||
                    !ReadOnlyWorld.TryObserve(true,out after))
                { Logger.LogWarning("[MRE fixture] FAILED physical controls spawn"); return; }
                ObjectObservation edgeSafe=null, farSafe=null, foreignSafe=null, edgeGen=null, farGen=null, foreignGen=null;
                foreach (var o in after) {
                    if(o.Owner==4) edgeSafe=o; else if(o.Owner==5) farSafe=o; else if(o.Owner==6) foreignSafe=o;
                    else if(o.Owner==7) edgeGen=o; else if(o.Owner==8) farGen=o; else if(o.Owner==9) foreignGen=o;
                }
                if(edgeSafe==null || farSafe==null || foreignSafe==null || edgeGen==null || farGen==null || foreignGen==null ||
                   edgeSafe.Y!=350 || farSafe.Y<=350 || edgeGen.Y!=350 || farGen.Y<=350)
                { Logger.LogWarning("[MRE fixture] FAILED physical controls observations"); return; }
                Func<ObjectObservation,MreDecay.ProtectionObject> asDevice = o => new MreDecay.ProtectionObject {
                    Owner=o.Owner, Group=o.Group, IdentityKnown=true, Position=new MreDecay.Position3(o.X,o.Y,o.Z), Powered=o.GeneratorPowered };
                // Generators are actually powered and fueled through the game API before re-observing.
                foreach (var region in BarricadeManager.regions) foreach (var drop in region.drops) {
                    var d=drop.GetServersideData(); if (d == null || (d.owner!=7 && d.owner!=8 && d.owner!=9)) continue;
                    var g=drop.model.GetComponent<InteractableGenerator>(); if(g==null) { Logger.LogWarning("[MRE fixture] FAILED physical generator"); return; }
                    BarricadeManager.sendFuel(drop.model,100); if(!BarricadeManager.ServerSetGeneratorPowered(g,true)) { Logger.LogWarning("[MRE fixture] FAILED physical power"); return; }
                }
                if (!ReadOnlyWorld.TryObserve(true,out after)) { Logger.LogWarning("[MRE fixture] FAILED physical re-observe"); return; }
                foreach(var o in after) { if(o.Owner==7) edgeGen=o; else if(o.Owner==8) farGen=o; else if(o.Owner==9) foreignGen=o; }
                if(edgeGen.GeneratorPowered!=true || farGen.GeneratorPowered!=true || foreignGen.GeneratorPowered!=true)
                { Logger.LogWarning("[MRE fixture] FAILED powered observation"); return; }
                var origin=new MreDecay.Position3(floorObservation.X,floorObservation.Y,floorObservation.Z);
                Func<ObjectObservation[],ObjectObservation[],MreDecay.ProtectionResult> classify = (ss,gg) => {
                    var sdev=new System.Collections.Generic.List<MreDecay.ProtectionObject>(); var gdev=new System.Collections.Generic.List<MreDecay.ProtectionObject>();
                    foreach(var o in ss) sdev.Add(asDevice(o)); foreach(var o in gg) gdev.Add(asDevice(o));
                    return MreDecay.ProtectionSearch.Find(origin,1,99,true,sdev,true,gdev,true);
                };
                var noPhysicalSafe=classify(new ObjectObservation[0],new[]{edgeGen});
                var foreignDevices=classify(new[]{foreignSafe},new[]{foreignGen});
                var atBoundary=classify(new[]{edgeSafe},new[]{edgeGen});
                var outsideBoundary=classify(new[]{farSafe},new[]{farGen});
                if(noPhysicalSafe.SafePresent!=false || noPhysicalSafe.GeneratorPresent!=true ||
                   foreignDevices.SafePresent!=false || foreignDevices.GeneratorPresent!=false ||
                   atBoundary.SafePresent!=true || atBoundary.GeneratorPresent!=true ||
                   outsideBoundary.SafePresent!=false || outsideBoundary.GeneratorPresent!=false)
                { Logger.LogWarning("[MRE fixture] FAILED physical ownership or 3D radius"); return; }
                Logger.Log("[MRE fixture] physical observations: generator-only, foreign owners/group, 3D 50m inclusive and 50.1m outside");



                StructureManager.damage(testStructure.model, Vector3.zero, 2, 1, false);
                BarricadeManager.damage(testStorage.model, 2, 1, false);
                if (testStructure.GetServersideData().structure.health != floor.health - 2 ||
                    testStorage.GetServersideData().barricade.health != safe.health - 2)
                { Logger.LogWarning("[MRE fixture] FAILED damage"); return; }
                StructureManager.repair(testStructure.model, 1, 1);
                BarricadeManager.repair(testStorage.model, 1, 1);
                if (testStructure.GetServersideData().structure.health != floor.health - 1 ||
                    testStorage.GetServersideData().barricade.health != safe.health - 1)
                { Logger.LogWarning("[MRE fixture] FAILED repair"); return; }
                Logger.Log("[MRE fixture] server health: damage=2 repair=1 for structure and storage");
                var structureHp = testStructure.GetServersideData().structure.health;
                var storageHp = testStorage.GetServersideData().barricade.health;
                if (MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyStructure(testStructure, (ushort)(structureHp - 1), structureHp) ||
                    MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyBarricade(testStorage, (ushort)(storageHp - 1), storageHp) ||
                    !MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyStructure(testStructure, structureHp, (ushort)(structureHp - 1)) ||
                    !MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyBarricade(testStorage, storageHp, (ushort)(storageHp - 1)) ||
                    !MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyStructure(testStructure, (ushort)(structureHp - 1), structureHp) ||
                    !MreDecay.IsolatedRuntime.ProbeHealthMutation.ApplyBarricade(testStorage, (ushort)(storageHp - 1), storageHp))
                { Logger.LogWarning("[MRE fixture] FAILED checked mutation"); return; }
                Logger.Log("[MRE fixture] checked mutation: stale HP refused; damage and repair verified");
                // Synthetic identity, no player connection: this demonstrates event timing,
                // not that a real player caused the request or that raids are detected.
                var syntheticAttacker = new CSteamID(76561198000000000UL);
                ushort structureBefore = testStructure.GetServersideData().structure.health;
                ushort storageBefore = testStorage.GetServersideData().barricade.health;
                requests = 0;
                RaidHookExperiment.Install();
                StructureManager.onDamageStructureRequested += ObserveRequest;
                StructureManager.onDamageStructureRequested += CancelRequest;
                BarricadeManager.onDamageBarricadeRequested += ObserveRequest;
                BarricadeManager.onDamageBarricadeRequested += CancelRequest;
                try
                {
                    StructureManager.damage(testStructure.model, Vector3.zero, 1, 1, false, syntheticAttacker);
                    BarricadeManager.damage(testStorage.model, 1, 1, false, syntheticAttacker);
                    if (requests != 2 || testStructure.GetServersideData().structure.health != structureBefore ||
                        testStorage.GetServersideData().barricade.health != storageBefore)
                    { Logger.LogWarning("[MRE fixture] FAILED cancelled request"); return; }
                    StructureManager.onDamageStructureRequested -= CancelRequest;
                    BarricadeManager.onDamageBarricadeRequested -= CancelRequest;
                    StructureManager.damage(testStructure.model, Vector3.zero, 1, 1, false, syntheticAttacker);
                    BarricadeManager.damage(testStorage.model, 1, 1, false, syntheticAttacker);
                    if (requests != 4 || testStructure.GetServersideData().structure.health != structureBefore - 1 ||
                        testStorage.GetServersideData().barricade.health != storageBefore - 1)
                    { Logger.LogWarning("[MRE fixture] FAILED accepted request"); return; }
                    if (RaidHookExperiment.Cancelled != 2 || RaidHookExperiment.Confirmed != 2)
                    { Logger.LogWarning("[MRE fixture] FAILED post-damage hook"); return; }
                    Logger.Log("[MRE fixture] synthetic attacker: requests observed; cancelled=0 HP, accepted=1 HP on both kinds");
                    Logger.Log("[MRE fixture] Harmony post-damage: cancelled=2 applied=2");
                    StructureManager.onDamageStructureRequested += NestedStructureRequest;
                    try { StructureManager.damage(testStructure.model, Vector3.zero, 1, 1, false, syntheticAttacker); }
                    finally { StructureManager.onDamageStructureRequested -= NestedStructureRequest; }
                    if (!RaidHookExperiment.Ledger.Faulted || RaidHookExperiment.Ledger.RepairAllowed)
                    { Logger.LogWarning("[MRE fixture] FAILED nested damage fence"); return; }
                    Logger.Log("[MRE fixture] nested same-target damage: attribution unknown, repair fenced");
                }
                finally
                {
                    StructureManager.onDamageStructureRequested -= ObserveRequest;
                    StructureManager.onDamageStructureRequested -= CancelRequest;
                    BarricadeManager.onDamageBarricadeRequested -= ObserveRequest;
                    BarricadeManager.onDamageBarricadeRequested -= CancelRequest;
                    RaidHookExperiment.Uninstall();
                }
            }
            catch (Exception) { Logger.LogWarning("[MRE fixture] FAILED exception"); }
            finally { Commander.execute(CSteamID.Nil, "shutdown"); }
        }
    }
}
