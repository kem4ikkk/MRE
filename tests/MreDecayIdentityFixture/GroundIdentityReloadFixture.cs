// Test-only U3DS fixture. It is guarded to run only from a fresh TEMP copy.
// Ordinary save/restart does not prove crash durability or a production transaction.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MreDecay.Adapter;
using Rocket.API;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreDecay.IsolatedGroundIdentity
{
    public sealed class GroundIdentityConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled;
        public string Mode;
        public void LoadDefaults() { Enabled = false; Mode = "create"; }
    }

    public sealed class GroundIdentityReloadFixture : RocketPlugin<GroundIdentityConfiguration>
    {
        private const string ServerId = "MREDecayGroundIdentityFixture";
        private const string Marker = "[MRE ground identity fixture]";
        private const string SafeGuid = "bf233036b60d403c836deb8ac0f03292";
        private const ushort FloorId = 51;
        private const ulong Owner = 76561198000000001UL;
        private const ulong Group = 76561198000000002UL;
        private readonly Vector3[] FloorPoints = { new Vector3(0, 300, 0), new Vector3(2, 300, 0) };
        private readonly Vector3[] SafePoints = { new Vector3(6, 300, 0), new Vector3(9, 300, 0) };
        private bool armed, shuttingDown;
        private double shutdownAt;

        protected override void Load()
        {
            armed = Configuration.Instance != null && Configuration.Instance.Enabled &&
                Environment.GetEnvironmentVariable("MRE_DECAY_GROUND_IDENTITY") == "YES" &&
                String.Equals(Provider.serverID, ServerId, StringComparison.Ordinal) && ExecutableInTemp();
            if (armed) Logger.Log(Marker + " ARMED mode=" + Configuration.Instance.Mode);
        }

        protected override void Unload() { armed = false; }

        private void Update()
        {
            if (!armed || !Provider.isServer || !Level.isLoaded) return;
            if (shuttingDown)
            {
                if (Time.realtimeSinceStartupAsDouble < shutdownAt) return;
                shuttingDown = false;
                Commander.execute(CSteamID.Nil, "shutdown");
                armed = false;
                return;
            }
            armed = false;
            try
            {
                if (Provider.clients == null || Provider.clients.Count != 0) { Stop("REFUSED clients/list"); return; }
                ObjectObservation[] before;
                if (!ReadOnlyWorld.TryObserve(true, out before)) { Stop("REFUSED incomplete initial observation"); return; }
                string mode = Configuration.Instance.Mode;
                if (mode == "create") CreateGeneration(before);
                else if (mode == "verify_delete") VerifyAndDelete(before);
                else if (mode == "recreate") RecreateGeneration(before);
                else Stop("REFUSED unknown mode");
            }
            catch (Exception ex) { Stop("FAILED exception=" + ex.GetType().Name); }
        }

        private void CreateGeneration(ObjectObservation[] before)
        {
            string state = StatePath();
            if (before.Length != 0 || state == null || File.Exists(state) || File.Exists(state + ".verified"))
            { Stop("REFUSED create requires empty world and fresh receipt"); return; }
            ItemStructureAsset floor = Assets.find(EAssetType.ITEM, FloorId) as ItemStructureAsset;
            ItemStorageAsset safe = Assets.find(new Guid(SafeGuid)) as ItemStorageAsset;
            if (floor == null || floor.health == 0 || safe == null || safe.health == 0 || !safe.isSaveable)
            { Stop("REFUSED missing/non-saveable test assets"); return; }
            Spawn(floor, safe);
            ObjectObservation[] created;
            if (!ReadOnlyWorld.TryObserve(true, out created) || !ValidGeneration(created, floor.GUID, safe.GUID))
            { Stop("FAILED initial generation observation"); return; }
            WriteRecords(state, created);
            Logger.Log(Marker + " CREATE_PASS records=" + created.Length + " ids=" + IdList(created));
            SaveAndShutdown("CREATE_SAVE_REQUESTED");
        }

        private void VerifyAndDelete(ObjectObservation[] before)
        {
            string state = StatePath();
            List<Record> old;
            ItemStructureAsset floor = Assets.find(EAssetType.ITEM, FloorId) as ItemStructureAsset;
            ItemStorageAsset safe = Assets.find(new Guid(SafeGuid)) as ItemStorageAsset;
            if (state == null || !ReadRecords(state, out old) || File.Exists(state + ".verified") || floor == null || safe == null ||
                !ValidGeneration(before, floor.GUID, safe.GUID) ||
                !SameRecords(old, before))
            { Stop("FAILED saved instanceID/asset/owner/position mismatch after restart"); return; }

            foreach (Record record in old)
            {
                if (record.Kind == "structure")
                {
                    StructureDrop drop = FindStructure(record.InstanceId);
                    byte x, y; StructureRegion region;
                    if (drop == null || !StructureManager.tryGetRegion(drop.model, out x, out y, out region) || region == null)
                    { Stop("FAILED structure delete precondition"); return; }
                    StructureManager.destroyStructure(drop, x, y, Vector3.zero, true);
                }
                else
                {
                    BarricadeDrop drop = FindBarricade(record.InstanceId);
                    byte x, y; ushort plant; BarricadeRegion region;
                    if (drop == null || !BarricadeManager.tryGetRegion(drop.model, out x, out y, out plant, out region) || region == null || plant != ushort.MaxValue)
                    { Stop("FAILED barricade delete precondition"); return; }
                    BarricadeManager.destroyBarricade(drop, x, y, plant);
                }
            }
            Logger.Log(Marker + " RELOAD_ID_PASS; deleting original generation before replacement");
            SaveAndShutdown("DELETE_SAVE_REQUESTED");
        }

        private void RecreateGeneration(ObjectObservation[] before)
        {
            string state = StatePath();
            List<Record> old;
            if (state == null || !ReadRecords(state, out old) || File.Exists(state + ".verified") || before.Length != 0)
            { Stop("REFUSED recreate requires empty reloaded world and original receipt"); return; }
            ItemStructureAsset floor = Assets.find(EAssetType.ITEM, FloorId) as ItemStructureAsset;
            ItemStorageAsset safe = Assets.find(new Guid(SafeGuid)) as ItemStorageAsset;
            if (floor == null || safe == null || !safe.isSaveable) { Stop("REFUSED test assets missing on recreate"); return; }
            Spawn(floor, safe);
            ObjectObservation[] current;
            if (!ReadOnlyWorld.TryObserve(true, out current) || !ValidGeneration(current, floor.GUID, safe.GUID) ||
                current.Any(x => old.Any(y => y.Kind == x.Kind && y.InstanceId == x.InstanceId)))
            { Stop("FAILED recreated generation reused an old Kind:InstanceId"); return; }
            File.WriteAllText(state + ".verified", "PASS" + Environment.NewLine, new UTF8Encoding(false));
            Logger.Log(Marker + " DELETE_RECREATE_PASS old=" + IdList(old) + " new=" + IdList(current));
            SaveAndShutdown("RECREATE_SAVE_REQUESTED");
        }

        private void Spawn(ItemStructureAsset floor, ItemStorageAsset safe)
        {
            foreach (Vector3 p in FloorPoints)
                if (!StructureManager.dropStructure(new Structure(floor, floor.health), p, 0, 0, 0, Owner, Group))
                    throw new InvalidOperationException("structure spawn refused");
            foreach (Vector3 p in SafePoints)
                if (BarricadeManager.dropBarricade(new Barricade(safe), null, p, 0, 0, 0, Owner, Group) == null)
                    throw new InvalidOperationException("barricade spawn refused");
        }

        private static bool ValidGeneration(ObjectObservation[] rows, Guid floorGuid, Guid safeGuid)
        {
            if (rows == null || rows.Length != 4 || floorGuid == Guid.Empty || safeGuid == Guid.Empty) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int structures = 0, barricades = 0;
            foreach (ObjectObservation row in rows)
            {
                if (row == null || row.InstanceId == 0 || row.Owner != Owner || row.Group != Group || row.Health != row.MaxHealth ||
                    !ids.Add(row.Kind + ":" + row.InstanceId.ToString(CultureInfo.InvariantCulture))) return false;
                if (row.Kind == "structure" && row.AssetGuid == floorGuid && row.Y == 300f && row.Z == 0f && (row.X == 0f || row.X == 2f)) structures++;
                else if (row.Kind == "barricade" && row.AssetGuid == safeGuid && row.IsStorage && row.Y == 300f && row.Z == 0f && (row.X == 6f || row.X == 9f)) barricades++;
                else return false;
            }
            return structures == 2 && barricades == 2;
        }

        private static bool SameRecords(List<Record> expected, ObjectObservation[] actual)
        {
            if (expected.Count != actual.Length) return false;
            foreach (ObjectObservation row in actual)
            {
                Record match = expected.FirstOrDefault(x => x.Kind == row.Kind && x.X == row.X && x.Y == row.Y && x.Z == row.Z);
                if (match == null || match.InstanceId != row.InstanceId || match.AssetGuid != row.AssetGuid ||
                    match.Owner != row.Owner || match.Group != row.Group || match.Health != row.Health || match.MaxHealth != row.MaxHealth) return false;
            }
            return true;
        }

        private static StructureDrop FindStructure(uint id)
        {
            foreach (StructureRegion region in StructureManager.regions)
                if (region != null && region.drops != null)
                    foreach (StructureDrop drop in region.drops) if (drop != null && drop.instanceID == id) return drop;
            return null;
        }

        private static BarricadeDrop FindBarricade(uint id)
        {
            foreach (BarricadeRegion region in BarricadeManager.regions)
                if (region != null && region.drops != null)
                    foreach (BarricadeDrop drop in region.drops) if (drop != null && drop.instanceID == id) return drop;
            return null;
        }

        private static void WriteRecords(string path, ObjectObservation[] rows)
        {
            var lines = new List<string> { "MRE_GROUND_IDENTITY_V1" };
            foreach (ObjectObservation row in rows.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.X))
                lines.Add(String.Join("|", row.Kind, row.InstanceId.ToString(CultureInfo.InvariantCulture), row.AssetGuid.ToString("N"),
                    row.Owner.ToString(CultureInfo.InvariantCulture), row.Group.ToString(CultureInfo.InvariantCulture),
                    row.X.ToString("R", CultureInfo.InvariantCulture), row.Y.ToString("R", CultureInfo.InvariantCulture),
                    row.Z.ToString("R", CultureInfo.InvariantCulture), row.Health.ToString(CultureInfo.InvariantCulture), row.MaxHealth.ToString(CultureInfo.InvariantCulture)));
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
        }

        private static bool ReadRecords(string path, out List<Record> rows)
        {
            rows = null;
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length != 5 || lines[0] != "MRE_GROUND_IDENTITY_V1") return false;
                var result = new List<Record>();
                foreach (string line in lines.Skip(1))
                {
                    string[] p = line.Split('|');
                    Record r; Guid guid; uint id; ulong owner, group; float x,y,z; ushort hp,max;
                    if (p.Length != 10 || (p[0] != "structure" && p[0] != "barricade") || !UInt32.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out id) || id == 0 ||
                        !Guid.TryParseExact(p[2], "N", out guid) || guid == Guid.Empty || !UInt64.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out owner) ||
                        !UInt64.TryParse(p[4], NumberStyles.None, CultureInfo.InvariantCulture, out group) || !Single.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                        !Single.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !Single.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out z) ||
                        !UInt16.TryParse(p[8], NumberStyles.None, CultureInfo.InvariantCulture, out hp) || !UInt16.TryParse(p[9], NumberStyles.None, CultureInfo.InvariantCulture, out max)) return false;
                    r = new Record { Kind=p[0], InstanceId=id, AssetGuid=guid, Owner=owner, Group=group, X=x,Y=y,Z=z,Health=hp,MaxHealth=max };
                    if (result.Any(q => q.Kind == r.Kind && q.InstanceId == r.InstanceId) || result.Any(q => q.Kind == r.Kind && q.X == r.X && q.Y == r.Y && q.Z == r.Z)) return false;
                    result.Add(r);
                }
                if (result.Count != 4 || result.Count(x => x.Kind == "structure") != 2 || result.Count(x => x.Kind == "barricade") != 2) return false;
                rows = result; return true;
            }
            catch { return false; }
        }

        private string StatePath()
        {
            try
            {
                string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string exe = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                if (!exe.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                return Path.Combine(Path.GetDirectoryName(exe), "Servers", ServerId, "Server", "ground-identity-state.txt");
            }
            catch { return null; }
        }

        private static bool ExecutableInTemp()
        {
            try
            {
                string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string exe = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                return exe.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void SaveAndShutdown(string marker)
        {
            if (!Commander.execute(CSteamID.Nil, "save")) { Stop("FAILED save command rejected"); return; }
            Logger.Log(Marker + " " + marker + "; ordinary save/restart only");
            shutdownAt = Time.realtimeSinceStartupAsDouble + 5d;
            shuttingDown = true;
            armed = true;
        }

        private static string IdList(IEnumerable<ObjectObservation> rows) => String.Join(",", rows.Select(x => x.Kind + ":" + x.InstanceId).OrderBy(x => x, StringComparer.Ordinal));
        private static string IdList(IEnumerable<Record> rows) => String.Join(",", rows.Select(x => x.Kind + ":" + x.InstanceId).OrderBy(x => x, StringComparer.Ordinal));
        private static void Stop(string reason) { Logger.LogWarning(Marker + " " + reason); Commander.execute(CSteamID.Nil, "shutdown"); }

        private sealed class Record
        {
            public string Kind; public uint InstanceId; public Guid AssetGuid; public ulong Owner, Group; public float X,Y,Z; public ushort Health,MaxHealth;
        }
    }
}
