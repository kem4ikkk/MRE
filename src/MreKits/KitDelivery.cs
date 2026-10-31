using System;
using System.Collections.Generic;
using SDG.Unturned;

namespace MreKits
{
    internal sealed class KitDelivery
    {
        private sealed class Placement { internal Item Item; internal byte Page, X, Y, Rotation; }
        private readonly Player player;
        private readonly PlayerInventory inventory;
        private readonly List<Placement> plan = new List<Placement>();
        private Item shirt, pants;
        private bool wearShirt, wearPants;
        internal KitDelivery(Player player) { this.player = player; inventory = player.inventory; }

        internal bool Preflight(ushort[] ids)
        {
            plan.Clear(); var pages = new Dictionary<byte, Items>();
            shirt = new Item(180, EItemOrigin.ADMIN); pants = new Item(2, EItemOrigin.ADMIN);
            var shirtAsset = shirt.GetAsset() as ItemShirtAsset;
            var pantsAsset = pants.GetAsset() as ItemPantsAsset;
            if (shirtAsset == null || pantsAsset == null) throw new InvalidOperationException("Одежда набора недоступна.");
            wearShirt = player.clothing.shirt == 0; wearPants = player.clothing.pants == 0;
            for (byte page = PlayerInventory.SLOTS; page < PlayerInventory.STORAGE; page++)
            {
                Items original = inventory.items[page]; if (original == null) continue;
                bool newShirt = page == PlayerInventory.SHIRT && wearShirt;
                bool newPants = page == PlayerInventory.PANTS && wearPants;
                if ((newShirt || newPants) && original.items.Count != 0) throw new InvalidOperationException("Страница пустой одежды содержит предметы.");
                byte width = newShirt ? shirtAsset.width : newPants ? pantsAsset.width : original.width;
                byte height = newShirt ? shirtAsset.height : newPants ? pantsAsset.height : original.height;
                if (width == 0 || height == 0) continue;
                var clone = new Items(page); clone.loadSize(width, height);
                foreach (var jar in original.items) clone.loadItem(jar.x, jar.y, jar.rot, jar.item);
                pages.Add(page, clone);
            }
            var items = new List<Item>();
            foreach (ushort id in ids) items.Add(new Item(id, EItemOrigin.ADMIN));
            // Уже надетые вещи игрока не заменяем: одежду набора кладём в инвентарь.
            if (!wearShirt) items.Add(shirt);
            if (!wearPants) items.Add(pants);
            foreach (Item item in items)
            {
                ItemAsset asset = item.GetAsset();
                if (asset == null) throw new InvalidOperationException("Предмет набора недоступен: " + item.id);
                bool placed = false;
                foreach (var page in pages)
                {
                    byte x, y, rotation;
                    if (!page.Value.tryFindSpace(asset.size_x, asset.size_y, out x, out y, out rotation)) continue;
                    page.Value.loadItem(x, y, rotation, item);
                    plan.Add(new Placement { Item = item, Page = page.Key, X = x, Y = y, Rotation = rotation });
                    placed = true; break;
                }
                if (!placed) { plan.Clear(); return false; }
            }
            return true;
        }

        // Только после резервирования. Ошибки оставляют pending для ручной сверки:
        // снятие одежды может переместить вещи или создать дроп.
        internal bool Deliver()
        {
            if (wearShirt)
            {
                if (player.clothing.shirt != 0) return false;
                player.clothing.askWearShirt(shirt.id, shirt.quality, shirt.state, false);
                if (player.clothing.shirt != shirt.id) return false;
            }
            if (wearPants)
            {
                if (player.clothing.pants != 0) return false;
                player.clothing.askWearPants(pants.id, pants.quality, pants.state, false);
                if (player.clothing.pants != pants.id) return false;
            }
            foreach (var p in plan) if (!inventory.tryAddItem(p.Item, p.X, p.Y, p.Page, p.Rotation)) return false;
            return true;
        }
    }
}
