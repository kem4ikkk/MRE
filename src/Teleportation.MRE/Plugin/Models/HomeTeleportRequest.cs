using RestoreMonarchy.Teleportation.Utils;
using Rocket.Core.Utils;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Diagnostics;

namespace RestoreMonarchy.Teleportation.Models
{
    // MoreHomes retains ownership of beds and storage; this owns only a cancellable transfer.
    public sealed class HomeTeleportRequest
    {
        private readonly TeleportationPlugin plugin;
        private readonly UnturnedPlayer player;
        private readonly Func<bool> validateDestination;
        private readonly Func<bool> performTeleport;
        private long startedAt;

        public HomeTeleportRequest(TeleportationPlugin plugin, UnturnedPlayer player, string homeName,
            Func<bool> validateDestination, Func<bool> performTeleport, object owner)
        {
            this.plugin = plugin;
            this.player = player;
            this.validateDestination = validateDestination;
            this.performTeleport = performTeleport;
            HomeName = homeName;
            Owner = owner;
        }

        public CSteamID PlayerId => player.CSteamID;
        public string HomeName { get; private set; }
        public object Owner { get; private set; }
        public bool IsCanceled { get; private set; }
        public bool IsExecuting { get; private set; }
        public DateTime? ExecuteAtUtc { get; private set; }
        public double DelaySeconds { get; private set; }
        public double RemainingSeconds => IsExecuting && !IsCanceled
            ? Math.Max(0, DelaySeconds - (Stopwatch.GetTimestamp() - startedAt) / (double)Stopwatch.Frequency)
            : 0;

        public bool Execute(double delay)
        {
            if (IsExecuting || IsCanceled) return false;
            if (!Validate()) return false;
            if (plugin.HomeRequests.ContainsKey(PlayerId) || plugin.TPRequests.Exists(x =>
                !x.IsCanceled && x.IsExecuting && (x.Sender == PlayerId || x.Target == PlayerId))) return false;

            DelaySeconds = double.IsNaN(delay) || double.IsInfinity(delay) ? 5 : Math.Max(0, delay);
            startedAt = Stopwatch.GetTimestamp();
            ExecuteAtUtc = DateTime.UtcNow.AddSeconds(DelaySeconds);
            IsExecuting = true;
            plugin.HomeRequests.Add(PlayerId, this);
            plugin.NotifyHomeRequestChanged(PlayerId);

            TaskDispatcher.QueueOnMainThread(() =>
            {
                if (IsCanceled) return;
                try
                {
                    if (Validate()) performTeleport();
                }
                finally
                {
                    Cancel();
                }
            }, (float)DelaySeconds);
            return true;
        }

        private bool Validate()
        {
            if (IsCanceled || player == null || player.Player == null || player.Dead ||
                TeleportationPlugin.Instance != plugin || PlayerTool.getPlayer(PlayerId) != player.Player)
                return false;
            if (plugin.IsPlayerInCombat(PlayerId))
            {
                plugin.SendMessageToPlayer(player, "TPAWhileCombatYou");
                return false;
            }
            if (plugin.IsPlayerInRaid(PlayerId))
            {
                plugin.SendMessageToPlayer(player, "TPAWhileRaidYou");
                return false;
            }
            return validateDestination != null && performTeleport != null && validateDestination();
        }

        public void Cancel()
        {
            if (IsCanceled) return;
            IsCanceled = true;
            HomeTeleportRequest current;
            if (plugin.HomeRequests != null && plugin.HomeRequests.TryGetValue(PlayerId, out current) &&
                ReferenceEquals(current, this)) plugin.HomeRequests.Remove(PlayerId);
            plugin.NotifyHomeRequestChanged(PlayerId);
        }
    }
}
