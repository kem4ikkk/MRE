using System;
using MreKits;
using SDG.Unturned;

internal static class InventoryDeliveryTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("inventory delivery equips slots and selects bow with existing active item", () => {
            var player = new Player(); player.equipment.HasValidUseable = true;
            var delivery = new InventoryDelivery(player, Kit());
            check(delivery.Preflight() && delivery.Deliver(), "delivery");
            delivery.TrySelectBow();
            check(player.inventory.items[0].items[0].item.id == 353 && player.inventory.items[1].items[0].item.id == 16, "slots");
            check(player.equipment.Selected == 0 && !delivery.BowInInventory, "selection");
        });
        test("occupied slots preserved and kit falls back to inventory", () => {
            var player = new Player(); var old0 = new Item(700, EItemOrigin.ADMIN); var old1 = new Item(701, EItemOrigin.ADMIN);
            player.inventory.items[0].loadItem(0, 0, 0, old0); player.inventory.items[1].loadItem(0, 0, 0, old1);
            var delivery = new InventoryDelivery(player, Kit());
            check(delivery.Preflight() && delivery.Deliver(), "delivery"); delivery.TrySelectBow();
            check(ReferenceEquals(player.inventory.items[0].items[0].item, old0) && ReferenceEquals(player.inventory.items[1].items[0].item, old1), "originals retained");
            check(player.inventory.items[2].items.Count == 2 && delivery.BowInInventory && player.equipment.Selected == -1, "fallback");
        });
        test("partial delivery rollback clears slot notification and retains old item", () => {
            var player = new Player(); var old = new Item(700, EItemOrigin.ADMIN);
            player.inventory.items[1].loadItem(0, 0, 0, old); player.inventory.FailAfter = 1;
            var delivery = new InventoryDelivery(player, Kit());
            check(delivery.Preflight() && !delivery.Deliver(), "injected rejection");
            check(delivery.Rollback(), "rollback");
            check(player.inventory.items[0].items.Count == 0 && ReferenceEquals(player.inventory.items[1].items[0].item, old), "inventory restored");
            check(player.equipment.SlotUpdates == 1, "slot notification");
        });
        test("full inventory preflight does not move existing items", () => {
            var player = new Player(); var old = new Item(700, EItemOrigin.ADMIN);
            player.inventory.items[0].loadItem(0, 0, 0, old); player.inventory.items[1].loadItem(0, 0, 0, new Item(701, EItemOrigin.ADMIN));
            player.inventory.items[2].loadSize(0, 0);
            var delivery = new InventoryDelivery(player, Kit());
            check(!delivery.Preflight() && ReferenceEquals(player.inventory.items[0].items[0].item, old), "no mutation");
        });
    }
    private static KitDefinition Kit() => new KitDefinition { Items = new System.Collections.Generic.List<KitItem> { new KitItem { Id = 353, Count = 1 }, new KitItem { Id = 16, Count = 1 } } };
}
