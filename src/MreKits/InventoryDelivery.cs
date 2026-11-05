using System;
using System.Collections.Generic;
using SDG.Unturned;

namespace MreKits
{
    internal sealed class InventoryDelivery : IKitDelivery
    {
        private sealed class Placement { public Item Item; public byte Page, X, Y, Rotation; }
        private readonly Player player;
        private readonly PlayerInventory inventory;
        private readonly KitDefinition kit;
        private readonly List<Placement> planned = new List<Placement>();
        private Item shirt, pants;
        private bool attemptedEquip;
        private Placement bowSlot;
        private bool bowInInventory;
        public InventoryDelivery(Player player, KitDefinition kit) { this.player = player; this.inventory = player.inventory; this.kit = kit; }
        public bool Preflight()
        {
            planned.Clear(); shirt = null; pants = null; attemptedEquip = false; bowSlot = null; bowInInventory = false;
            var items = new List<Item>();
            foreach (KitItem entry in kit.Items) for (int count = 0; count < entry.Count; count++) {
                var item = new Item(entry.Id, EItemOrigin.ADMIN); item.quality = 100;
                ItemAsset asset = item.GetAsset();
                if (asset == null) throw new InvalidOperationException("Не найден предмет kit: " + entry.Id);
                if (kit.AutoEquipClothing && shirt == null && player.clothing.shirt == 0 && asset is ItemShirtAsset) shirt = item;
                else if (kit.AutoEquipClothing && pants == null && player.clothing.pants == 0 && asset is ItemPantsAsset) pants = item;
                else items.Add(item);
            }
            var clones = new Dictionary<byte, Items>();
            // Личные страницы: исключены оружейные слоты, storage и временная AREA.
            for (byte page = PlayerInventory.SLOTS; page < PlayerInventory.STORAGE; page++) {
                Items original = inventory.items[page];
                if (original == null) continue;
                bool newShirt = page == PlayerInventory.SHIRT && shirt != null;
                bool newPants = page == PlayerInventory.PANTS && pants != null;
                if ((newShirt || newPants) && original.items.Count != 0) throw new InvalidOperationException("Страница пустой одежды содержит предметы.");
                byte width = newShirt ? ((ItemShirtAsset)shirt.GetAsset()).width : newPants ? ((ItemPantsAsset)pants.GetAsset()).width : original.width;
                byte height = newShirt ? ((ItemShirtAsset)shirt.GetAsset()).height : newPants ? ((ItemPantsAsset)pants.GetAsset()).height : original.height;
                if (width == 0 || height == 0) continue;
                var clone = new Items(page); clone.loadSize(width, height);
                foreach (ItemJar jar in original.items) clone.loadItem(jar.x, jar.y, jar.rot, jar.item);
                clones[page] = clone;
            }
            var reservedSlots = new HashSet<byte>();
            foreach (Item item in items) {
                ItemAsset asset = item.GetAsset();
                int slot = WeaponSlotPlan.Choose(item.id, page => page < PlayerInventory.SLOTS &&
                    !reservedSlots.Contains(page) && inventory.items[page] != null &&
                    inventory.items[page].items.Count == 0 && asset.canPlayerEquip &&
                    asset.slot.canEquipInPage(page));
                if (slot >= 0) {
                    byte page = (byte)slot;
                    var placement = new Placement { Item = item, Page = page, X = 0, Y = 0, Rotation = 0 };
                    planned.Add(placement); reservedSlots.Add(page);
                    if (item.id == 353 && bowSlot == null) bowSlot = placement;
                    continue;
                }
                bool found = false;
                foreach (var pair in clones) {
                    byte x, y, rotation;
                    if (!pair.Value.tryFindSpace(asset.size_x, asset.size_y, out x, out y, out rotation)) continue;
                    pair.Value.loadItem(x, y, rotation, item);
                    planned.Add(new Placement { Item = item, Page = pair.Key, X = x, Y = y, Rotation = rotation });
                    if (item.id == 353) bowInInventory = true;
                    found = true; break;
                }
                if (!found) { planned.Clear(); return false; }
            }
            return true;
        }
        public bool BowInInventory { get { return bowInInventory; } }
        public void TrySelectBow()
        {
            if (bowSlot == null || player.life.isDead) return;
            Items slot = inventory.items[bowSlot.Page];
            if (slot == null || slot.items.Count != 1 || !Object.ReferenceEquals(slot.items[0].item, bowSlot.Item)) return;
            player.equipment.ServerEquip(bowSlot.Page, bowSlot.X, bowSlot.Y);
        }
        public bool Deliver()
        {
            if (shirt != null) {
                if (player.clothing.shirt != 0) return false;
                attemptedEquip = true;
                player.clothing.askWearShirt(shirt.id, shirt.quality, shirt.state, false);
                if (player.clothing.shirt != shirt.id) return false;
            }
            if (pants != null) {
                if (player.clothing.pants != 0) return false;
                attemptedEquip = true;
                player.clothing.askWearPants(pants.id, pants.quality, pants.state, false);
                if (player.clothing.pants != pants.id) return false;
            }
            foreach (Placement p in planned)
                if (!inventory.tryAddItem(p.Item, p.X, p.Y, p.Page, p.Rotation)) return false;
            return true;
        }
        public bool Rollback()
        {
            // Снятие одежды может переместить предметы и создать дроп. При попытке
            // надевания результат требует ручной сверки, поэтому pending сохраняется.
            if (attemptedEquip) return false;
            // Только exact-reference нового предмета; старые похожие вещи не удаляются.
            foreach (Placement p in planned) {
                for (byte page = 0; page < inventory.items.Length; page++) {
                    Items items = inventory.items[page];
                    if (items == null) continue;
                    for (int index = items.items.Count - 1; index >= 0; index--)
                        if (Object.ReferenceEquals(items.items[index].item, p.Item)) {
                            inventory.removeItem(page, (byte)index);
                            if (page < PlayerInventory.SLOTS) player.equipment.sendSlot(page);
                        }
                }
            }
            foreach (Placement p in planned) foreach (Items items in inventory.items)
                if (items != null) foreach (ItemJar jar in items.items)
                    if (Object.ReferenceEquals(jar.item, p.Item)) return false;
            return true;
        }
    }
}
