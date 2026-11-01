using RestoreMonarchy.Teleportation.Utils;
using Rocket.Core.Utils;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Diagnostics;

namespace RestoreMonarchy.Teleportation.Models
{
    public class TPARequest
    {
        private TeleportationPlugin pluginInstance => TeleportationPlugin.Instance;

        public TPARequest(CSteamID sender, CSteamID target)
        {
            Sender = sender;
            Target = target;

            double duration = pluginInstance.Configuration.Instance.TPADuration;
            if (duration == 0)
            {
                duration = 30;
            }
            ExpireDate = DateTime.Now.AddSeconds(duration);

            SenderPlayer = UnturnedPlayer.FromCSteamID(sender);
            TargetPlayer = UnturnedPlayer.FromCSteamID(target);
        }

        public TPARequest() { }

        public CSteamID Sender { get; set; }
        public CSteamID Target { get; set; }
        public DateTime ExpireDate { get; set; }
        public bool IsCanceled { get; private set; }

        public UnturnedPlayer SenderPlayer { get; set; }
        public UnturnedPlayer TargetPlayer { get; set; }

        public bool IsExecuting { get; private set; }
        public bool IsExpired => !IsExecuting && ExpireDate < DateTime.Now;
        private long executionStartedAt;
        public DateTime? ExecuteAtUtc { get; private set; }
        public double DelaySeconds { get; private set; }
        // HUD uses the same accepted request and a monotonic clock, without a second teleport timer.
        public double RemainingSeconds => IsExecuting && !IsCanceled
            ? Math.Max(0, DelaySeconds - (Stopwatch.GetTimestamp() - executionStartedAt) / (double)Stopwatch.Frequency)
            : 0;

        public void Execute(double delay)
        {
            // MRE: accepted requests remain tracked until completion, and cannot be queued twice.
            if (IsExecuting || IsCanceled)
            {
                return;
            }
            if (!Validate())
            {
                Cancel();
                return;
            }
            delay = double.IsNaN(delay) || double.IsInfinity(delay) ? 5 : Math.Max(0, delay);
            DelaySeconds = delay;
            executionStartedAt = Stopwatch.GetTimestamp();
            ExecuteAtUtc = DateTime.UtcNow.AddSeconds(delay);
            IsExecuting = true;
            pluginInstance.NotifyRequestChanged(this);

            if (delay > 0)
            {
                pluginInstance.SendMessageToPlayer(SenderPlayer, "TPADelay", TargetPlayer.DisplayName, delay);
                if (pluginInstance.Configuration.Instance.CancelOnMove)
                {
                    pluginInstance.MovementDetector.AddPlayer(SenderPlayer.Player, () =>
                    {
                        pluginInstance.SendMessageToPlayer(SenderPlayer, "TPACanceledYouMoved");
                        pluginInstance.SendMessageToPlayer(TargetPlayer, "TPACanceledSenderMoved", SenderPlayer.DisplayName);
                        Cancel();
                    });
                }
            }

            TaskDispatcher.QueueOnMainThread(() =>
            {
                if (IsCanceled)
                {
                    return;
                }

                pluginInstance.MovementDetector.RemovePlayer(SenderPlayer.Player);

                if (!Validate(true))
                {
                    Cancel();
                    return;
                }

                try
                {
                    if (pluginInstance.Configuration.Instance.UseUnsafeTeleport)
                    {
                        SenderPlayer.Player.teleportToLocationUnsafe(TargetPlayer.Position, TargetPlayer.Rotation);
                    } else
                    {
                        SenderPlayer.Teleport(TargetPlayer);
                    }

                    pluginInstance.SendMessageToPlayer(SenderPlayer, "TPASuccess", TargetPlayer.DisplayName);
                }
                finally
                {
                    // Даже исключение игрового API не оставляет HUD и запрос в вечном отсчёте.
                    Cancel();
                }

            }, (float)delay);
        }

        public bool Validate(bool isFinal = false)
        {
            var pluginInstance = TeleportationPlugin.Instance;

            // Compare the actual player objects: disconnect/reconnect must not resurrect an old request.
            if (IsCanceled || SenderPlayer == null || TargetPlayer == null ||
                SenderPlayer.Player == null || TargetPlayer.Player == null ||
                PlayerTool.getPlayer(Sender) != SenderPlayer.Player ||
                PlayerTool.getPlayer(Target) != TargetPlayer.Player)
            {
                return false;
            }

            if (pluginInstance.IsPlayerInCombat(SenderPlayer.CSteamID))
            {
                pluginInstance.SendMessageToPlayer(SenderPlayer, "TPAWhileCombatYou");
                if (isFinal)
                {
                    pluginInstance.SendMessageToPlayer(TargetPlayer, "TPAWhileCombat", SenderPlayer.DisplayName);
                }

                return false;
            }
            else if (pluginInstance.IsPlayerInRaid(SenderPlayer.CSteamID))
            {
                pluginInstance.SendMessageToPlayer(SenderPlayer, "TPAWhileRaidYou");
                if (isFinal)
                {
                    pluginInstance.SendMessageToPlayer(TargetPlayer, "TPAWhileRaid", SenderPlayer.DisplayName);
                }

                return false;
            }
            else if (pluginInstance.IsPlayerInCombat(TargetPlayer.CSteamID))
            {
                pluginInstance.SendMessageToPlayer(SenderPlayer, "TPAWhileCombat", TargetPlayer.DisplayName);
                pluginInstance.SendMessageToPlayer(TargetPlayer, "TPAWhileCombatYou");
                return false;
            }
            else if (pluginInstance.IsPlayerInRaid(TargetPlayer.CSteamID))
            {
                pluginInstance.SendMessageToPlayer(SenderPlayer, "TPAWhileRaid", TargetPlayer.DisplayName);
                pluginInstance.SendMessageToPlayer(TargetPlayer, "TPAWhileRaidYou");
                return false;
            }
            else if (SenderPlayer.Dead || TargetPlayer.Dead)
            {
                pluginInstance.SendMessageToPlayer(SenderPlayer, "TPADead", TargetPlayer.DisplayName);
                if (isFinal)
                {
                    pluginInstance.SendMessageToPlayer(TargetPlayer, "TPADead", SenderPlayer.DisplayName);
                }

                return false;
            }
            else if (pluginInstance.IsPlayerInCave(TargetPlayer))
            {
                pluginInstance.SendMessageToPlayer(SenderPlayer, "TPACave", TargetPlayer.DisplayName);
                if (isFinal)
                {
                    pluginInstance.SendMessageToPlayer(TargetPlayer, "TPACaveYou", SenderPlayer.DisplayName);
                }

                return false;
            }
            else if (SenderPlayer.IsInVehicle)
            {
                pluginInstance.SendMessageToPlayer(SenderPlayer, "TPAVehicleYou");
                if (isFinal)
                {
                    pluginInstance.SendMessageToPlayer(TargetPlayer, "TPAVehicle", SenderPlayer.DisplayName);
                }

                return false;
            }

            return true;
        }

        public void Cancel()
        {
            if (IsCanceled) return;
            IsCanceled = true;
            var plugin = TeleportationPlugin.Instance;
            if (plugin == null)
            {
                return;
            }
            if (plugin.MovementDetector != null && SenderPlayer != null && SenderPlayer.Player != null)
            {
                plugin.MovementDetector.RemovePlayer(SenderPlayer.Player);
            }
            plugin.TPRequests?.Remove(this);
            plugin.NotifyRequestChanged(this);
        }
    }
}
