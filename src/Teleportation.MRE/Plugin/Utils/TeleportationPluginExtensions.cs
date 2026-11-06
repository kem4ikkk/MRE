using System;
using Rocket.Core.Utils;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System.Timers;
using UnityEngine;

namespace RestoreMonarchy.Teleportation.Utils
{
    public static class TeleportationPluginExtensions
    {
        public static void StartPlayerCombat(this TeleportationPlugin plugin, CSteamID steamID)
        {
            StartBlock(plugin, steamID, plugin.CombatPlayers, plugin.CombatDeadlines,
                plugin.Configuration.Instance.CombatDuration, "CombatStart", true);
        }

        public static void StopPlayerCombat(this TeleportationPlugin plugin, CSteamID steamID)
        {
            StopBlock(plugin, steamID, plugin.CombatPlayers, plugin.CombatDeadlines, "CombatExpire");
        }

        public static void StartPlayerRaid(this TeleportationPlugin plugin, CSteamID steamID)
        {
            StartBlock(plugin, steamID, plugin.RaidPlayers, plugin.RaidDeadlines,
                plugin.Configuration.Instance.RaidDuration, "RaidStart", false);
        }

        public static void StopPlayerRaid(this TeleportationPlugin plugin, CSteamID steamID)
        {
            StopBlock(plugin, steamID, plugin.RaidPlayers, plugin.RaidDeadlines, "RaidExpire");
        }

        private static void StartBlock(TeleportationPlugin plugin, CSteamID player,
            System.Collections.Generic.Dictionary<CSteamID, Timer> timers,
            System.Collections.Generic.Dictionary<CSteamID, long> deadlines,
            double duration, string startMessage, bool combat)
        {
            Timer previous;
            bool alreadyBlocked = timers.TryGetValue(player, out previous);
            // A new timer identity prevents an already queued expiration from clearing a renewed block.
            var timer = new Timer(duration * 1000) { AutoReset = false };
            timer.Elapsed += (sender, e) => TaskDispatcher.QueueOnMainThread(() =>
            {
                Timer current;
                if (timers.TryGetValue(player, out current) && ReferenceEquals(current, timer))
                {
                    if (combat) plugin.StopPlayerCombat(player);
                    else plugin.StopPlayerRaid(player);
                }
            });
            if (alreadyBlocked) previous.Dispose();
            timers[player] = timer;
            deadlines[player] = BlockDeadline.AfterSeconds(duration);
            timer.Start();
            if (!alreadyBlocked) plugin.SendMessageToPlayer(player, startMessage);
        }

        private static void StopBlock(TeleportationPlugin plugin, CSteamID player,
            System.Collections.Generic.Dictionary<CSteamID, Timer> timers,
            System.Collections.Generic.Dictionary<CSteamID, long> deadlines, string expireMessage)
        {
            Timer timer;
            if (timers != null && timers.TryGetValue(player, out timer))
            {
                timer.Dispose();
                timers.Remove(player);
                deadlines.Remove(player);
                plugin.SendMessageToPlayer(player, expireMessage);
            }
        }

        public static bool IsPlayerInRaid(this TeleportationPlugin plugin, CSteamID steamID) =>
            plugin.GetRaidRemainingSeconds(steamID) > 0;

        public static bool IsPlayerInCombat(this TeleportationPlugin plugin, CSteamID steamID) =>
            plugin.GetCombatRemainingSeconds(steamID) > 0;

        public static bool IsPlayerInCave(this TeleportationPlugin plugin, UnturnedPlayer player)
        {
            if (!plugin.Configuration.Instance.AllowCave)
            {
                Vector3 point = player.Position;

                float height = LevelGround.getHeight(point);
                if (height > point.y)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
