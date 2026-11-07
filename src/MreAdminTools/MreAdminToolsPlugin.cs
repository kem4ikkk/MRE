using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rocket.Core;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreAdminTools
{
    public sealed class MreAdminToolsPlugin : RocketPlugin
    {
        private int gameThreadId;
        public static MreAdminToolsPlugin Instance { get; private set; }

        protected override void Load()
        {
            gameThreadId = Thread.CurrentThread.ManagedThreadId;
            Instance = this;
            Logger.Log("[MRE] MreAdminTools загружен: /give и /veh.");
        }

        protected override void Unload()
        {
            if (Instance == this) Instance = null;
        }

        private void RequireGameThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != gameThreadId)
                throw new InvalidOperationException("MreAdminTools API вызывается только с главного игрового потока.");
        }

        private void Authorize(UnturnedPlayer actor, string permission)
        {
            RequireGameThread();
            if (Instance != this || !IsOnline(actor))
                throw new InvalidOperationException("Администратор должен быть подключен к серверу.");
            if (!actor.IsAdmin && !R.Permissions.HasPermission(actor, new List<string> { permission }))
                throw new UnauthorizedAccessException("Недостаточно прав для этого действия.");
        }

        private static bool IsOnline(UnturnedPlayer player)
        {
            return player != null && player.Player != null && Provider.clients.Any(client => client != null &&
                client.playerID.steamID == player.CSteamID && object.ReferenceEquals(client.player, player.Player));
        }

        private static SteamPlayer FindOnlineTarget(ulong steamId)
        {
            if (steamId == 0) return null;
            return Provider.clients.FirstOrDefault(client => client != null && client.player != null &&
                client.playerID.steamID.m_SteamID == steamId);
        }

        /// <summary>Выдаёт один стек предмета подключённому игроку, если для него есть место.</summary>
        public string GiveItem(UnturnedPlayer actor, ulong targetId, ushort itemId, byte amount)
        {
            Authorize(actor, "mre.admin.give");
            if (itemId == 0) return "ID предмета должен быть больше нуля.";
            SteamPlayer target = FindOnlineTarget(targetId);
            if (target == null) return "Игрок с указанным SteamID64 не подключён.";
            try
            {
                ItemAsset asset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
                if (asset == null) return "Предмет с таким ID не найден.";
                if (amount < 1 || amount > asset.amount)
                    return "Количество должно быть от 1 до максимального стака предмета (" + asset.amount + ").";

                PlayerInventory inventory = target.player.inventory;
                Items destination = null;
                byte destinationPage = 0, x = 0, y = 0, rotation = 0;
                for (byte page = PlayerInventory.SLOTS; page < PlayerInventory.STORAGE; page++)
                {
                    Items items = inventory.items[page];
                    if (items == null || !items.tryFindSpace(asset.size_x, asset.size_y, out x, out y, out rotation)) continue;
                    destination = items;
                    destinationPage = page;
                    break;
                }
                if (destination == null) return "В личном инвентаре игрока нет места. Предмет не выдан.";

                var item = new Item(asset, EItemOrigin.ADMIN);
                item.amount = amount;
                item.quality = 100;
                if (!inventory.tryAddItem(item, x, y, destinationPage, rotation))
                    return "Инвентарь изменился до выдачи. Предмет не выдан; освободите место и повторите.";

                Logger.Log("[MRE ADMIN] give actor=" + actor.CSteamID.m_SteamID + " target=" + targetId +
                    " item=" + itemId + " amount=" + amount);
                return "Выдан предмет " + itemId + " ×" + amount + " игроку " + targetId + ".";
            }
            catch (Exception error)
            {
                Logger.LogException(error, "[MRE ADMIN] Ошибка выдачи предмета.");
                return "Не удалось выдать предмет. Проверьте инвентарь игрока и сообщите администратору.";
            }
        }

        /// <summary>Создаёт транспорт рядом с живым администратором штатным серверным инструментом игры.</summary>
        public string SpawnVehicle(UnturnedPlayer actor, ushort vehicleId)
        {
            Authorize(actor, "mre.admin.veh");
            if (actor.Dead || actor.Player.life == null || actor.Player.life.isDead)
                return "Для создания транспорта администратор должен быть жив.";
            if (vehicleId == 0) return "ID транспорта должен быть больше нуля.";
            try
            {
                Asset asset = Assets.find(EAssetType.VEHICLE, vehicleId);
                if (asset == null) return "Транспорт с таким ID не найден.";

                int before = VehicleManager.vehicles == null ? -1 : VehicleManager.vehicles.Count;
                VehicleTool.SpawnVehicleForPlayer(actor.Player, asset);
                if (before < 0 || VehicleManager.vehicles == null || VehicleManager.vehicles.Count <= before)
                    return "Сервер не смог создать транспорт рядом с вами.";
                Logger.Log("[MRE ADMIN] vehicle actor=" + actor.CSteamID.m_SteamID + " vehicle=" + vehicleId);
                return "Транспорт " + vehicleId + " создан рядом с вами.";
            }
            catch (Exception error)
            {
                Logger.LogException(error, "[MRE ADMIN] Ошибка создания транспорта.");
                return "Не удалось создать транспорт. Повторите попытку позже.";
            }
        }
    }
}
