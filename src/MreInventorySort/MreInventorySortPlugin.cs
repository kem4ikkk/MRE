using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreInventorySort
{
    public sealed class InventorySortConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled { get; set; }
        public void LoadDefaults() { Enabled = false; }
    }

    public sealed class MreInventorySortPlugin : RocketPlugin<InventorySortConfiguration>
    {
        public static MreInventorySortPlugin Instance { get; private set; }
        private readonly HashSet<ulong> active = new HashSet<ulong>();
        private readonly Dictionary<ulong, DateTime> nextUse = new Dictionary<ulong, DateTime>();
        private int gameThread;
        private bool compatible, faulted;

        protected override void Load()
        {
            gameThread = Thread.CurrentThread.ManagedThreadId;
            Instance = this;
            compatible = NativeSortSurface.SendAdd != null && NativeSortSurface.SendRemove != null && NativeSortSurface.Increment != null;
            using (var stream = File.OpenRead(typeof(PlayerInventory).Assembly.Location))
            using (var hash = SHA256.Create())
                compatible &= BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == InventorySortBuild.GameSha256;
            Logger.Log("[MRE SORT] Загружен. Enabled=" + Configuration.Instance.Enabled + ", API=" + compatible + ". До независимой проверки отключён по умолчанию.");
        }
        protected override void Unload() { active.Clear(); nextUse.Clear(); Instance = null; }

        private void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != gameThread) throw new InvalidOperationException("Сортировка доступна только на игровом потоке.");
        }
        private bool Live(UnturnedPlayer actor)
        {
            return actor != null && actor.Player != null && !actor.Dead && PlayerTool.getPlayer(actor.CSteamID) == actor.Player &&
                Provider.isServer && Dedicator.IsDedicatedServer && Level.isLoaded;
        }
        private static bool EmptyHand(UnturnedPlayer actor)
        {
            return actor.Player.equipment != null && !actor.Player.equipment.HasValidUseable &&
                actor.Player.equipment.asset == null && !actor.Player.equipment.isBusy;
        }
        private bool StorageValid(UnturnedPlayer actor, InteractableStorage expected)
        {
            if (!Live(actor) || expected == null || expected.GetType() != typeof(InteractableStorage)) return false;
            var inventory = actor.Player.inventory;
            if (!inventory.isStoring || inventory.isStorageTrunk || inventory.storage != expected || !expected.isOpen ||
                expected.opener != actor.Player || !expected.isActiveAndEnabled || !expected.checkInteractable() || expected.isDisplay ||
                expected.onStateRebuilt != null || !ReferenceEquals(inventory.items[PlayerInventory.STORAGE], expected.items) ||
                !expected.checkRot(actor.CSteamID, actor.Player.quests.groupID)) return false;
            if (BarricadeManager.FindBarricadeByRootTransform(expected.transform) == null) return false;
            if ((expected.transform.position - actor.Position).sqrMagnitude > 400f) return false;
            if (Physics.Linecast(actor.Player.look.getEyesPosition(), expected.transform.position,
                RayMasks.BLOCK_BARRICADE_INTERACT_LOS, QueryTriggerInteraction.Ignore)) return false;
            return Provider.clients.Count(client => client.player != null && client.player.inventory != null &&
                client.player.inventory.isStoring && client.player.inventory.storage == expected) == 1;
        }

        public bool CanSortInventory(UnturnedPlayer actor)
        {
            RequireThread();
            return Configuration.Instance.Enabled && compatible && !faulted && Live(actor) && actor.HasPermission("mre.sort") && EmptyHand(actor);
        }
        public bool CanSortStorage(UnturnedPlayer actor)
        {
            RequireThread();
            return Configuration.Instance.Enabled && compatible && !faulted && Live(actor) && actor.HasPermission("mre.sort") &&
                StorageValid(actor, actor.Player.inventory.storage);
        }

        public string Sort(UnturnedPlayer actor, bool storage)
        {
            RequireThread();
            if (!Configuration.Instance.Enabled) return "Сортировка ещё проходит независимую проверку и пока отключена.";
            if (!compatible || faulted) return "Безопасная сортировка временно недоступна. Обратитесь к администрации.";
            if (!Live(actor) || !actor.HasPermission("mre.sort")) return "Нет доступа к сортировке.";
            if (!storage && !EmptyHand(actor)) return "Для сортировки инвентаря освободите руку и завершите действие.";
            var player = actor.Player;
            var inventory = player.inventory;
            var selected = storage ? inventory.storage : null;
            if (storage && !StorageValid(actor, selected)) return "Откройте разрешённое обычное хранилище рядом с собой. Сортировка багажника, витрины и виртуального склада не поддерживается.";
            var id = actor.CSteamID.m_SteamID;
            DateTime until;
            if (nextUse.TryGetValue(id, out until) && until > DateTime.UtcNow) return "Подождите две секунды между сортировками.";
            if (!active.Add(id)) return "Сортировка уже выполняется.";
            nextUse[id] = DateTime.UtcNow.AddSeconds(2);
            try
            {
                Func<bool> valid = () => Live(actor) && actor.Player == player && actor.HasPermission("mre.sort") &&
                    (storage ? StorageValid(actor, selected) : EmptyHand(actor));
                var surfaces = new List<ISortSurface>();
                if (storage) surfaces.Add(new NativeSortSurface(inventory, PlayerInventory.STORAGE, valid, selected));
                else for (byte page = PlayerInventory.SLOTS; page < PlayerInventory.STORAGE; page++)
                    if (inventory.items[page] != null) surfaces.Add(new NativeSortSurface(inventory, page, valid));
                var result = SortTransaction.Execute(surfaces.ToArray());
                if (result.NeedsReview)
                {
                    faulted = true;
                    Logger.LogError("[MRE SORT] Требуется ручная сверка после неудачной сортировки игрока " + id + ". Новые сортировки заблокированы; вещи не создавались и не удалялись.");
                }
                return result.Message;
            }
            catch (Exception error)
            {
                Logger.LogException(error, "[MRE SORT] Сортировка не выполнена.");
                return "Сортировка не выполнена. Сообщите администрации.";
            }
            finally { active.Remove(id); }
        }
    }
}
