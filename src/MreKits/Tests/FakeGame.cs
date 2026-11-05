using System.Collections.Generic;
namespace MreKits {
    internal sealed class KitItem { public ushort Id; public int Count; }
    internal sealed class KitDefinition { public bool AutoEquipClothing; public List<KitItem> Items; }
}
namespace SDG.Unturned {
    public enum EItemOrigin { ADMIN }
    public class Slot { private readonly ushort id; public Slot(ushort id) { this.id = id; } public bool canEquipInPage(byte p) => id == 353 ? p == 0 : id == 16 && p == 1; }
    public class ItemAsset { public byte size_x = 1, size_y = 1; public bool canPlayerEquip = true; public Slot slot; }
    public class ItemShirtAsset : ItemAsset { public byte width, height; }
    public class ItemPantsAsset : ItemAsset { public byte width, height; }
    public class Item { public ushort id; public byte quality; public byte[] state = new byte[0]; public Item(ushort id, EItemOrigin origin) { this.id = id; } public ItemAsset GetAsset() => new ItemAsset { slot = new Slot(id) }; }
    public class ItemJar { public byte x,y,rot; public Item item; }
    public class Items {
        public byte width = 8, height = 8; public List<ItemJar> items = new List<ItemJar>();
        public Items(byte page) { } public void loadSize(byte w, byte h) { width = w; height = h; }
        public void loadItem(byte x, byte y, byte rot, Item item) { items.Add(new ItemJar { x=x,y=y,rot=rot,item=item }); }
        public bool tryFindSpace(byte w, byte h, out byte x, out byte y, out byte rot) { x=(byte)items.Count; y=0; rot=0; return width>items.Count && height>0; }
    }
    public class PlayerInventory {
        public const byte SLOTS=2, SHIRT=3, PANTS=4, STORAGE=3;
        public Items[] items = { new Items(0), new Items(1), new Items(2) }; public int FailAfter=int.MaxValue, Added;
        public bool tryAddItem(Item i, byte x, byte y, byte p, byte r) { if (Added++ >= FailAfter) return false; if (p<2 && items[p].items.Count>0) return false; items[p].loadItem(x,y,r,i); return true; }
        public void removeItem(byte page, byte index) { items[page].items.RemoveAt(index); }
    }
    public class Clothing { public ushort shirt,pants; public void askWearShirt(ushort i,byte q,byte[] s,bool e) { shirt=i; } public void askWearPants(ushort i,byte q,byte[] s,bool e) { pants=i; } }
    public class Life { public bool isDead; }
    public class Equipment { public bool HasValidUseable; public int Selected=-1, SlotUpdates; public void ServerEquip(byte p,byte x,byte y) { Selected=p; } public void sendSlot(byte p) { SlotUpdates++; } }
    public class Player { public PlayerInventory inventory=new PlayerInventory(); public Clothing clothing=new Clothing(); public Life life=new Life(); public Equipment equipment=new Equipment(); }
}
