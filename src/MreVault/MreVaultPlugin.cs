using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MreShared;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using RestoreMonarchy.Teleportation;
using RestoreMonarchy.Teleportation.Utils;
using SDG.Unturned;
using UnityEngine;

namespace MreVault
{
    public sealed class MreVaultPlugin : RocketPlugin
    {
        internal static MreVaultPlugin Instance;
        private VaultSessionStore store;
        private Harmony harmony;
        private readonly Dictionary<ulong, Session> sessions = new Dictionary<ulong, Session>();
        private sealed class Session { internal UnturnedPlayer Player; internal Items Items; }
        protected override void Load()
        {
            store = new VaultSessionStore(Path.Combine(Directory, "storage"));
            harmony = new Harmony("mre.vault.guard");
            harmony.PatchAll(typeof(MreVaultPlugin).Assembly);
            Instance = this;
            ChatManager.onChatted += OnChatted;
        }
        protected override void Unload()
        {
            ChatManager.onChatted -= OnChatted;
            foreach (var id in sessions.Keys.ToArray()) Close(id);
            Instance = null;
            if (harmony != null) harmony.UnpatchAll("mre.vault.guard");
        }
        private void OnChatted(SteamPlayer steamPlayer, EChatMode mode, ref Color color, ref bool rich, string text, ref bool visible)
        {
            if (!visible || !VaultAlias.IsMatch(text)) return;
            visible = false;
            if (steamPlayer == null || steamPlayer.player == null) return;
            try {
                var player = UnturnedPlayer.FromPlayer(steamPlayer.player);
                if (player != null) Open(player); // Open повторно проверяет права и ограничения боя/рейда.
            } catch (Exception e) {
                Rocket.Core.Logging.Logger.LogException(e, "[MRE] Не удалось обработать .ы.");
            }
        }
        private bool Allowed(UnturnedPlayer player)
        {
            var tpa = TeleportationPlugin.Instance;
            return player != null && player.Player != null && player.HasPermission("mre.vault") && Provider.clients.Any(c => c != null && c.player == player.Player && c.playerID.steamID == player.CSteamID) && !player.Player.life.isDead && tpa != null && tpa.CombatPlayers != null && tpa.RaidPlayers != null && tpa.Configuration != null && tpa.Configuration.Instance != null && !tpa.Configuration.Instance.AllowCombat && !tpa.Configuration.Instance.AllowRaid && !tpa.IsPlayerInCombat(player.CSteamID) && !tpa.IsPlayerInRaid(player.CSteamID);
        }
        internal bool AllowTransfer(PlayerInventory inventory)
        {
            if (inventory == null || inventory.player == null) return false;
            var player = UnturnedPlayer.FromPlayer(inventory.player);
            if (player == null || !sessions.ContainsKey(player.CSteamID.m_SteamID)) return true;
            return Allowed(player);
        }
        internal void Open(UnturnedPlayer player)
        {
            if (!Allowed(player)) { MreChat.Say(player, "MRE: хранилище недоступно во время боя или рейда либо проверка недоступна."); return; }
            ulong id = player.CSteamID.m_SteamID;
            if (sessions.ContainsKey(id) || player.Player.inventory.isStoring) { MreChat.Say(player, "MRE: сначала закройте открытое хранилище."); return; }
            try
            {
                if (store.Locked(id)) { MreChat.Say(player, "MRE: хранилище заблокировано до проверки администрацией."); return; }
                var stored = store.Read(id);
                var items = new Items(PlayerInventory.STORAGE); items.resize(4, 4);
                foreach (var x in stored)
                {
                    var asset = Assets.find(EAssetType.ITEM, x.Id) as ItemAsset;
                    if (asset == null) throw new InvalidDataException("Предмет хранилища не найден.");
                    var item = new Item(x.Id, x.Amount, x.Quality, x.State);
                    if (!items.checkSpaceEmpty(x.X, x.Y, asset.size_x, asset.size_y, x.Rotation)) throw new InvalidDataException("Предметы хранилища не помещаются.");
                    items.addItem(x.X, x.Y, x.Rotation, item);
                }
                store.Begin(id);
                sessions.Add(id, new Session { Player = player, Items = items });
                // openTrunk помечает контейнер багажником: клиент не открывает окно.
                // Используем тот же порядок, что openStorage, без привязки к чужой постройке.
                var inventory = player.Player.inventory;
                inventory.isStoring = true;
                inventory.isStorageTrunk = false;
                inventory.storage = null;
                inventory.updateItems(PlayerInventory.STORAGE, items);
                inventory.sendStorage();
            }
            catch (Exception e) { Rocket.Core.Logging.Logger.LogException(e, "[MRE] Ошибка открытия хранилища."); MreChat.Say(player, "MRE: хранилище недоступно; обратитесь к администрации."); }
        }
        private void Update()
        {
            foreach (var pair in sessions.ToArray())
            {
                var p = pair.Value.Player;
                if (p == null || p.Player == null || !p.Player.inventory.isStoring || !object.ReferenceEquals(p.Player.inventory.items[PlayerInventory.STORAGE], pair.Value.Items) || !Allowed(p)) Close(pair.Key);
            }
        }
        private void Close(ulong id)
        {
            Session s; if (!sessions.TryGetValue(id, out s)) return;
            sessions.Remove(id);
            try
            {
                var inv = s.Player.Player.inventory;
                if (inv.isStoring && object.ReferenceEquals(inv.items[PlayerInventory.STORAGE], s.Items)) inv.closeStorageAndNotifyClient();
                var result = new List<VaultStoredItem>();
                for (byte i = 0; i < s.Items.getItemCount(); i++)
                {
                    var j = s.Items.getItem(i);
                    result.Add(new VaultStoredItem { Id = j.item.id, X = j.x, Y = j.y, Rotation = j.rot, Amount = j.item.amount, Quality = j.item.quality, State = (byte[])j.item.state.Clone() });
                }
                inv.save();
                store.Write(id, result);
                store.End(id);
            }
            catch (Exception e) { Rocket.Core.Logging.Logger.LogException(e, "[MRE] Хранилище требует ручной сверки; pending сохранён."); }
        }
        [HarmonyPatch(typeof(PlayerInventory), "ReceiveDragItem")]
        private static class DragGuard { private static bool Prefix(PlayerInventory __instance) { return Instance == null || Instance.AllowTransfer(__instance); } }
        [HarmonyPatch(typeof(PlayerInventory), "ReceiveDropItem")]
        private static class DropGuard
        {
            private static bool Prefix(PlayerInventory __instance, byte page)
            {
                if (Instance == null || __instance == null || __instance.player == null) return true;
                var p = UnturnedPlayer.FromPlayer(__instance.player);
                if (p == null || !Instance.sessions.ContainsKey(p.CSteamID.m_SteamID)) return true;
                return page != PlayerInventory.STORAGE && Instance.AllowTransfer(__instance);
            }
        }
        [HarmonyPatch(typeof(PlayerInventory), "ReceiveSwapItem")]
        private static class SwapGuard { private static bool Prefix(PlayerInventory __instance) { return Instance == null || Instance.AllowTransfer(__instance); } }
    }
}
