using System;
using System.Collections.Generic;
namespace SDG.Unturned
{
    public sealed class ItemAsset { public byte size_x=1,size_y=1; public int type; }
    public sealed class Item
    {
        public ushort id; public byte amount=1,quality=100; public byte[] state=new byte[0];
        public readonly ItemAsset Asset = new ItemAsset();
    }
    public sealed class ItemJar
    {
        public byte x,y,rot,size_x,size_y; public Item item; public object interactableItem;
        public ItemJar(byte x,byte y,byte rot,Item item) {this.x=x;this.y=y;this.rot=rot;this.item=item;size_x=item.Asset.size_x;size_y=item.Asset.size_y;}
        public ItemAsset GetAsset() => item.Asset;
    }
    public sealed class Items
    {
        public byte width,height;
        public List<ItemJar> items=new List<ItemJar>();
        public int RebuildCalls,DiscardCalls;
        public bool ThrowNextRebuild;
        public void loadSize(byte w,byte h)
        {
            if(ThrowNextRebuild){ThrowNextRebuild=false;throw new InvalidOperationException("Injected grid allocation failure");}
            RebuildCalls++;width=w;height=h;
            foreach(var jar in items.ToArray()) {
                int iw=jar.rot%2==0?jar.size_x:jar.size_y,ih=jar.rot%2==0?jar.size_y:jar.size_x;
                if(jar.x+iw>w||jar.y+ih>h){items.Remove(jar);DiscardCalls++;}
            }
        }
    }
    public sealed class PlayerInventory
    {
        public readonly Items[] items=new Items[9];
        public readonly List<(string kind,byte page,byte x,byte y,Item item)> Packets=new();
        public int Updates,PacketAttempts;
        public int FailAtPacket;
        public bool PermanentNetworkFailure;
        public Action OnPacket;
        private void incrementUpdateIndex() {Updates++;}
        private void sendItemRemove(byte page,ItemJar jar){Send("remove",page,jar);}
        private void sendItemAdd(byte page,ItemJar jar){Send("add",page,jar);}
        private void Send(string kind,byte page,ItemJar jar){
            PacketAttempts++;if(PermanentNetworkFailure||PacketAttempts==FailAtPacket)throw new InvalidOperationException("Injected send failure");
            Packets.Add((kind,page,jar.x,jar.y,jar.item));OnPacket?.Invoke();
        }
    }
    public sealed class InteractableStorage
    {
        public Items items;public object onStateRebuilt;public int Saves;public bool ThrowNextSave;
        public void rebuildState(){if(ThrowNextSave){ThrowNextSave=false;throw new InvalidOperationException("Injected save failure");}Saves++;}
    }
}
