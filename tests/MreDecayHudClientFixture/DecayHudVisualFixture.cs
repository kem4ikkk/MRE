// TEST ONLY. Sends a visibly labelled UI effect to exactly two clients on a guarded TEMP server.
// This fixture does not read decay zones and does not call production HUD policy/adapter.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Rocket.Core.Plugins;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreDecayHudClientFixture
{
    public sealed class DecayHudVisualFixture : RocketPlugin
    {
        private const string ExpectedServerId = "MREDecayHudVisualTest";
        private const string ExpectedMapName = "MREDecayTest";
        private const string RequiredEnvironment = "MRE_DECAY_HUD_VISUAL_TEST";
        private const string RequiredEnvironmentValue = "YES";
        private const ushort EffectId = 48623;
        private const short EffectKey = 14823;
        private static readonly Guid EffectGuid = new Guid("5d4628c25c73441a89ef5aed4db47713");
        private const string Panel = "DecayZonePanel";
        private const string Label = "DecayZoneText";
        private const string TestText = "ТЕСТ UI: эффект загружен";
        private const double WaitForClientsSeconds = 120d;
        private const double ShowSeconds = 12d;

        private readonly Dictionary<ulong, ShownSession> shown = new Dictionary<ulong, ShownSession>();
        private int serverThread;
        private bool checkedGuard;
        private bool finished;
        private bool visible;
        private bool cleanupOnly;
        private double armedAt;
        private double shownAt;

        protected override void Load()
        {
            serverThread = Thread.CurrentThread.ManagedThreadId;
            checkedGuard = false;
            finished = false;
            visible = false;
            cleanupOnly = false;
            shown.Clear();
            armedAt = Time.realtimeSinceStartupAsDouble;
            Logger.Log("[MRE decay HUD TEST] Fixture loaded; waiting for map and exactly two test clients.");
        }

        protected override void Unload()
        {
            if (Thread.CurrentThread.ManagedThreadId != serverThread)
            {
                Logger.LogWarning("[MRE decay HUD TEST] TEST_UNLOAD_OFF_THREAD; skipped UI cleanup to avoid off-thread game API calls.");
                finished = true;
                return;
            }
            ClearVisibleEffects();
            if (shown.Count != 0)
                Logger.LogWarning("[MRE decay HUD TEST] TEST_UNLOAD_CLEANUP_PENDING; remaining online effects were not cleared.");
            finished = true;
        }

        private void Update()
        {
            if (finished) return;
            if (serverThread != Thread.CurrentThread.ManagedThreadId)
            {
                Logger.LogWarning("[MRE decay HUD TEST] TEST_REFUSED server thread changed; no off-thread UI cleanup attempted.");
                finished = true;
                return;
            }
            if (!Provider.isServer || !Level.isLoaded) return;
            if (cleanupOnly)
            {
                ClearVisibleEffects();
                if (shown.Count == 0)
                {
                    finished = true;
                    Logger.Log("[MRE decay HUD TEST] TEST_EFFECT_CLEARED");
                }
                return;
            }

            if (!checkedGuard)
            {
                checkedGuard = true;
                string reason;
                if (!TryGuard(out reason)) { Refuse(reason); return; }
                Logger.Log("[MRE decay HUD TEST] Guard passed; no decay source is enabled.");
            }

            if (!visible)
            {
                if (Provider.clients == null) { Refuse("client list unavailable"); return; }
                if (Provider.clients.Count > 2) { Refuse("expected exactly two clients; refusing to show to extras"); return; }
                if (Provider.clients.Count != 2)
                {
                    if (Time.realtimeSinceStartupAsDouble - armedAt > WaitForClientsSeconds)
                        Refuse("timed out waiting for exactly two clients");
                    return;
                }
                if (!TryShowToBoth()) Refuse("effect creation or delivery call failed");
                return;
            }

            if (!StillSameTwoClients())
            {
                Refuse("test client set changed while the effect was visible");
                return;
            }

            if (Time.realtimeSinceStartupAsDouble - shownAt >= ShowSeconds)
            {
                ClearVisibleEffects();
                if (shown.Count == 0)
                {
                    finished = true;
                    Logger.Log("[MRE decay HUD TEST] TEST_EFFECT_CLEARED");
                }
                else cleanupOnly = true;
            }
        }

        private bool TryGuard(out string reason)
        {
            reason = null;
            try
            {
                if (!String.Equals(Provider.serverID, ExpectedServerId, StringComparison.Ordinal))
                { reason = "unexpected serverID"; return false; }
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string executable = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                if (!executable.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
                { reason = "server executable is outside TEMP"; return false; }
                if (!String.Equals(Environment.GetEnvironmentVariable(RequiredEnvironment), RequiredEnvironmentValue, StringComparison.Ordinal))
                { reason = "environment marker is absent"; return false; }
                if (Level.info == null || String.IsNullOrEmpty(Level.info.path) ||
                    !String.Equals(new DirectoryInfo(Level.info.path).Name, ExpectedMapName, StringComparison.Ordinal))
                { reason = "unexpected map"; return false; }
                var byId = Assets.find(EAssetType.EFFECT, EffectId) as EffectAsset;
                if (byId == null || byId.id != EffectId)
                { reason = "effect ID is unavailable"; return false; }
                if (!ReferenceEquals(byId, Assets.find(EffectGuid)) || byId.GUID != EffectGuid)
                { reason = "effect ID/GUID mismatch"; return false; }
                if (Provider.clients == null || Provider.clients.Count != 0)
                { reason = "clients joined before the guarded map/effect check"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                reason = "guard exception: " + ex.GetType().Name;
                return false;
            }
        }

        private bool TryShowToBoth()
        {
            var found = new Dictionary<ulong, ShownSession>();
            foreach (SteamPlayer client in Provider.clients)
            {
                if (client == null || client.player == null || client.player.channel == null || client.playerID == null)
                    return false;
                ulong id = client.playerID.steamID.m_SteamID;
                ITransportConnection connection = client.player.channel.GetOwnerTransportConnection();
                if (id == 0 || found.ContainsKey(id) || connection == null)
                    return false;
                found.Add(id, new ShownSession(client, client.player, connection));
            }
            if (found.Count != 2) return false;

            try
            {
                foreach (KeyValuePair<ulong, ShownSession> entry in found)
                {
                    // Track before the first send because a thrown API call can have an unknown partial outcome.
                    shown.Add(entry.Key, entry.Value);
                    EffectManager.SendUIEffect(Assets.find(EffectGuid) as EffectAsset, EffectKey, entry.Value.Connection, true);
                    EffectManager.sendUIEffectText(EffectKey, entry.Value.Connection, true, Label, TestText);
                    EffectManager.sendUIEffectVisibility(EffectKey, entry.Value.Connection, true, Panel, true);
                }
                visible = true;
                shownAt = Time.realtimeSinceStartupAsDouble;
                Logger.Log("[MRE decay HUD TEST] TEST_EFFECT_SENT_TO_TWO_CLIENTS");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[MRE decay HUD TEST] TEST_SEND_FAILED " + ex.GetType().Name);
                ClearVisibleEffects();
                return false;
            }
        }

        private bool StillSameTwoClients()
        {
            if (Provider.clients == null || Provider.clients.Count != 2 || shown.Count != 2) return false;
            var current = new HashSet<ulong>();
            foreach (SteamPlayer client in Provider.clients)
            {
                if (client == null || client.playerID == null || client.player == null || client.player.channel == null)
                    return false;
                ulong id = client.playerID.steamID.m_SteamID;
                if (id == 0 || !current.Add(id)) return false;
                ShownSession original;
                object connection = client.player.channel.GetOwnerTransportConnection();
                if (connection == null || !shown.TryGetValue(id, out original) ||
                    !ReferenceEquals(client, original.Client) || !ReferenceEquals(client.player, original.Player) ||
                    !ReferenceEquals(connection, original.Connection)) return false;
            }
            return current.Count == shown.Count;
        }

        private void ClearVisibleEffects()
        {
            if (shown.Count == 0) { visible = false; return; }
            var remaining = new Dictionary<ulong, ShownSession>(shown);
            if (Provider.clients != null)
            {
                foreach (KeyValuePair<ulong, ShownSession> entry in shown)
                {
                    SteamPlayer current = null;
                    foreach (SteamPlayer candidate in Provider.clients)
                        if (ReferenceEquals(candidate, entry.Value.Client)) { current = candidate; break; }

                    // This exact SteamPlayer session vanished. Never send a stale clear
                    // to a new session that happens to reuse the same SteamID.
                    if (current == null) { remaining.Remove(entry.Key); continue; }

                    // A listed session with temporarily missing identity/channel data is
                    // unresolved. Keep it pending so a later Update can retry safely.
                    if (current.player == null || current.playerID == null || current.player.channel == null)
                        continue;
                    if (!ReferenceEquals(current.player, entry.Value.Player) ||
                        current.playerID.steamID.m_SteamID != entry.Key)
                    { remaining.Remove(entry.Key); continue; }

                    ITransportConnection connection = current.player.channel.GetOwnerTransportConnection();
                    if (connection == null) continue;
                    // The SteamPlayer may remain listed while its transport has already
                    // been replaced. That is a new connection; do not clear through it.
                    if (!ReferenceEquals(connection, entry.Value.Connection))
                    { remaining.Remove(entry.Key); continue; }
                    try
                    {
                        EffectManager.ClearEffectByGuid(EffectGuid, entry.Value.Connection);
                        remaining.Remove(entry.Key);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("[MRE decay HUD TEST] TEST_CLEAR_RETRY_REQUIRED " + ex.GetType().Name);
                    }
                }
            }
            shown.Clear();
            foreach (KeyValuePair<ulong, ShownSession> entry in remaining) shown.Add(entry.Key, entry.Value);
            visible = shown.Count > 0;
        }

        private void Refuse(string reason)
        {
            ClearVisibleEffects();
            finished = shown.Count == 0;
            cleanupOnly = !finished;
            Logger.LogWarning("[MRE decay HUD TEST] TEST_REFUSED " + reason);
        }

        private sealed class ShownSession
        {
            internal readonly SteamPlayer Client;
            internal readonly Player Player;
            internal readonly ITransportConnection Connection;
            internal ShownSession(SteamPlayer client, Player player, ITransportConnection connection)
            { Client = client; Player = player; Connection = connection; }
        }
    }
}
