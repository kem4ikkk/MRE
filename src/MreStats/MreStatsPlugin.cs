using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Rocket.API;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreStats
{
    public sealed class MreStatsConfiguration : IRocketPluginConfiguration
    {
        public string SeasonId;
        public void LoadDefaults() { SeasonId = "season-1"; }
    }
    public sealed class MreStatsPlugin : RocketPlugin<MreStatsConfiguration>
    {
        public static MreStatsPlugin Instance { get; private set; }
        private StatsStore store;
        private readonly HashSet<ulong> dead = new HashSet<ulong>();
        private readonly Dictionary<ulong, double> sessions = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, long> pendingSeconds = new Dictionary<ulong, long>();
        private double nextTick;
        private static double Now { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }
        protected override void Load()
        {
            store = new StatsStore(Path.Combine(Directory, "stats.bin"), Configuration.Instance.SeasonId);
            PlayerLife.onPlayerDied += OnDeath;
            PlayerLife.OnRevived_Global += OnRevived;
            Player.onPlayerStatIncremented += OnStat;
            Provider.onServerDisconnected += OnDisconnect;
            Provider.onServerConnected += OnConnect;
            if (Provider.clients != null) foreach (var p in Provider.clients) if (p != null) OnConnect(p.playerID.steamID);
            Instance = this;
            nextTick = Now;
            Logger.Log("[MRE Stats] Статистика включена; постройки недоступны.");
        }
        protected override void Unload()
        {
            PlayerLife.onPlayerDied -= OnDeath;
            PlayerLife.OnRevived_Global -= OnRevived;
            Player.onPlayerStatIncremented -= OnStat;
            Provider.onServerDisconnected -= OnDisconnect;
            Provider.onServerConnected -= OnConnect;
            foreach (ulong id in sessions.Keys.ToArray()) Accrue(id, Now);
            Flush();
            sessions.Clear(); dead.Clear(); Instance = null; store = null;
        }
        public StatsSnapshot GetSnapshot(ulong steamId) { return store == null || steamId == 0 ? null : store.Snapshot(steamId); }
        public StatsSnapshot[] GetTopKills(int limit) { return store == null ? new StatsSnapshot[0] : store.TopKills(limit); }
        public StatsSnapshot[] GetTop(TopCategory category, int limit) { return store == null ? new StatsSnapshot[0] : store.Top(category, limit); }
        private void OnStat(Player player, EPlayerStat stat)
        {
            if (stat != EPlayerStat.KILLS_ZOMBIES_NORMAL && stat != EPlayerStat.KILLS_ZOMBIES_MEGA) return;
            try { store.StageZombie(player.channel.owner.playerID.steamID.m_SteamID); }
            catch (Exception e) { Logger.LogException(e, "[MRE Stats] Не удалось записать убийство зомби; требуется сверка."); }
        }
        private void OnDeath(PlayerLife life, EDeathCause cause, ELimb limb, CSteamID instigator)
        {
            ulong victim = 0;
            try {
                victim = life.channel.owner.playerID.steamID.m_SteamID;
                if (!dead.Add(victim)) return;
                ulong killer = instigator.m_SteamID;
                string why = cause.ToString();
                // Ограниченный набор прямых причин: неоднозначные ловушки и кровотечение не засчитываем.
                if (why != "GUN" && why != "MELEE" && why != "PUNCH") killer = 0;
                if (!Provider.clients.Any(p => p != null && p.playerID.steamID.m_SteamID == killer)) killer = 0;
                store.StageDeath(victim, killer);
            } catch (Exception e) { if (victim != 0) dead.Remove(victim); Logger.LogException(e, "[MRE Stats] Не удалось записать смерть; требуется сверка."); }
        }
        private void OnRevived(PlayerLife life) { dead.Remove(life.channel.owner.playerID.steamID.m_SteamID); }
        private void OnConnect(CSteamID id) { if (id.m_SteamID != 0 && !sessions.ContainsKey(id.m_SteamID)) sessions.Add(id.m_SteamID, Now); }
        private void OnDisconnect(CSteamID id) { Accrue(id.m_SteamID, Now); sessions.Remove(id.m_SteamID); dead.Remove(id.m_SteamID); Flush(); }
        private void Accrue(ulong id, double now)
        {
            double since; if (!sessions.TryGetValue(id, out since)) return;
            long seconds = (long)(now - since); if (seconds <= 0) return;
            long pending; pendingSeconds.TryGetValue(id, out pending); pendingSeconds[id] = checked(pending + seconds); sessions[id] = since + seconds;
        }
        private void Flush()
        {
            try {
                if (pendingSeconds.Count != 0) { store.AddSecondsBatch(pendingSeconds); pendingSeconds.Clear(); }
                store.FlushPending();
            }
            catch (Exception e) { Logger.LogException(e, "[MRE Stats] Не удалось сохранить время; требуется сверка."); }
        }
        private void Update()
        {
            double now = Now; if (store == null || now < nextTick || Provider.clients == null) return;
            nextTick = now + 5;
            foreach (var p in Provider.clients) if (p != null && p.playerID.steamID.m_SteamID != 0) store.ObserveName(p.playerID.steamID.m_SteamID, p.playerID.characterName);
            foreach (ulong id in sessions.Keys.ToArray()) Accrue(id, now);
            Flush();
        }
    }
}
