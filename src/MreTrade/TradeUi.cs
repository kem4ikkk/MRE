using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using HarmonyLib;
using MreShared;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreTrade
{
    public sealed partial class MreTradePlugin
    {
        private const ushort TradeEffectId = 48622;
        private const short TradeEffectKey = 14822;
        private static readonly Guid TradeEffectGuid = new Guid("a986933995df4d29816efcc37f87b220");
        private sealed class Editor { internal Player Player; internal Items Projection, Previous; internal Dictionary<Item, TradeOffer> Copies; }
        private readonly Dictionary<ulong, Editor> editors = new Dictionary<ulong, Editor>();
        private readonly HashSet<ulong> modalOwned = new HashSet<ulong>();
        private Harmony uiHarmony;
        private EffectAsset tradeEffect;
        private bool uiReady;
        private DateTime uiRetryUtc;

        private void StartTradeUi()
        {
            try
            {
                tradeEffect = Assets.find(TradeEffectGuid) as EffectAsset;
                if (tradeEffect == null || tradeEffect.id != TradeEffectId) throw new InvalidOperationException("EffectAsset окна обмена не найден по GUID и ID.");
                uiHarmony = new Harmony("mre.trade.projection");
                TradeProjectionHooks.Install(uiHarmony);
                EffectManager.onEffectButtonClicked += TradeButton;
                uiReady = true;
                Logger.Log("[MRE TRADE] Тестовый интерфейс обмена включён.");
            }
            catch (Exception error)
            {
                if (uiHarmony != null) uiHarmony.UnpatchAll("mre.trade.projection");
                uiReady = false; tradeEffect = null;
                Logger.LogException(error, "[MRE TRADE] Интерфейс не включён; доступен текстовый обмен.");
            }
        }
        private void StopTradeUi()
        {
            EffectManager.onEffectButtonClicked -= TradeButton;
            foreach (ulong id in editors.Keys.ToArray()) ClearTradeUi(id);
            foreach (ulong id in modalOwned.ToArray()) ClearTradeUi(id);
            // При исключении закрытия удаляем только созданные нами копии до снятия guards.
            foreach (var editor in editors.Values) { if (editor.Projection != null) editor.Projection.items.Clear(); if (editor.Previous != null) editor.Previous.items.Clear(); }
            uiReady = false;
            if (uiHarmony != null) uiHarmony.UnpatchAll("mre.trade.projection");
        }
        private bool IsOwnEditor(UnturnedPlayer player)
        {
            Editor e; return player != null && editors.TryGetValue(player.CSteamID.m_SteamID, out e) && e.Player == player.Player && player.Player.inventory.isStoring && player.Player.inventory.items[PlayerInventory.STORAGE] == e.Projection;
        }
        private bool IsEditorOpen(ulong id) { return editors.ContainsKey(id); }
        private void ShowTradeUi(ulong id)
        {
            TradeSession s; var p = Online(id);
            if (!uiReady || p == null || !sessions.TryGetValue(id, out s) || !s.Accepted || IsEditorOpen(id) || !Allowed(p)) return;
            try
            {
                var connection = p.Player.channel.GetOwnerTransportConnection();
                EffectManager.SendUIEffect(tradeEffect, TradeEffectKey, connection, true);
                if (!p.Player.isPluginWidgetFlagActive(EPluginWidgetFlags.Modal)) { p.Player.enablePluginWidgetFlag(EPluginWidgetFlags.Modal); modalOwned.Add(id); }
                RenderTradeUi(p, s);
            }
            catch (Exception error) { Logger.LogException(error, "[MRE TRADE] Не удалось открыть интерфейс."); ClearTradeUi(id); }
        }
        private void HideTradeEffect(UnturnedPlayer p)
        {
            if (p == null || p.Player == null) return;
            EffectManager.ClearEffectByGuid(TradeEffectGuid, p.Player.channel.GetOwnerTransportConnection());
            if (modalOwned.Remove(p.CSteamID.m_SteamID)) p.Player.disablePluginWidgetFlag(EPluginWidgetFlags.Modal);
        }
        private void ClearTradeUi(ulong id)
        {
            Editor editor;
            if (editors.TryGetValue(id, out editor))
            {
                try
                {
                    if (editor.Player != null && editor.Player.inventory != null && editor.Player.inventory.isStoring && (editor.Player.inventory.items[PlayerInventory.STORAGE] == editor.Projection || editor.Player.inventory.items[PlayerInventory.STORAGE] == editor.Previous || (editor.Player.inventory.storage == null && !editor.Player.inventory.isStorageTrunk)))
                        editor.Player.inventory.closeStorageAndNotifyClient();
                    if (editor.Player != null && editor.Player.inventory != null && !editor.Player.inventory.isStoring && (editor.Player.inventory.items[PlayerInventory.STORAGE] == editor.Projection || editor.Player.inventory.items[PlayerInventory.STORAGE] == editor.Previous))
                    { editor.Player.inventory.updateItems(PlayerInventory.STORAGE, null); editor.Player.inventory.sendStorage(); }
                    if (editor.Player != null && editor.Player.inventory != null && (editor.Player.inventory.items[PlayerInventory.STORAGE] == editor.Projection || editor.Player.inventory.items[PlayerInventory.STORAGE] == editor.Previous))
                        throw new InvalidOperationException("Виртуальная страница не закрылась.");
                    editors.Remove(id);
                }
                catch (Exception error) { Logger.LogException(error, "[MRE TRADE] Проекция остаётся под защитой до отключения игрока."); return; }
            }
            var p = Online(id); if (p != null) HideTradeEffect(p); else modalOwned.Remove(id);
        }
        private void UpdateTradeUi()
        {
            if (!uiReady && Configuration.Instance.UiEnabled && Configuration.Instance.AssetsVerified && Level.isLoaded && DateTime.UtcNow >= uiRetryUtc)
            { uiRetryUtc = DateTime.UtcNow.AddSeconds(30); StartTradeUi(); }
            foreach (var pair in editors.ToArray())
            {
                var e = pair.Value;
                if (e.Player != null && e.Player.inventory != null && !e.Player.inventory.isStoring && (e.Player.inventory.items[PlayerInventory.STORAGE] == e.Projection || e.Player.inventory.items[PlayerInventory.STORAGE] == e.Previous))
                { ClearTradeUi(pair.Key); if (!editors.ContainsKey(pair.Key)) ShowTradeUi(pair.Key); continue; }
                if (e.Player == null || e.Player.inventory == null || (e.Player.inventory.items[PlayerInventory.STORAGE] != e.Projection && e.Player.inventory.items[PlayerInventory.STORAGE] != e.Previous))
                { editors.Remove(pair.Key); ShowTradeUi(pair.Key); }
            }
        }
        private void RefreshTradeUi(TradeSession s)
        {
            if (!uiReady || s == null || !s.Accepted) return;
            foreach (ulong id in new[] { s.A, s.B }) { var p = Online(id); if (p != null && !IsEditorOpen(id)) RenderTradeUi(p, s); }
        }
        private void RenderTradeUi(UnturnedPlayer p, TradeSession s)
        {
            var c = p.Player.channel.GetOwnerTransportConnection(); ulong id = p.CSteamID.m_SteamID;
            EffectManager.sendUIEffectText(TradeEffectKey, c, true, "TradePartner", "Обмен с " + SafeLabel(Online(s.Other(id)) == null ? "игроком" : Online(s.Other(id)).DisplayName));
            EffectManager.sendUIEffectText(TradeEffectKey, c, true, "TradeOwnOffer", "Ваше предложение");
            EffectManager.sendUIEffectText(TradeEffectKey, c, true, "TradePartnerOffer", "Предложение партнёра");
            EffectManager.sendUIEffectText(TradeEffectKey, c, true, "TradeStatus", "Вы: " + ((id == s.A ? s.ConfirmedA : s.ConfirmedB) ? "подтверждено" : "ожидание") + " / партнёр: " + ((id == s.A ? s.ConfirmedB : s.ConfirmedA) ? "подтверждено" : "ожидание"));
            for (int i = 0; i < 10; i++)
            {
                var own = s.Own(id); var other = s.Own(s.Other(id));
                EffectManager.sendUIEffectVisibility(TradeEffectKey, c, true, "TradeOwnIcon" + i, i < own.Count && own[i].Id == 363);
                EffectManager.sendUIEffectVisibility(TradeEffectKey, c, true, "TradeOtherIcon" + i, i < other.Count && other[i].Id == 363);
                EffectManager.sendUIEffectText(TradeEffectKey, c, true, "TradeOwn" + i, i < own.Count ? SafeLabel(ItemName(own[i].Id)) + " ×" + own[i].Amount + " / " + own[i].Quality + "%" : "+");
                EffectManager.sendUIEffectText(TradeEffectKey, c, true, "TradeOther" + i, i < other.Count ? SafeLabel(ItemName(other[i].Id)) + " ×" + other[i].Amount + " / " + other[i].Quality + "%" : "—");
            }
        }
        private static string SafeLabel(string text)
        {
            var result = new StringBuilder();
            foreach (char c in text ?? "")
            {
                var category = Char.GetUnicodeCategory(c);
                if (Char.IsControl(c) || category == UnicodeCategory.Format || c == '<' || c == '>') continue;
                result.Append(c); if (result.Length >= 32) break;
            }
            return result.Length == 0 ? "Предмет" : result.ToString();
        }
        private void TradeButton(Player player, string name)
        {
            if (!uiReady || player == null || player.channel == null || player.channel.owner == null) return;
            ulong id = player.channel.owner.playerID.steamID.m_SteamID; TradeSession s;
            var actor = Online(id);
            if (actor == null || actor.Player != player || !sessions.TryGetValue(id, out s) || !s.Accepted || s.Committing || IsEditorOpen(id) || !Allowed(actor) || !Allowed(Online(s.Other(id)))) return;
            if (name == "TradeConfirm") { Confirm(actor, s); return; }
            if (name == "TradeCancel") { Cancel(s, "Обмен отменён."); return; }
            if (name != null && name.StartsWith("TradeOwnSlot", StringComparison.Ordinal))
            {
                int index;
                if (Int32.TryParse(name.Substring("TradeOwnSlot".Length), out index) && index >= 0 && index < 10) OpenEditor(actor, s);
            }
        }
        private void OpenEditor(UnturnedPlayer actor, TradeSession s)
        {
            ulong id = actor.CSteamID.m_SteamID;
            if (!uiReady || actor.Player.inventory.isStoring || !Allowed(actor) || !Allowed(Online(s.Other(id)))) return;
            try
            {
                var editor = new Editor { Player = actor.Player };
                BuildProjection(editor, s.Own(id));
                HideTradeEffect(actor); editors.Add(id, editor);
                var inventory = actor.Player.inventory;
                inventory.isStoring = true; inventory.isStorageTrunk = false; inventory.storage = null;
                inventory.updateItems(PlayerInventory.STORAGE, editor.Projection); inventory.sendStorage();
            }
            catch (Exception error) { ClearTradeUi(id); Logger.LogException(error, "[MRE TRADE] Не удалось открыть выбор предметов."); Tell(id, "Не удалось открыть выбор предметов."); if (!editors.ContainsKey(id)) ShowTradeUi(id); }
        }
        private static void BuildProjection(Editor editor, IList<TradeOffer> offers)
        {
            var copies = new Dictionary<Item, TradeOffer>(); var items = new Items(PlayerInventory.STORAGE); items.resize(8, 8);
            foreach (var offer in offers)
            {
                var copy = new Item(offer.Id, offer.Amount, offer.Quality, (byte[])offer.State.Clone());
                if (!items.tryAddItem(copy, false)) throw new InvalidOperationException("Предложение не помещается в окне.");
                copies.Add(copy, offer);
            }
            editor.Projection = items; editor.Copies = copies;
        }
        internal bool ProjectionOpen(PlayerInventory inventory)
        {
            if (!uiReady || inventory == null || inventory.player == null || inventory.player.channel == null || inventory.player.channel.owner == null) return false;
            Editor e; return editors.TryGetValue(inventory.player.channel.owner.playerID.steamID.m_SteamID, out e) && e.Player == inventory.player;
        }
        internal bool IsTradeProjection(Items items) { return items != null && editors.Values.Any(e => object.ReferenceEquals(e.Projection, items) || object.ReferenceEquals(e.Previous, items)); }
        internal void ProjectionDrag(PlayerInventory inventory, byte sourcePage, byte sourceX, byte sourceY, byte targetPage)
        {
            if (!ProjectionOpen(inventory)) return;
            ulong id = inventory.player.channel.owner.playerID.steamID.m_SteamID; TradeSession s; Editor editor;
            if (!editors.TryGetValue(id, out editor) || !sessions.TryGetValue(id, out s)) return;
            var actor = Online(id);
            if (actor == null || !Allowed(actor) || !Allowed(Online(s.Other(id)))) { ClearTradeUi(id); Cancel(s, "Обмен отменён из-за состояния игрока."); return; }
            if (sourcePage == PlayerInventory.STORAGE && targetPage != PlayerInventory.STORAGE)
            {
                byte index = editor.Projection.getIndex(sourceX, sourceY); TradeOffer offer;
                if (index != byte.MaxValue && editor.Copies.TryGetValue(editor.Projection.getItem(index).item, out offer))
                    Modify(actor, s, "remove", new[] { "remove", offer.Page.ToString(), offer.X.ToString(), offer.Y.ToString() });
            }
            else if (targetPage == PlayerInventory.STORAGE && sourcePage >= PlayerInventory.SLOTS && sourcePage < PlayerInventory.STORAGE)
                Modify(actor, s, "offer", new[] { "offer", sourcePage.ToString(), sourceX.ToString(), sourceY.ToString() });
            try { editor.Previous = editor.Projection; BuildProjection(editor, s.Own(id)); inventory.updateItems(PlayerInventory.STORAGE, editor.Projection); inventory.sendStorage(); editor.Previous = null; }
            catch (Exception error) { Logger.LogException(error, "[MRE TRADE] Ошибка проекции; обмен отменён."); ClearTradeUi(id); Cancel(s, "Обмен отменён из-за ошибки интерфейса."); }
        }
    }
}
