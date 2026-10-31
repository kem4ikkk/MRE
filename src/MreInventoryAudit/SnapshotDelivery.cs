using System;
using System.Collections.Generic;
using SDG.Unturned;

namespace MreInventoryAudit
{
    internal sealed class SnapshotDelivery : IInventoryDelivery
    {
        private sealed class Placement { public Item Item; public byte Page,X,Y,Rotation; }
        private readonly PlayerInventory inventory;
        private readonly DeathSnapshot snapshot;
        private readonly List<Placement> plan=new List<Placement>();
        public SnapshotDelivery(PlayerInventory inventory,DeathSnapshot snapshot) {this.inventory=inventory;this.snapshot=snapshot;}
        public bool Preflight()
        {
            plan.Clear();var pages=new Dictionary<byte,Items>();
            for(byte page=PlayerInventory.SLOTS;page<PlayerInventory.STORAGE;page++) {
                Items original=inventory.items[page];if(original==null||original.width==0||original.height==0)continue;
                var clone=new Items(page);clone.loadSize(original.width,original.height);
                foreach(var jar in original.items)clone.loadItem(jar.x,jar.y,jar.rot,jar.item);pages[page]=clone;
            }
            foreach(var saved in snapshot.Items) {
                var item=new Item(saved.Id,saved.Amount,saved.Quality,(byte[])saved.State.Clone());ItemAsset asset=item.GetAsset();
                if(asset==null)throw new InvalidOperationException("Предмет снимка больше не установлен: "+saved.Id);
                bool found=false;
                foreach(var pair in pages) {
                    byte x,y,rotation;if(!pair.Value.tryFindSpace(asset.size_x,asset.size_y,out x,out y,out rotation))continue;
                    pair.Value.loadItem(x,y,rotation,item);plan.Add(new Placement {Item=item,Page=pair.Key,X=x,Y=y,Rotation=rotation});found=true;break;
                }
                if(!found){plan.Clear();return false;}
            }
            return true;
        }
        public bool Deliver() {foreach(var p in plan)if(!inventory.tryAddItem(p.Item,p.X,p.Y,p.Page,p.Rotation))return false;return true;}
        public bool Rollback()
        {
            foreach(var placement in plan)for(byte page=0;page<inventory.items.Length;page++) {
                Items items=inventory.items[page];if(items==null)continue;
                for(int index=items.items.Count-1;index>=0;index--)if(Object.ReferenceEquals(items.items[index].item,placement.Item))inventory.removeItem(page,(byte)index);
            }
            foreach(var placement in plan)foreach(var items in inventory.items)if(items!=null)foreach(var jar in items.items)if(Object.ReferenceEquals(jar.item,placement.Item))return false;
            return true;
        }
    }
}
