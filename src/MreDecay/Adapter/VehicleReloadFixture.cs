// Isolated TEMP-only integration fixture. Never install on a working server.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using MreDecay.Adapter;
using Rocket.API;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreDecay.IsolatedVehicleReload
{
    public sealed class VehicleReloadConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled;
        public string Mode;
        public ushort VehicleAssetId;
        public string BarricadeAssetGuid;
        public void LoadDefaults()
        {
            Enabled = false;
            Mode = "attach";
            VehicleAssetId = 185;
            BarricadeAssetGuid = String.Empty;
        }
    }

    public sealed class VehicleReloadFixture : RocketPlugin<VehicleReloadConfiguration>
    {
        private const string ServerId = "MREDecayVehicleReloadFixture";
        private const ulong TestOwner = 76561198000000001UL;
        private const ulong TestGroup = 76561198000000002UL;
        private const string MarkerPrefix = "[MRE vehicle reload fixture]";
        private bool armed;
        private bool waitingForShutdown;
        private double shutdownAt;
        private string refuseAtStart;

        protected override void Load()
        {
            armed = false;
            if (Configuration.Instance == null || !Configuration.Instance.Enabled ||
                Environment.GetEnvironmentVariable("MRE_DECAY_VEHICLE_RELOAD") != "YES" ||
                !String.Equals(Provider.serverID, ServerId, StringComparison.Ordinal) || !ExecutableInTemp()) return;
            string mode = Configuration.Instance.Mode;
            if (!String.Equals(mode, "attach", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(mode, "verify", StringComparison.OrdinalIgnoreCase)) return;
            if (String.Equals(mode, "verify", StringComparison.OrdinalIgnoreCase))
            {
                string state = StatePath();
                if (state == null || File.Exists(state + ".verified"))
                {
                    refuseAtStart = "REFUSED single-use verify receipt is missing or already consumed";
                    armed = true;
                    return;
                }
            }
            armed = true;
            Logger.Log(MarkerPrefix + " ARMED mode=" + mode);
        }

        protected override void Unload() { armed = false; }

        private void Update()
        {
            if (!armed || !Provider.isServer || !Level.isLoaded) return;
            if (refuseAtStart != null)
            {
                armed = false;
                Stop(refuseAtStart);
                return;
            }
            if (waitingForShutdown)
            {
                if (Time.realtimeSinceStartupAsDouble < shutdownAt) return;
                waitingForShutdown = false;
                Logger.Log(MarkerPrefix + " SHUTDOWN_REQUESTED");
                Commander.execute(CSteamID.Nil, "shutdown");
                armed = false;
                return;
            }
            armed = false;
            try
            {
                if (Provider.clients == null || Provider.clients.Count != 0)
                {
                    Stop("REFUSED players or client list unavailable");
                    return;
                }
                if (String.Equals(Configuration.Instance.Mode, "attach", StringComparison.OrdinalIgnoreCase)) Attach();
                else VerifyReload();
            }
            catch (Exception ex)
            {
                Stop("FAILED exception=" + ex.GetType().Name);
            }
        }

        private void Attach()
        {
            ObjectObservation[] before;
            if (!ReadOnlyWorld.TryObserve(true, out before) || before.Length != 0)
            {
                Stop("REFUSED world observation not empty/complete");
                return;
            }
            string stateReceipt = StatePath();
            if (stateReceipt == null || File.Exists(stateReceipt) || File.Exists(stateReceipt + ".verified") || File.Exists(stateReceipt + ".previous"))
            {
                Stop("REFUSED stale fixture receipt; use a fresh TEMP copy");
                return;
            }
            VehicleAsset vehicleAsset = Assets.find(EAssetType.VEHICLE, Configuration.Instance.VehicleAssetId) as VehicleAsset;
            Guid barricadeGuid;
            if (vehicleAsset == null || vehicleAsset.GUID == Guid.Empty || !vehicleAsset.IsExplosionEffectRefNull() ||
                vehicleAsset.ShouldExplosionCauseDamage || vehicleAsset.ShouldExplosionBurnMaterials ||
                !Guid.TryParse(Configuration.Instance.BarricadeAssetGuid, out barricadeGuid) || barricadeGuid == Guid.Empty)
            {
                Stop("REFUSED missing/unsafe configured vehicle or barricade asset");
                return;
            }
            ItemBarricadeAsset barricadeAsset = Assets.find(barricadeGuid) as ItemBarricadeAsset;
            if (!SafeBarricadeAsset(barricadeAsset))
            {
                Stop("REFUSED barricade asset is not an inert, saveable base barricade");
                return;
            }
            Vector3 spawn;
            Quaternion spawnRotation;
            if (!TryFindSpawnPoint(out spawn, out spawnRotation))
            {
                Stop("REFUSED no clear safe terrain spawn point");
                return;
            }

            InteractableVehicle vehicle = VehicleManager.spawnVehicleV2(vehicleAsset.id, spawn, spawnRotation);
            if (vehicle == null || vehicle.IsPendingDestroy || vehicle.instanceID == 0 || vehicle.asset == null ||
                vehicle.asset.GUID != vehicleAsset.GUID || vehicle.isAutoClearable || !ContainsVehicle(vehicle))
            {
                Stop("FAILED public vehicle spawn did not produce the expected live vehicle");
                return;
            }
            VehicleBarricadeRegion region = BarricadeManager.FindVehicleRegionByTransform(vehicle.transform);
            if (region == null || !ReferenceEquals(region.vehicle, vehicle) || region.parent == null || region.drops == null)
            {
                Stop("FAILED spawned vehicle region is unavailable");
                return;
            }
            foreach (VehicleBarricadeRegion existing in BarricadeManager.vehicleRegions)
            {
                if (existing == null || existing.drops == null)
                {
                    Stop("REFUSED incomplete vehicle region before attachment");
                    return;
                }
                if (existing.drops.Count != 0)
                {
                    Stop("REFUSED existing vehicle barricades in isolated world");
                    return;
                }
            }

            Vector3 localPoint = new Vector3(0f, 1.25f, 0f);
            Quaternion localRotation = Quaternion.identity;
            Transform root = BarricadeManager.dropPlantedBarricade(region.parent, new Barricade(barricadeAsset),
                localPoint, localRotation, TestOwner, TestGroup);
            BarricadeDrop drop = root == null ? null : BarricadeManager.FindBarricadeByRootTransform(root);
            VehicleBarricadeRegion attachedRegion = root == null ? null : BarricadeManager.FindVehicleRegionByTransform(root.parent);
            if (drop == null || attachedRegion == null || !ReferenceEquals(attachedRegion.vehicle, vehicle) ||
                !drop.IsChildOfVehicle || !ReferenceEquals(root.parent, attachedRegion.parent) ||
                attachedRegion.subvehicleIndex != region.subvehicleIndex)
            {
                Stop("FAILED public planted barricade attach/region assertions");
                return;
            }
            ObjectObservation[] after;
            if (!ReadOnlyWorld.TryObserve(true, out after) || after.Length != 1 ||
                !MatchesObservation(after[0], drop, attachedRegion, barricadeAsset, vehicle))
            {
                Stop("FAILED read-only vehicle attachment observation");
                return;
            }
            Vector3 expectedWorld = attachedRegion.parent.TransformPoint(localPoint);
            if (Vector3.Distance(root.position, expectedWorld) > 0.05f ||
                Quaternion.Angle(root.rotation, attachedRegion.parent.rotation * localRotation) > 1f)
            {
                Stop("FAILED local transform contract after attach");
                return;
            }
            string statePath = StatePath();
            if (statePath == null)
            {
                Stop("REFUSED state receipt path outside TEMP");
                return;
            }
            WriteState(statePath, vehicleAsset, vehicle, barricadeAsset, drop, attachedRegion, spawn, localPoint, localRotation);
            Logger.Log(MarkerPrefix + " ATTACH_PASS vehicle_id=" + vehicleAsset.id + " vehicle_guid=" + vehicleAsset.GUID +
                " vehicle_instance=" + vehicle.instanceID + " barricade_guid=" + barricadeAsset.GUID +
                " barricade_instance=" + drop.instanceID + " subvehicle=" + attachedRegion.subvehicleIndex +
                " world=" + Vec(root.position) + " local=" + Vec(localPoint));
            if (!Commander.execute(CSteamID.Nil, "save"))
            {
                Stop("FAILED normal save command rejected");
                return;
            }
            Logger.Log(MarkerPrefix + " SAVE_REQUESTED; waiting 5 seconds before normal shutdown");
            shutdownAt = Time.realtimeSinceStartupAsDouble + 5d;
            waitingForShutdown = true;
            armed = true;
        }

        private void VerifyReload()
        {
            string statePath = StatePath();
            FixtureState state;
            if (statePath == null || !TryReadState(statePath, out state))
            {
                Stop("REFUSED attach receipt missing or invalid");
                return;
            }
            ObjectObservation[] observed;
            if (!ReadOnlyWorld.TryObserve(true, out observed) || observed.Length != 1)
            {
                Stop("FAILED reloaded world observation is incomplete or duplicated");
                return;
            }
            VehicleAsset vehicleAsset = Assets.find(EAssetType.VEHICLE, state.VehicleAssetId) as VehicleAsset;
            ItemBarricadeAsset barricadeAsset = Assets.find(state.BarricadeGuid) as ItemBarricadeAsset;
            if (!SafeBarricadeAsset(barricadeAsset) || vehicleAsset == null || vehicleAsset.GUID != state.VehicleGuid ||
                observed[0].AssetGuid != state.BarricadeGuid || observed[0].Owner != TestOwner || observed[0].Group != TestGroup ||
                observed[0].VehicleInstanceId != state.VehicleInstanceId || observed[0].VehicleInstanceId == 0 ||
                observed[0].VehicleSubvehicleIndex != state.SubvehicleIndex ||
                !observed[0].IsVehicleAttached || observed[0].InstanceId != state.BarricadeInstanceId)
            {
                Stop("FAILED persisted object identity/ownership/attachment mismatch");
                return;
            }
            InteractableVehicle vehicle = FindVehicle(observed[0].VehicleInstanceId);
            if (vehicle == null || vehicle.IsPendingDestroy || vehicle.asset == null || vehicle.asset.GUID != state.VehicleGuid ||
                vehicle.isAutoClearable || Vector3.Distance(vehicle.transform.position, state.SpawnPosition) > 20f)
            {
                Stop("FAILED persisted parent vehicle identity/position");
                return;
            }
            VehicleBarricadeRegion region = BarricadeManager.FindVehicleRegionByTransform(vehicle.transform);
            BarricadeDrop drop = FindBarricade(observed[0].InstanceId);
            if (region == null || drop == null || !ReferenceEquals(region.vehicle, vehicle) || region.drops == null ||
                !region.drops.Contains(drop) || !drop.IsChildOfVehicle || !ReferenceEquals(drop.model.parent, region.parent) ||
                region.subvehicleIndex != state.SubvehicleIndex || drop.asset == null || drop.asset.GUID != state.BarricadeGuid)
            {
                Stop("FAILED persisted vehicle region relationship");
                return;
            }
            Vector3 expectedWorld = region.parent.TransformPoint(state.LocalPoint);
            if (Vector3.Distance(drop.model.position, expectedWorld) > 0.1f ||
                Quaternion.Angle(drop.model.rotation, region.parent.rotation * state.LocalRotation) > 2f)
            {
                Stop("FAILED persisted local barricade transform");
                return;
            }
            Logger.Log(MarkerPrefix + " RELOAD_PASS vehicle_guid=" + state.VehicleGuid +
                " vehicle_instance=" + vehicle.instanceID + " barricade_guid=" + state.BarricadeGuid +
                " barricade_instance=" + drop.instanceID + " subvehicle=" + region.subvehicleIndex +
                " world=" + Vec(drop.model.position) + " local=" + Vec(state.LocalPoint));
            File.WriteAllText(statePath + ".verified", "PASS" + Environment.NewLine, new UTF8Encoding(false));
            if (!Commander.execute(CSteamID.Nil, "save"))
            {
                Stop("FAILED verify shutdown save command rejected");
                return;
            }
            Logger.Log(MarkerPrefix + " VERIFY_SAVE_REQUESTED; waiting 5 seconds before normal shutdown");
            shutdownAt = Time.realtimeSinceStartupAsDouble + 5d;
            waitingForShutdown = true;
            armed = true;
        }

        private static bool SafeBarricadeAsset(ItemBarricadeAsset asset)
        {
            if (asset == null || asset.GetType() != typeof(ItemBarricadeAsset) || asset.GUID == Guid.Empty ||
                asset.health == 0 || !asset.isSaveable || asset.explosionGuid != Guid.Empty ||
                asset.ExplosionEffectFlags != EPlaceableExplosionEffectFlags.None || asset.barricade == null)
                return false;
            Component[] components = asset.barricade.GetComponentsInChildren<Component>(true);
            if (components == null) return false;
            foreach (Component component in components) if (component is Interactable) return false;
            return true;
        }

        private static bool TryFindSpawnPoint(out Vector3 point, out Quaternion rotation)
        {
            point = Vector3.zero;
            rotation = Quaternion.identity;
            if (LevelVehicles.spawns == null || VehicleManager.vehicles == null) return false;
            foreach (VehicleSpawnpoint spawn in LevelVehicles.spawns)
            {
                if (spawn == null) continue;
                Vector3 candidate = spawn.point;
                if (!Finite(candidate.x) || !Finite(candidate.y) || !Finite(candidate.z) ||
                    !Level.checkSafeIncludingClipVolumes(candidate) || !Level.isPointWithinValidHeight(candidate.y)) continue;
                var nearby = new List<InteractableVehicle>();
                VehicleManager.getVehiclesInRadius(candidate, 40f, nearby);
                if (nearby.Count != 0) continue;
                point = candidate;
                rotation = Quaternion.Euler(0f, spawn.angle, 0f);
                return true;
            }
            return false;
        }

        private static bool MatchesObservation(ObjectObservation observation, BarricadeDrop drop,
            VehicleBarricadeRegion region, ItemBarricadeAsset asset, InteractableVehicle vehicle)
        {
            return observation != null && observation.Kind == "barricade" && observation.IsVehicleAttached &&
                observation.InstanceId == drop.instanceID && observation.AssetGuid == asset.GUID &&
                observation.Owner == TestOwner && observation.Group == TestGroup &&
                observation.VehicleInstanceId == vehicle.instanceID && observation.VehicleSubvehicleIndex == region.subvehicleIndex;
        }

        private static bool ContainsVehicle(InteractableVehicle candidate)
        {
            if (VehicleManager.vehicles == null) return false;
            foreach (InteractableVehicle item in VehicleManager.vehicles) if (ReferenceEquals(item, candidate)) return true;
            return false;
        }

        private static InteractableVehicle FindVehicle(uint instanceId)
        {
            if (VehicleManager.vehicles == null) return null;
            foreach (InteractableVehicle item in VehicleManager.vehicles)
                if (item != null && item.instanceID == instanceId) return item;
            return null;
        }

        private static BarricadeDrop FindBarricade(uint instanceId)
        {
            if (BarricadeManager.vehicleRegions == null) return null;
            foreach (VehicleBarricadeRegion region in BarricadeManager.vehicleRegions)
            {
                if (region == null || region.drops == null) continue;
                foreach (BarricadeDrop drop in region.drops) if (drop != null && drop.instanceID == instanceId) return drop;
            }
            return null;
        }

        private static void WriteState(string path, VehicleAsset vehicleAsset, InteractableVehicle vehicle,
            ItemBarricadeAsset barricadeAsset, BarricadeDrop drop, VehicleBarricadeRegion region,
            Vector3 spawn, Vector3 localPoint, Quaternion localRotation)
        {
            string[] lines = {
                "format=1", "vehicleAssetId=" + vehicleAsset.id.ToString(CultureInfo.InvariantCulture),
                "vehicleGuid=" + vehicleAsset.GUID.ToString("D"), "vehicleInstanceId=" + vehicle.instanceID.ToString(CultureInfo.InvariantCulture),
                "barricadeGuid=" + barricadeAsset.GUID.ToString("D"), "barricadeInstanceId=" + drop.instanceID.ToString(CultureInfo.InvariantCulture),
                "subvehicleIndex=" + region.subvehicleIndex.ToString(CultureInfo.InvariantCulture),
                "spawn=" + Vec(spawn), "vehicleRotation=" + Quat(vehicle.transform.rotation), "local=" + Vec(localPoint), "rotation=" + Quat(localRotation),
                "owner=" + TestOwner.ToString(CultureInfo.InvariantCulture), "group=" + TestGroup.ToString(CultureInfo.InvariantCulture)
            };
            string temp = path + ".tmp";
            File.WriteAllLines(temp, lines, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".previous"); else File.Move(temp, path);
        }

        private sealed class FixtureState
        {
            public ushort VehicleAssetId;
            public Guid VehicleGuid, BarricadeGuid;
            public uint VehicleInstanceId, BarricadeInstanceId;
            public int SubvehicleIndex;
            public Vector3 SpawnPosition, LocalPoint;
            public Quaternion LocalRotation;
        }

        private static bool TryReadState(string path, out FixtureState state)
        {
            state = null;
            try
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(path))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0 || values.ContainsKey(line.Substring(0, separator))) return false;
                    values.Add(line.Substring(0, separator), line.Substring(separator + 1));
                }
                FixtureState parsed = new FixtureState();
                if (Get(values, "format") != "1" || !UShort(values, "vehicleAssetId", out parsed.VehicleAssetId) ||
                    !GuidValue(values, "vehicleGuid", out parsed.VehicleGuid) || !GuidValue(values, "barricadeGuid", out parsed.BarricadeGuid) ||
                    !UInt(values, "vehicleInstanceId", out parsed.VehicleInstanceId) || !UInt(values, "barricadeInstanceId", out parsed.BarricadeInstanceId) ||
                    !Int(values, "subvehicleIndex", out parsed.SubvehicleIndex) || !Vector(values, "spawn", out parsed.SpawnPosition) ||
                    !Vector(values, "local", out parsed.LocalPoint) || !QuaternionValue(values, "rotation", out parsed.LocalRotation) ||
                    Get(values, "owner") != TestOwner.ToString(CultureInfo.InvariantCulture) ||
                    Get(values, "group") != TestGroup.ToString(CultureInfo.InvariantCulture)) return false;
                state = parsed;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static string Get(Dictionary<string, string> d, string key) { string value; return d.TryGetValue(key, out value) ? value : null; }
        private static bool UShort(Dictionary<string, string> d, string k, out ushort v) { return UInt16.TryParse(Get(d, k), NumberStyles.None, CultureInfo.InvariantCulture, out v); }
        private static bool UInt(Dictionary<string, string> d, string k, out uint v) { return UInt32.TryParse(Get(d, k), NumberStyles.None, CultureInfo.InvariantCulture, out v); }
        private static bool Int(Dictionary<string, string> d, string k, out int v) { return Int32.TryParse(Get(d, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out v); }
        private static bool GuidValue(Dictionary<string, string> d, string k, out Guid v) { return Guid.TryParse(Get(d, k), out v) && v != Guid.Empty; }
        private static bool Vector(Dictionary<string, string> d, string k, out Vector3 v)
        {
            v = Vector3.zero; string[] p = Get(d, k) == null ? new string[0] : Get(d, k).Split(',');
            float x, y, z; if (p.Length != 3 || !Float(p[0], out x) || !Float(p[1], out y) || !Float(p[2], out z)) return false;
            v = new Vector3(x, y, z); return true;
        }
        private static bool QuaternionValue(Dictionary<string, string> d, string k, out Quaternion v)
        {
            v = Quaternion.identity; string[] p = Get(d, k) == null ? new string[0] : Get(d, k).Split(',');
            float x, y, z, w; if (p.Length != 4 || !Float(p[0], out x) || !Float(p[1], out y) || !Float(p[2], out z) || !Float(p[3], out w)) return false;
            v = new Quaternion(x, y, z, w); return Finite(v.x) && Finite(v.y) && Finite(v.z) && Finite(v.w) && Math.Abs(v.x*v.x+v.y*v.y+v.z*v.z+v.w*v.w-1f) < 0.01f;
        }
        private static bool Float(string s, out float v) { return Single.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && Finite(v); }
        private static string Vec(Vector3 v) { return v.x.ToString("R", CultureInfo.InvariantCulture)+","+v.y.ToString("R", CultureInfo.InvariantCulture)+","+v.z.ToString("R", CultureInfo.InvariantCulture); }
        private static string Quat(Quaternion q) { return q.x.ToString("R", CultureInfo.InvariantCulture)+","+q.y.ToString("R", CultureInfo.InvariantCulture)+","+q.z.ToString("R", CultureInfo.InvariantCulture)+","+q.w.ToString("R", CultureInfo.InvariantCulture); }
        private static bool Finite(float x) { return !Single.IsNaN(x) && !Single.IsInfinity(x); }

        private string StatePath()
        {
            string root = Path.GetDirectoryName(Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName));
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) return null;
            string path = Path.GetFullPath(Path.Combine(root, "Servers", ServerId, "Server", "vehicle-reload-state.txt"));
            return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        private static bool ExecutableInTemp()
        {
            try
            {
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string exe = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                return exe.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static void Stop(string reason)
        {
            Logger.LogWarning(MarkerPrefix + " " + reason);
            Commander.execute(CSteamID.Nil, "shutdown");
        }
    }
}
