using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SDG.Unturned;

namespace MreTrade
{
    internal static class TradeProjectionHooks
    {
        private static MreTradePlugin Plugin { get { return MreTradePlugin.Instance; } }
        private static void Add(Harmony harmony, MethodInfo method, string prefix)
        {
            if (method == null) throw new InvalidOperationException("Не найдена обязательная сигнатура защиты проекции: " + prefix);
            harmony.Patch(method, new HarmonyMethod(typeof(TradeProjectionHooks), prefix) { priority = Priority.First });
        }
        internal static void Install(Harmony harmony)
        {
            Add(harmony, AccessTools.Method(typeof(PlayerInventory), "ReceiveDragItem", Enumerable.Repeat(typeof(byte), 7).ToArray()), "Drag");
            Add(harmony, AccessTools.Method(typeof(PlayerInventory), "ReceiveSwapItem", Enumerable.Repeat(typeof(byte), 8).ToArray()), "Swap");
            Add(harmony, AccessTools.Method(typeof(PlayerInventory), "ReceiveDropItem", Enumerable.Repeat(typeof(byte), 3).ToArray()), "Drop");
            string[] clothes = { "ReceiveSwapShirtRequest", "ReceiveSwapPantsRequest", "ReceiveSwapHatRequest", "ReceiveSwapBackpackRequest", "ReceiveSwapVestRequest", "ReceiveSwapMaskRequest", "ReceiveSwapGlassesRequest" };
            foreach (string name in clothes) Add(harmony, AccessTools.Method(typeof(PlayerClothing), name, Enumerable.Repeat(typeof(byte), 3).ToArray()), "Clothing");
            var crafts = typeof(PlayerCrafting).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(m => m.Name == "ReceiveCraft").ToArray();
            if (crafts.Length < 2) throw new InvalidOperationException("Не найдены обе сигнатуры крафта.");
            foreach (var method in crafts) Add(harmony, method, "Craft");
            Add(harmony, AccessTools.Method(typeof(PlayerCrafting), "ReceiveStripAttachments", Enumerable.Repeat(typeof(byte), 3).ToArray()), "Craft");
            Add(harmony, AccessTools.Method(typeof(PlayerEquipment), "ReceiveEquipRequest", Enumerable.Repeat(typeof(byte), 3).ToArray()), "Equip");
            foreach (string name in new[] { "removeItem", "updateAmount", "updateQuality", "updateState", "loadItem", "addItem", "tryAddItem", "loadSize", "resize" })
            {
                var methods = typeof(Items).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(m => m.Name == name).ToArray();
                if (methods.Length == 0) throw new InvalidOperationException("Не найден Items." + name);
                foreach (var method in methods) Add(harmony, method, "ItemsMutation");
            }
        }
        private static bool Drag(PlayerInventory __instance, byte page_0, byte x_0, byte y_0, byte page_1)
        {
            var plugin = Plugin; if (plugin == null || !plugin.ProjectionOpen(__instance)) return true;
            if (page_0 == PlayerInventory.STORAGE || page_1 == PlayerInventory.STORAGE) plugin.ProjectionDrag(__instance, page_0, x_0, y_0, page_1);
            return false;
        }
        private static bool Swap(PlayerInventory __instance, byte page_0, byte x_0, byte y_0, byte page_1)
        {
            var plugin = Plugin; if (plugin == null || !plugin.ProjectionOpen(__instance)) return true;
            if (page_0 == PlayerInventory.STORAGE || page_1 == PlayerInventory.STORAGE) plugin.ProjectionDrag(__instance, page_0, x_0, y_0, page_1);
            return false;
        }
        private static bool Drop(PlayerInventory __instance) { var p = Plugin; return p == null || !p.ProjectionOpen(__instance); }
        private static bool Clothing(PlayerClothing __instance) { var p = Plugin; return p == null || __instance.player == null || !p.ProjectionOpen(__instance.player.inventory); }
        private static bool Craft(PlayerCrafting __instance) { var p = Plugin; return p == null || __instance.player == null || !p.ProjectionOpen(__instance.player.inventory); }
        private static bool Equip(PlayerEquipment __instance) { var p = Plugin; return p == null || __instance.player == null || !p.ProjectionOpen(__instance.player.inventory); }
        private static bool ItemsMutation(Items __instance) { var p = Plugin; return p == null || !p.IsTradeProjection(__instance); }
    }
}
