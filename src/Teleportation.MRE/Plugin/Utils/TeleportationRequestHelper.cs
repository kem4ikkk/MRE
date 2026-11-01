using RestoreMonarchy.Teleportation.Models;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using Steamworks;
using System;
using System.Linq;

namespace RestoreMonarchy.Teleportation.Utils
{
    public static class TeleportationRequestHelper
    {
        public static TPARequest GetUiRequest(this TeleportationPlugin plugin, CSteamID player)
        {
            if (plugin == null || plugin.TPRequests == null) return null;
            var active = plugin.TPRequests.Where(x => !x.IsCanceled && !x.IsExpired);
            return active.FirstOrDefault(x => x.IsExecuting && (x.Sender == player || x.Target == player))
                ?? active.FirstOrDefault(x => x.Target == player)
                ?? active.FirstOrDefault(x => x.Sender == player);
        }

        // Отмена собственной активной операции не требует сохранения права её запуска.
        // Порядок HOME -> TPA совпадает с единой карточкой HUD.
        public static void HandlePluginUiAction(this TeleportationPlugin plugin, UnturnedPlayer caller, bool accept)
        {
            var home = plugin.GetHomeUiRequest(caller.CSteamID);
            if (home != null)
            {
                if (!accept)
                {
                    plugin.SendMessageToPlayer(caller, "HomeCanceled");
                    home.Cancel();
                }
                return;
            }
            if (!caller.HasPermission("tpa")) return;
            plugin.HandleTPAUiAction(caller, accept);
        }

        // Вызывается только после проверки живого игрока и permission tpa в Plugin Key обработчике.
        public static void HandleTPAUiAction(this TeleportationPlugin plugin, UnturnedPlayer caller, bool accept)
        {
            var request = plugin.GetUiRequest(caller.CSteamID);
            if (request == null) return;
            if (accept)
            {
                if (request.Target == caller.CSteamID && !request.IsExecuting) plugin.AcceptTPARequest(caller);
            }
            else
            {
                if (request.Target == caller.CSteamID)
                    plugin.SendMessageToPlayer(caller, "TPADenied", request.SenderPlayer.DisplayName);
                else
                    plugin.SendMessageToPlayer(caller, "TPACanceled", request.TargetPlayer.DisplayName);
                request.Cancel();
            }
        }

        private static bool HasExecutingRequest(TeleportationPlugin plugin, CSteamID player, TPARequest except = null)
        {
            return plugin.GetHomeUiRequest(player) != null || plugin.TPRequests.Any(x => x != except && !x.IsCanceled && x.IsExecuting &&
                (x.Sender == player || x.Target == player));
        }

        public static void SendTPARequest(this TeleportationPlugin pluginInstance, UnturnedPlayer sender, UnturnedPlayer target)
        {
            foreach (var expiredRequest in pluginInstance.TPRequests.Where(x => x.IsExpired).ToList())
            {
                expiredRequest.Cancel();
            }
            if (sender.Id == target.Id)
            {
                pluginInstance.SendMessageToPlayer(sender, "TPAYourself");
                return;
            }

            if (HasExecutingRequest(pluginInstance, sender.CSteamID) || HasExecutingRequest(pluginInstance, target.CSteamID))
            {
                pluginInstance.SendMessageToPlayer(sender, "TPABusy");
                return;
            }

            if (pluginInstance.Cooldowns.TryGetValue(sender.CSteamID, out DateTime lastUse))
            {
                double secondsElapsed = (DateTime.Now - lastUse).TotalSeconds;
                double timeLeft = Math.Round(pluginInstance.Configuration.Instance.TPACooldown - secondsElapsed);
                if (secondsElapsed < pluginInstance.Configuration.Instance.TPACooldown)
                {
                    pluginInstance.SendMessageToPlayer(sender, "TPACooldown", timeLeft.ToString("N0"));
                    return;
                }
            }

            if (pluginInstance.TPRequests.Exists(x => x.Sender == sender.CSteamID && !x.IsCanceled))
            {
                pluginInstance.SendMessageToPlayer(sender, "TPADuplicate");
                return;
            }

            var request = new TPARequest(sender.CSteamID, target.CSteamID);
            if (!request.Validate())
            {
                return;
            }

            pluginInstance.TPRequests.Add(request);
            pluginInstance.Cooldowns[sender.CSteamID] = DateTime.Now;

            pluginInstance.SendMessageToPlayer(sender, "TPASent", target.DisplayName);
            pluginInstance.SendMessageToPlayer(target, "TPAReceive", sender.DisplayName);
            pluginInstance.NotifyRequestChanged(request);
        }

        public static void AcceptTPARequest(this TeleportationPlugin pluginInstance, UnturnedPlayer caller)
        {
            // Remove all expired TP requests
            foreach (var expiredRequest in pluginInstance.TPRequests.Where(x => x.IsExpired).ToList())
            {
                expiredRequest.Cancel();
            }

            var request = pluginInstance.TPRequests.FirstOrDefault(x => x.Target == caller.CSteamID);
            if (request == null)
            {
                pluginInstance.SendMessageToPlayer(caller, "TPANoRequest");
                return;
            }

            if (request.IsExecuting)
            {
                return;
            }
            if (HasExecutingRequest(pluginInstance, request.Sender, request) || HasExecutingRequest(pluginInstance, request.Target, request))
            {
                pluginInstance.SendMessageToPlayer(caller, "TPABusy");
                return;
            }
            if (!request.Validate())
            {
                request.Cancel();
                return;
            }

            pluginInstance.SendMessageToPlayer(caller, "TPAAccepted", request.SenderPlayer.CharacterName);
            request.Execute(pluginInstance.Configuration.Instance.TPADelay);
        }

        public static void CancelTPARequest(this TeleportationPlugin pluginInstance, UnturnedPlayer caller)
        {
            var request = pluginInstance.TPRequests.FirstOrDefault(x => x.Sender == caller.CSteamID);
            if (request != null)
            {
                pluginInstance.SendMessageToPlayer(caller, "TPACanceled", request.TargetPlayer.DisplayName);
                request.Cancel();
            } else
            {
                pluginInstance.SendMessageToPlayer(caller, "TPANoSentRequest");
            }
        }

        public static void DenyTPARequest(this TeleportationPlugin pluginInstance, UnturnedPlayer caller)
        {
            var request = pluginInstance.TPRequests.FirstOrDefault(x => x.Target == caller.CSteamID);
            if (request != null)
            {
                pluginInstance.SendMessageToPlayer(caller, "TPADenied", request.SenderPlayer.DisplayName);
                request.Cancel();
            }
            else
            {
                pluginInstance.SendMessageToPlayer(caller, "TPANoRequest");
            }
        }

        public static void ClearPlayerRequests(this TeleportationPlugin plugin, CSteamID steamID)
        {
            foreach (var request in plugin.TPRequests.Where(x => x.Sender == steamID || x.Target == steamID).ToList())
            {
                request.Cancel();
            }
        }
    }
}
