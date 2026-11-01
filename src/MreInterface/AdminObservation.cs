using MreShared;
using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreInterface
{
    public sealed partial class MreInterfacePlugin
    {
        private sealed class Observation
        {
            public UnturnedPlayer Actor;
            public ulong Target;
            public Player TargetPlayer;
            public Vector3 ReturnPosition;
            public float ReturnYaw;
            public StaffModeRestoreState Original;
        }
        private readonly Dictionary<ulong, Observation> observations = new Dictionary<ulong, Observation>();
        private readonly Dictionary<ulong, UnturnedPlayer> freecamGrants = new Dictionary<ulong, UnturnedPlayer>();
        private readonly Dictionary<ulong, UnturnedPlayer> godGrants = new Dictionary<ulong, UnturnedPlayer>();
        private readonly Dictionary<ulong, UnturnedPlayer> vanishGrants = new Dictionary<ulong, UnturnedPlayer>();
        private DateTime nextObservationTickUtc;

        public string SetFreecamPermission(UnturnedPlayer actor, bool enabled)
        {
            RequireGameThread();
            if (!IsOnline(actor)) return "Игрок уже вышел.";
            ulong id = actor.CSteamID.m_SteamID;
            if (!enabled)
            {
                if (freecamGrants.Remove(id)) actor.Player.look.sendFreecamAllowed(false);
                return "Наше разрешение freecam снято. Выход из камеры: Shift+F1. У native администратора право freecam остаётся штатным.";
            }
            if (!CanExecuteAdmin(actor, "fly")) return "Нет права mre.admin.fly.";
            if (!actor.Player.look.canUseFreecam)
            {
                actor.Player.look.sendFreecamAllowed(true);
                freecamGrants[id] = actor;
            }
            Audit(actor, "fly", id);
            return "Летающая камера: Shift+F1 включить/выключить; WASD — движение, Q/E — вверх/вниз, колесо — скорость. Тело остаётся на месте; клавиши могут быть переназначены.";
        }

        public string SetStaffMode(UnturnedPlayer actor, string mode, bool enabled)
        {
            RequireGameThread();
            if (mode != "god" && mode != "vanish") return "Неизвестный режим.";
            if (!IsOnline(actor)) return "Игрок уже вышел.";
            ulong id = actor.CSteamID.m_SteamID;
            var grants = mode == "god" ? godGrants : vanishGrants;
            // Отозванное право не мешает выключить именно наш временный режим.
            if ((enabled || !grants.ContainsKey(id)) && !CanExecuteAdmin(actor, mode)) return "Нет права на этот режим.";
            if (observations.ContainsKey(id)) return "Сначала заверши наблюдение: /admin spectate off.";
            bool current = mode == "god" ? actor.GodMode : actor.VanishMode;
            if (enabled && !current) grants[id] = actor;
            if (!enabled) grants.Remove(id);
            if (mode == "god") { if (actor.GodMode != enabled) actor.GodMode = enabled; }
            else if (actor.VanishMode != enabled) actor.VanishMode = enabled;
            Audit(actor, mode + (enabled ? "-on" : "-off"), id);
            return mode + (enabled ? " включён." : " выключен.");
        }

        public string StartObservation(UnturnedPlayer actor, ulong targetId)
        {
            RequireGameThread();
            if (!CanExecuteAdmin(actor, "spectate")) return "Нет права mre.admin.spectate.";
            var target = Provider.clients.FirstOrDefault(p => p != null && p.player != null && p.playerID.steamID.m_SteamID == targetId);
            if (target == null || target.player.life.isDead || actor.Player.life.isDead) return "Для наблюдения нужны два живых подключённых игрока.";
            if (actor.CSteamID.m_SteamID == targetId) return "Нельзя наблюдать за собой.";
            StopObservationInternal(actor.CSteamID.m_SteamID, true);
            var state = new Observation { Actor = actor, Target = targetId, TargetPlayer = target.player, ReturnPosition = actor.Position, ReturnYaw = actor.Player.look.yaw, Original = new StaffModeRestoreState(actor.GodMode, actor.VanishMode) };
            observations[actor.CSteamID.m_SteamID] = state;
            try
            {
                if (!actor.GodMode) actor.GodMode = true;
                if (!actor.VanishMode) actor.VanishMode = true;
                if (!TryFollow(actor, target.player)) { StopObservationInternal(actor.CSteamID.m_SteamID, true); return "Рядом с целью нет безопасной точки. Наблюдение отменено."; }
            }
            catch
            {
                StopObservationInternal(actor.CSteamID.m_SteamID, true); throw;
            }
            Audit(actor, "spectate-start", targetId);
            return "Наблюдение: сервер следует рядом с целью; временно включены штатные Vanish и GodMode. Это не вид из глаз игрока. /admin spectate off — закончить и вернуться.";
        }

        // Cleanup всегда разрешён для самого actor, даже после отзыва права.
        public string StopObservation(UnturnedPlayer actor)
        {
            RequireGameThread();
            if (!IsOnline(actor)) return "Игрок уже вышел.";
            bool existed = observations.ContainsKey(actor.CSteamID.m_SteamID);
            StopObservationInternal(actor.CSteamID.m_SteamID, true);
            return existed ? "Наблюдение завершено; прежние режимы восстановлены." : "Наблюдение не включено.";
        }

        private static bool TryFollow(UnturnedPlayer actor, Player target)
        {
            Vector3 backward = Quaternion.Euler(0, target.look.yaw, 0) * Vector3.back;
            Vector3[] candidates = { target.transform.position + backward * 4f + Vector3.up, target.transform.position - backward * 4f + Vector3.up, target.transform.position + Vector3.right * 4f + Vector3.up, target.transform.position + Vector3.left * 4f + Vector3.up };
            foreach (Vector3 position in candidates)
                if (actor.Player.teleportToLocation(position, target.look.yaw)) return true;
            return false;
        }

        private void StopObservationInternal(ulong id, bool returnPosition)
        {
            Observation state;
            if (!observations.TryGetValue(id, out state)) return;
            observations.Remove(id);
            try
            {
                if (state.Actor.Player != null)
                {
                    if (state.Actor.GodMode != state.Original.God) state.Actor.GodMode = state.Original.God;
                    if (state.Actor.VanishMode != state.Original.Vanish) state.Actor.VanishMode = state.Original.Vanish;
                    if (returnPosition && IsOnline(state.Actor) && !state.Actor.Player.life.isDead && !state.Actor.Player.teleportToLocation(state.ReturnPosition, state.ReturnYaw))
                        MreChat.Say(state.Actor, "MRE: наблюдение выключено; возврат заблокирован препятствием. Используй /home или /admin tp.");
                }
            }
            catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка восстановления состояния наблюдателя."); }
        }

        private void TickStaffModes()
        {
            if (DateTime.UtcNow < nextObservationTickUtc) return;
            nextObservationTickUtc = DateTime.UtcNow.AddSeconds(0.75);
            foreach (var pair in observations.ToArray())
            {
                var state = pair.Value;
                var target = Provider.clients.FirstOrDefault(p => p != null && p.player != null && p.playerID.steamID.m_SteamID == state.Target);
                if (!IsOnline(state.Actor) || !CanExecuteAdmin(state.Actor, "spectate") || state.Actor.Player.life.isDead || target == null || !ReferenceEquals(target.player, state.TargetPlayer) || target.player.life.isDead)
                { StopObservationInternal(pair.Key, true); continue; }
                try
                {
                    if (!state.Actor.GodMode) state.Actor.GodMode = true;
                    if (!state.Actor.VanishMode) state.Actor.VanishMode = true;
                    if (!TryFollow(state.Actor, target.player)) { StopObservationInternal(pair.Key, true); MreChat.Say(state.Actor, "MRE: наблюдение остановлено, безопасная точка недоступна."); }
                }
                catch (Exception error) { StopObservationInternal(pair.Key, true); Logger.LogException(error, "[MRE] Наблюдение остановлено из-за ошибки."); }
            }
            RevokeModeGrants(freecamGrants, "fly", player => player.Player.look.sendFreecamAllowed(false));
            RevokeModeGrants(godGrants, "god", player => player.GodMode = false);
            RevokeModeGrants(vanishGrants, "vanish", player => player.VanishMode = false);
        }

        private void RevokeModeGrants(Dictionary<ulong, UnturnedPlayer> grants, string permissionAction, Action<UnturnedPlayer> restore)
        {
            foreach (var pair in grants.ToArray())
                if (!IsOnline(pair.Value) || !CanExecuteAdmin(pair.Value, permissionAction))
                {
                    grants.Remove(pair.Key);
                    Observation observation;
                    if (observations.TryGetValue(pair.Key, out observation))
                    {
                        observation.Original.RevokeOwnGrant(permissionAction);
                        if (permissionAction == "god" || permissionAction == "vanish") continue; // Этот режим пока нужен действующему наблюдению.
                    }
                    try { if (pair.Value.Player != null) restore(pair.Value); } catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка снятия режима администратора."); }
                }
        }

        private void CleanupStaff(ulong id, bool returnPosition)
        {
            StopObservationInternal(id, returnPosition);
            removals.Cancel(id);
            killsPending.Cancel(id);
            CleanupAdditionalGrants(id);
            RestoreGrant(freecamGrants, id, p => p.Player.look.sendFreecamAllowed(false));
            RestoreGrant(godGrants, id, p => p.GodMode = false);
            RestoreGrant(vanishGrants, id, p => p.VanishMode = false);
        }

        private static void RestoreGrant(Dictionary<ulong, UnturnedPlayer> grants, ulong id, Action<UnturnedPlayer> restore)
        {
            UnturnedPlayer actor;
            if (!grants.TryGetValue(id, out actor)) return;
            grants.Remove(id);
            try { if (actor.Player != null) restore(actor); } catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка очистки режима администратора."); }
        }
    }
}
