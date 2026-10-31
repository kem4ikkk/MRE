using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreInterface
{
    public sealed class KillPreview
    {
        public string Token { get; internal set; }
        public ulong TargetId { get; internal set; }
        public string TargetName { get; internal set; }
        public DateTime ExpiresUtc { get; internal set; }
    }
    public sealed partial class MreInterfacePlugin
    {
        private sealed class KillTarget { public Player Player; public KillPreview Preview; }
        private sealed class FreezeState { public UnturnedPlayer Actor; public SteamPlayer Target; public float Speed, Jump; }
        private readonly ConfirmationStore<KillTarget> killsPending = new ConfirmationStore<KillTarget>();
        private readonly Dictionary<ulong, FreezeState> freezes = new Dictionary<ulong, FreezeState>();
        private void OnEnterVehicleRequested(Player player, InteractableVehicle vehicle, ref bool shouldAllow)
        {
            FreezeState state;
            if (player != null && freezes.TryGetValue(player.channel.owner.playerID.steamID.m_SteamID, out state) && ReferenceEquals(state.Target.player, player)) shouldAllow = false;
        }
        public KillPreview PreviewKill(UnturnedPlayer actor, ulong targetId)
        {
            RequireGameThread(); if (!CanExecuteAdmin(actor, "kill")) throw new UnauthorizedAccessException("Нет права mre.admin.kill.");
            killsPending.Cancel(actor.CSteamID.m_SteamID);
            var target = Provider.clients.FirstOrDefault(p => p != null && p.player != null && p.playerID.steamID.m_SteamID == targetId);
            if (target == null || target.player.life.isDead || target.player == actor.Player) return null;
            var preview = new KillPreview { TargetId = targetId, TargetName = target.playerID.characterName, ExpiresUtc = DateTime.UtcNow.AddSeconds(15) };
            preview.Token = killsPending.Issue(actor.CSteamID.m_SteamID, new KillTarget { Player = target.player, Preview = preview }, DateTime.UtcNow);
            return preview;
        }
        private static string DescribeKill(KillPreview preview) { return preview == null ? "Нужен другой живой подключенный игрок." : "Будет убит игрок " + preview.TargetName + " [" + preview.TargetId + "]. Подтвердить в 15 секунд: /admin kill confirm " + preview.Token; }
        public string ConfirmKill(UnturnedPlayer actor, string token)
        {
            RequireGameThread();
            if (!CanExecuteAdmin(actor, "kill")) { if (actor != null) killsPending.Cancel(actor.CSteamID.m_SteamID); return "Нет права на это действие."; }
            KillTarget target;
            if (!killsPending.TryConsume(actor.CSteamID.m_SteamID, token, DateTime.UtcNow, out target)) return "Подтверждение истекло или неверно.";
            var current = Provider.clients.FirstOrDefault(p => p != null && p.player == target.Player && p.playerID.steamID.m_SteamID == target.Preview.TargetId);
            if (current == null || current.player.life.isDead) return "Игрок вышел, сменил сессию или уже умер.";
            EPlayerKill result;
            current.player.life.askDamage(101, Vector3.up * 101f, EDeathCause.KILL, ELimb.SKULL, actor.CSteamID, out result);
            Audit(actor, "kill", target.Preview.TargetId);
            return current.player.life.isDead ? "Игрок убит штатным серверным обработчиком." : "Штатный обработчик отклонил урон; игрок не убит.";
        }
        public string SetFreeze(UnturnedPlayer actor, ulong targetId, bool enabled)
        {
            RequireGameThread(); if (!IsOnline(actor)) return "Игрок уже вышел.";
            FreezeState own;
            bool ownCleanup = !enabled && freezes.TryGetValue(targetId, out own) && IsOnline(own.Actor) && own.Actor.Player == actor.Player;
            if (!ownCleanup && !CanExecuteAdmin(actor, "freeze")) return "Нет права mre.admin.freeze.";
            if (!enabled) { bool existed = freezes.ContainsKey(targetId); StopFreeze(targetId); Audit(actor, "freeze-off", targetId); return existed ? "Заморозка снята; прежние множители движения восстановлены." : "Игрок не заморожен этим модулем."; }
            var target = Provider.clients.FirstOrDefault(p => p != null && p.player != null && p.playerID.steamID.m_SteamID == targetId);
            if (target == null || target.player.life.isDead) return "Нужен живой подключенный игрок.";
            if (target.player == actor.Player) return "Нельзя заморозить себя.";
            if (freezes.ContainsKey(targetId)) return "Игрок уже заморожен; повторная команда снимет заморозку.";
            var state = new FreezeState { Actor = actor, Target = target, Speed = target.player.movement.pluginSpeedMultiplier, Jump = target.player.movement.pluginJumpMultiplier };
            freezes[targetId] = state;
            try { target.player.movement.forceRemoveFromVehicle(); target.player.movement.sendPluginSpeedMultiplier(0); target.player.movement.sendPluginJumpMultiplier(0); }
            catch { StopFreeze(targetId); throw; }
            Audit(actor, "freeze-on", targetId); return "Движение и прыжок игрока заморожены. Повторная /admin freeze <игрок> снимает заморозку.";
        }
        private void StopFreeze(ulong id)
        {
            FreezeState state; if (!freezes.TryGetValue(id, out state)) return; freezes.Remove(id);
            try { if (state.Target.player != null) { if (state.Target.player.movement.pluginSpeedMultiplier == 0) state.Target.player.movement.sendPluginSpeedMultiplier(state.Speed); if (state.Target.player.movement.pluginJumpMultiplier == 0) state.Target.player.movement.sendPluginJumpMultiplier(state.Jump); } }
            catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка восстановления движения игрока."); }
        }
        private void CleanupFreezesFor(ulong id) { foreach (var entry in freezes.Where(p => p.Key == id || p.Value.Actor.CSteamID.m_SteamID == id).ToArray()) StopFreeze(entry.Key); }
        private void TickFreezes()
        {
            foreach (var entry in freezes.ToArray())
            {
                var state = entry.Value;
                if (!IsOnline(state.Actor) || !CanExecuteAdmin(state.Actor, "freeze") || !Provider.clients.Any(p => ReferenceEquals(p, state.Target)) || state.Target.player == null || state.Target.player.life.isDead) { StopFreeze(entry.Key); continue; }
                // Если соседний плагин поменял множители, сохраняем его новое значение для возврата.
                if (state.Target.player.movement.pluginSpeedMultiplier != 0) state.Speed = state.Target.player.movement.pluginSpeedMultiplier;
                if (state.Target.player.movement.pluginJumpMultiplier != 0) state.Jump = state.Target.player.movement.pluginJumpMultiplier;
                state.Target.player.movement.sendPluginSpeedMultiplier(0); state.Target.player.movement.sendPluginJumpMultiplier(0);
            }
        }
    }
}
