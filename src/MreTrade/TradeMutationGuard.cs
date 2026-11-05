using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SDG.Unturned;
using UnityEngine;

namespace MreTrade
{
    // Узкая защита синхронной критической секции; произвольные сторонние
    // плагины и прямые изменения игровых полей она не покрывает.
    internal sealed class TradeMutationGuard : IDisposable
    {
        internal static TradeMutationGuard Active;
        private readonly List<TradeOffer> offers;
        private Items allowedPage;
        private Item allowedItem;
        private bool allowedUsed;
        internal bool Interference;
        internal TradeMutationGuard(IEnumerable<TradeOffer> values) { if (Active != null) throw new InvalidOperationException("Другой обмен выполняется."); offers = values.ToList(); Active = this; }
        internal void Permit(Items page, Item item) { allowedPage = page; allowedItem = item; allowedUsed = false; }
        internal void EndPermit() { allowedPage = null; allowedItem = null; }
        internal bool Matches(Item item)
        {
            return item != null && offers.Any(o => object.ReferenceEquals(o.Item, item) || (item.id == o.Id && item.amount == o.Amount && item.quality == o.Quality && item.state != null && item.state.SequenceEqual(o.State)));
        }
        internal bool AllowAdd(Items page, Item item)
        {
            if (!Matches(item)) return true;
            if (!allowedUsed && object.ReferenceEquals(page, allowedPage) && object.ReferenceEquals(item, allowedItem)) { allowedUsed = true; return true; }
            Interference = true; return false;
        }
        public void Dispose() { if (Active == this) Active = null; EndPermit(); }
    }

    [HarmonyPatch(typeof(Items), "addItem", new Type[] { typeof(byte), typeof(byte), typeof(byte), typeof(Item) })]
    internal static class TradeAddPatch
    {
        private static bool Prefix(Items __instance, Item item) { var g = TradeMutationGuard.Active; return g == null || g.AllowAdd(__instance, item); }
    }
    // Эти пути напрямую изменяют Items.items, не вызывая addItem.
    [HarmonyPatch(typeof(Items), "loadItem", new Type[] { typeof(byte), typeof(byte), typeof(byte), typeof(Item) })]
    internal static class TradeLoadPatch
    {
        private static bool Prefix(Items __instance, Item item) { var g = TradeMutationGuard.Active; return g == null || g.AllowAdd(__instance, item); }
    }
    [HarmonyPatch(typeof(Items), "tryAddItem", new Type[] { typeof(Item), typeof(bool) })]
    internal static class TradeTryAddPatch
    {
        private static bool Prefix(Items __instance, Item item, ref bool __result)
        {
            var g = TradeMutationGuard.Active;
            if (g == null || g.AllowAdd(__instance, item)) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(ItemManager), "dropItem", new Type[] { typeof(Item), typeof(Vector3), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class TradeDropPatch
    {
        private static bool Prefix(Item item) { var g = TradeMutationGuard.Active; if (g == null || !g.Matches(item)) return true; g.Interference = true; return false; }
    }
}
