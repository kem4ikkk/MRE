using RestoreMonarchy.MoreHomes.Helpers;
using RestoreMonarchy.MoreHomes.Models;
using RestoreMonarchy.Teleportation;
using Rocket.API;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RestoreMonarchy.MoreHomes.Commands
{
    public class HomeCommand : IRocketCommand
    {
        public void Execute(IRocketPlayer caller, string[] command)
        {
            var plugin = MoreHomesPlugin.Instance;
            var player = (UnturnedPlayer)caller;
            // Fail closed: a missing combat/raid provider must not enable an unrestricted HOME.
            var teleportation = TeleportationPlugin.Instance;
            if (teleportation == null)
            {
                plugin.SendMessageToPlayer(player, "HomeUnavailable");
                return;
            }
            if (string.Equals(command.ElementAtOrDefault(0), "cancel", StringComparison.OrdinalIgnoreCase))
            {
                var active = teleportation.GetHomeUiRequest(player.CSteamID);
                if (active != null)
                {
                    active.Cancel();
                    plugin.SendMessageToPlayer(player, "HomeCanceled");
                    return;
                }
            }

            var home = HomesHelper.GetPlayerHome(player.CSteamID, command.ElementAtOrDefault(0));
            if (home == null)
            {
                plugin.SendMessageToPlayer(player, "NoHome");
                return;
            }
            DateTime cooldown;
            if (plugin.PlayerCooldowns.TryGetValue(caller.Id, out cooldown) && cooldown > DateTime.Now)
            {
                plugin.SendMessageToPlayer(caller, "HomeCooldown", Math.Ceiling((cooldown - DateTime.Now).TotalSeconds));
                return;
            }

            // MRE uses one shared five-second lifecycle for every HOME privilege level.
            const double delay = 5;
            bool started = teleportation.TryStartHomeTeleport(player, home.Name, delay,
                () => ValidateDestination(plugin, player, home),
                () =>
                {
                    bool success = player.Player.teleportToLocation(home.LivePosition +
                        new Vector3(0f, plugin.Configuration.Instance.TeleportHeight, 0f), player.Rotation);
                    plugin.SendMessageToPlayer(player, success ? "HomeSuccess" : "HomeTeleportationFailed", home.Name);
                    return success;
                }, plugin);
            if (!started) return;

            // Only this successfully scheduled operation starts cooldown; cancel/failure never refunds it.
            plugin.PlayerCooldowns[caller.Id] = DateTime.Now.AddSeconds(VipHelper.GetPlayerHomeCooldown(caller.Id));
            plugin.SendMessageToPlayer(caller, "HomeDelayWarn", delay);
        }

        private static bool ValidateDestination(MoreHomesPlugin plugin, UnturnedPlayer player, PlayerHome home)
        {
            if (MoreHomesPlugin.Instance != plugin) return false;
            if (home.InteractableBed == null || !home.InteractableBed.isActiveAndEnabled ||
                home.InteractableBed.owner != player.CSteamID ||
                HomesHelper.GetPlayerHome(player.CSteamID, home.InteractableBed) != home)
            {
                HomesHelper.RemoveHome(player.CSteamID, home);
                plugin.SendMessageToPlayer(player, "BedDestroyed");
                return false;
            }
            if (player.Stance == EPlayerStance.DRIVING)
            {
                plugin.SendMessageToPlayer(player, "WhileDriving");
                return false;
            }
            if (plugin.Configuration.Instance.BlockUnderground && LevelGround.getHeight(home.LivePosition) > home.LivePosition.y)
            {
                plugin.SendMessageToPlayer(player, "CantTeleportToBedUnderground", home.Name);
                return false;
            }
            return true;
        }

        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "home";
        public string Help => "Teleports player to their bed";
        public string Syntax => "[name/cancel]";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();
    }
}
