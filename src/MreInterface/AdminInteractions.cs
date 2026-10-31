using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.Unturned.Player;
using Rocket.Core;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreInterface
{
    public sealed partial class MreInterfacePlugin
    {
        private sealed class StorageGrant { public UnturnedPlayer Actor; public InteractableStorage Storage; }
        private readonly Dictionary<ulong, UnturnedPlayer> workzoneGrants = new Dictionary<ulong, UnturnedPlayer>();
        private readonly Dictionary<ulong, UnturnedPlayer> statsGrants = new Dictionary<ulong, UnturnedPlayer>();
        private readonly Dictionary<ulong, StorageGrant> storageGrants = new Dictionary<ulong, StorageGrant>();
        private DateTime nextAdditionalToolsTick;
        public string RunBodyFlight(UnturnedPlayer actor, bool? enabled)
        {
            RequireGameThread(); if (!IsOnline(actor)) return "Игрок уже вышел.";
            if (enabled != false && !CanExecuteAdmin(actor, "fly")) return "Нет права mre.admin.fly.";
            if (R.Commands.GetCommand("afly") == null) return "Модуль полета тела MreFlight не установлен; свободная камера доступна через /admin freecam.";
            string command = enabled.HasValue ? (enabled.Value ? "afly on" : "afly off") : "afly";
            // Только фиксированная команда, от имени исходного игрока, через обычный менеджер прав.
            return R.Commands.Execute(actor, command) ? "Команда передана модулю MreFlight; результат указан его сообщением." : "Модуль полета отклонил команду.";
        }
        public string SetWorkzonePermission(UnturnedPlayer actor, bool enabled) { return SetNativePermission(actor, "workzone", enabled); }
        public string SetStatsPermission(UnturnedPlayer actor, bool enabled) { return SetNativePermission(actor, "stats", enabled); }
        private string SetNativePermission(UnturnedPlayer actor, string mode, bool enabled)
        {
            RequireGameThread(); if (!IsOnline(actor)) return "Игрок уже вышел.";
            var grants = mode == "workzone" ? workzoneGrants : statsGrants;
            ulong id = actor.CSteamID.m_SteamID;
            Action<bool> send = value => { if (mode == "workzone") actor.Player.look.sendWorkzoneAllowed(value); else actor.Player.look.sendSpecStatsAllowed(value); };
            if (!enabled) { if (grants.Remove(id)) send(false); return "Наше разрешение " + mode + " снято; штатное право native администратора сохраняется."; }
            if (!CanExecuteAdmin(actor, mode)) return "Нет права на этот режим.";
            bool allowed = mode == "workzone" ? actor.Player.look.canUseWorkzone : actor.Player.look.canUseSpecStats;
            if (!allowed) { send(true); grants[id] = actor; }
            Audit(actor, mode + "-on", id);
            return mode == "workzone" ? "Редактирование построек: Shift+F6 в третьем лице. Режим включается клиентом." : "Статистика наблюдения: Shift+F7. Режим включается клиентом.";
        }
        private static BarricadeDrop FindAimedBarricade(UnturnedPlayer actor)
        {
            if (actor.Player.life.isDead) return null;
            var aim = actor.Player.look.aim;
            var hit = DamageTool.raycast(new Ray(aim.position, aim.forward), 6f, RayMasks.DAMAGE_SERVER | RayMasks.VEHICLE | RayMasks.PLAYER | RayMasks.ENEMY, actor.Player);
            return hit == null || hit.transform == null ? null : BarricadeManager.FindBarricadeByRootTransform(hit.transform);
        }
        public string ToggleAimedDoor(UnturnedPlayer actor)
        {
            RequireGameThread(); if (!CanExecuteAdmin(actor, "door")) return "Нет права mre.admin.door.";
            var drop = FindAimedBarricade(actor); var door = drop == null ? null : drop.interactable as InteractableDoor;
            if (door == null) return "Наведи прицел на дверь в пределах 6 метров.";
            if (!door.isOpenable) return "Дверь еще движется; повтори позже.";
            if (!BarricadeManager.ServerSetDoorOpen(door, !door.isOpen)) return "Дверь не найдена в регионе сервера.";
            Audit(actor, "door-toggle", drop.instanceID); return "Дверь переключена штатным серверным обработчиком.";
        }
        public string OpenAimedStorage(UnturnedPlayer actor)
        {
            RequireGameThread(); if (!CanExecuteAdmin(actor, "storage")) return "Нет права mre.admin.storage.";
            var drop = FindAimedBarricade(actor); var storage = drop == null ? null : drop.interactable as InteractableStorage;
            if (storage == null || storage.items == null) return "Наведи прицел на контейнер в пределах 6 метров.";
            if (storage.isOpen) return storage.opener == actor.Player ? "Этот контейнер уже открыт." : "Контейнер занят другим игроком.";
            actor.Player.inventory.openStorage(storage);
            storageGrants[actor.CSteamID.m_SteamID] = new StorageGrant { Actor = actor, Storage = storage };
            Audit(actor, "storage-open", drop.instanceID);
            return "Контейнер открыт. Это настоящий контейнер; перемещение предметов изменяет его содержимое.";
        }
        private void CloseOwnStorage(ulong id)
        {
            StorageGrant grant; if (!storageGrants.TryGetValue(id, out grant)) return; storageGrants.Remove(id);
            try { if (grant.Actor.Player != null && grant.Actor.Player.inventory.storage == grant.Storage) grant.Actor.Player.inventory.closeStorageAndNotifyClient(); }
            catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка закрытия административного доступа к контейнеру."); }
        }
        private void CleanupAdditionalGrants(ulong id)
        {
            RestoreGrant(workzoneGrants, id, p => p.Player.look.sendWorkzoneAllowed(false));
            RestoreGrant(statsGrants, id, p => p.Player.look.sendSpecStatsAllowed(false));
            CloseOwnStorage(id);
        }
        private void TickAdditionalTools()
        {
            if (DateTime.UtcNow < nextAdditionalToolsTick) return;
            nextAdditionalToolsTick = DateTime.UtcNow.AddSeconds(0.75);
            RevokeModeGrants(workzoneGrants, "workzone", player => player.Player.look.sendWorkzoneAllowed(false));
            RevokeModeGrants(statsGrants, "stats", player => player.Player.look.sendSpecStatsAllowed(false));
            foreach (var entry in storageGrants.ToArray())
                if (!IsOnline(entry.Value.Actor) || !CanExecuteAdmin(entry.Value.Actor, "storage") || entry.Value.Storage == null || entry.Value.Actor.Player.inventory.storage != entry.Value.Storage) CloseOwnStorage(entry.Key);
            TickFreezes();
        }
    }
}
