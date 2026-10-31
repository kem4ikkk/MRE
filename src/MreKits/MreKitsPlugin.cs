using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using MreShared;
using Rocket.Core;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using RestoreMonarchy.Teleportation;
using RestoreMonarchy.Teleportation.Utils;
using SDG.Unturned;
using Logger = Rocket.Core.Logging.Logger;

namespace MreKits
{
    public sealed class MreKitsPlugin : RocketPlugin
    {
        public static MreKitsPlugin Instance { get; private set; }
        private KitLedger ledger;
        private int gameThread;
        private static readonly ushort[] StartItems = { 353, 16, 348, 348, 348, 348, 348, 95, 95, 13, 13, 14, 14 };
        private static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);
        protected override void Load() { gameThread = Thread.CurrentThread.ManagedThreadId; ledger = new KitLedger(Path.Combine(Directory, "claims")); Instance = this; }
        protected override void Unload() { Instance = null; ledger = null; }

        public void Claim(UnturnedPlayer player)
        {
            if (Thread.CurrentThread.ManagedThreadId != gameThread) throw new InvalidOperationException("Набор вызывается только на игровом потоке.");
            if (player == null || player.Player == null || !Provider.clients.Any(p => p != null && p.player == player.Player && p.playerID.steamID == player.CSteamID)) return;
            if (!R.Permissions.HasPermission(player, new List<string> { "mre.kit" })) { MreChat.Say(player, "MRE: нет права на набор."); return; }
            ulong id = player.CSteamID.m_SteamID;
            try
            {
                if (!Provider.isServer || !Level.isLoaded || player.Player == null || player.Player.life.isDead)
                { MreChat.Say(player, "MRE: набор доступен только живому игроку на загруженном сервере."); return; }
                var tpa = TeleportationPlugin.Instance;
                if (tpa == null || tpa.CombatPlayers == null || tpa.RaidPlayers == null || tpa.Configuration == null || tpa.Configuration.Instance == null || tpa.Configuration.Instance.AllowCombat || tpa.Configuration.Instance.AllowRaid)
                { MreChat.Say(player, "MRE: проверка боя и рейда недоступна; попробуйте позже."); return; }
                if (tpa.IsPlayerInCombat(player.CSteamID) || tpa.IsPlayerInRaid(player.CSteamID))
                { MreChat.Say(player, "MRE: набор недоступен во время боя или рейда."); return; }
                if (ledger.HasPending(id)) { MreChat.Say(player, "MRE: предыдущая выдача проверяется администрацией."); return; }
                DateTime now = DateTime.UtcNow; DateTime next = ledger.LastClaim(id).Add(Cooldown);
                if (now < next) { MreChat.Say(player, "MRE: набор будет доступен через " + Math.Ceiling((next - now).TotalMinutes) + " мин."); return; }
                var delivery = new KitDelivery(player.Player);
                if (!delivery.Preflight(StartItems)) { MreChat.Say(player, "MRE: освободите место для всего набора и попробуйте снова."); return; }
                ledger.Reserve(id);
                if (!delivery.Deliver())
                {
                    MreChat.Say(player, "MRE: выдача прервана; обратитесь к администрации."); return;
                }
                ledger.Complete(id, DateTime.UtcNow);
                MreChat.Say(player, "MRE: стартовый набор выдан. Следующий — через час.");
            }
            catch (Exception error)
            {
                Logger.LogException(error, "[MRE] Выдача набора не завершена; проверьте журнал выдач.");
                MreChat.Say(player, "MRE: выдача не завершена; обратитесь к администрации.");
            }
        }
    }
}
