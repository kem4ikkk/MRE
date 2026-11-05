using System;
using System.Collections.Generic;
using System.Linq;
using MreTrade;
using SDG.Unturned;
class Program
{
    static void Main()
    {
        var a = new PlayerInventory(); var b = new PlayerInventory();
        var item = new Item(); var jar = new ItemJar { item = item };
        a.items[2].items.Add(jar);
        var offer = new TradeOffer { Page=2,X=0,Y=0,Jar=jar,Item=item,Id=item.id,Amount=item.amount,Quality=item.quality,State=(byte[])item.state.Clone() };
        a.AfterRemove = () => a.items[2].items.Add(new ItemJar { x=1,item=item });
        bool aborted = false;
        try
        {
            TradeTransaction.Apply(a, b, new[] { offer }, Array.Empty<TradeOffer>(), Array.Empty<TradePlacement>(),
                new[] { new TradePlacement { Offer = offer, Page = 2, X = 0, Y = 0 } }, () => { });
        }
        catch (InvalidOperationException) { aborted = true; }
        bool inB = b.items[2].items.Any(x => ReferenceEquals(x.item, item));
        if (!aborted || inB) throw new Exception("Callback повторной вставки не остановил передачу.");
        Console.WriteLine("Повторная вставка в callback остановила выдачу второй стороне.");
    }
}
namespace SDG.Unturned
{
    public class ItemAsset { public byte size_x=1,size_y=1; }
    public class Item { public ushort id=42; public byte amount=1,quality=100; public byte[] state=new byte[0]; public ItemAsset GetAsset()=>new ItemAsset(); }
    public class ItemJar { public byte x,y,rot; public Item item; }
    public class Items
    {
        public byte width=5,height=3; public List<ItemJar> items=new List<ItemJar>();
        public Items(byte page){} public void loadSize(byte w,byte h){width=w;height=h;}
        public void loadItem(byte x,byte y,byte r,Item item)=>items.Add(new ItemJar{x=x,y=y,rot=r,item=item});
        public byte getIndex(byte x,byte y){int i=items.FindIndex(j=>j.x==x&&j.y==y);return i<0?byte.MaxValue:(byte)i;}
        public ItemJar getItem(byte i)=>items[i];
        public bool tryFindSpace(byte w,byte h,out byte x,out byte y,out byte rot){x=y=rot=0;return true;}
    }
    public class PlayerInventory
    {
        public const byte SLOTS=2,STORAGE=7; public Items[] items=Enumerable.Range(0,9).Select(i=>new Items((byte)i)).ToArray(); public Action AfterRemove;
        public void removeItem(byte page,byte index){items[page].items.RemoveAt(index);AfterRemove?.Invoke();}
        public bool tryAddItem(Item item,byte x,byte y,byte page,byte rot){items[page].loadItem(x,y,rot,item);return true;}
    }
}

// Логика Harmony здесь не тестируется: эта заглушка нужна, чтобы
// отдельно проверить обнаружение повторной вставки снимком инвентаря.
namespace MreTrade
{
    internal sealed class TradeMutationGuard
    {
        internal static TradeMutationGuard Active;
        internal void Permit(SDG.Unturned.Items page, SDG.Unturned.Item item) { }
        internal void EndPermit() { }
    }
}
