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
                bool structure = StructureManager.dropStructure(new Structure(floor, floor.health), new Vector3(0, 300, 0), 0, 0, 0, 1, 0);
                bool storage = BarricadeManager.dropBarricade(new Barricade(safe), null, new Vector3(3, 300, 0), 0, 0, 0, 1, 0) != null;
                bool gen = BarricadeManager.dropBarricade(new Barricade(generator), null, new Vector3(6, 300, 0), 0, 0, 0, 1, 0) != null;
                ObjectObservation[] after;
                if (!structure || !storage || !gen || !ReadOnlyWorld.TryObserve(true, out after)) { Logger.LogWarning("[MRE fixture] FAILED spawn or observation"); return; }
                if (after.Length != 3) { Logger.LogWarning("[MRE fixture] FAILED unexpected observation count"); return; }
                foreach (var o in after)
                    if (o.Owner != 1 || o.Group != 0 || o.Health != o.MaxHealth || o.Y != 300 || o.Z != 0 ||
                        !((o.Kind == "structure" && o.AssetGuid == floor.GUID && o.X == 0) ||
                          (o.Kind == "barricade" && o.AssetGuid == safe.GUID && o.IsStorage && o.X == 3) ||
                          (o.Kind == "barricade" && o.AssetGuid == generator.GUID && o.IsGenerator && o.X == 6)))
                    { Logger.LogWarning("[MRE fixture] FAILED unexpected observation fields"); return; }
                int structures = 0, safes = 0, generators = 0;
                foreach (var o in after) { if (o.Kind == "structure") structures++; if (o.AssetGuid == safe.GUID && o.IsStorage) safes++; if (o.IsGenerator) generators++; }
                Logger.Log("[MRE fixture] structures=" + structures + " safes=" + safes + " generators=" + generators);
            }
            catch (Exception) { Logger.LogWarning("[MRE fixture] FAILED exception"); }
            finally { Commander.execute(CSteamID.Nil, "shutdown"); }
        }
    }
}
