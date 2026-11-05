using System;
using System.Collections.Generic;
using System.Linq;
using MreTrade;
using SDG.Unturned;
class Program
{
    static void Main()
    {
        CheckSuccessfulTransfer();
        CheckInterruptionAfterRemoval();
        CheckInterruptionWithChangedInventory();
        CheckRollbackAfterSecondRemoval();
        CheckRollbackAfterFailedDelivery();
        CheckMissingLockWithPendingJournal();
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
    static TradeOffer AddOffer(PlayerInventory owner)
    {
        var item = new Item { state = new byte[] { 7, 8 } };
        var jar = new ItemJar { item = item };
        owner.items[2].items.Add(jar);
        return new TradeOffer { Page = 2, X = 0, Y = 0, Jar = jar, Item = item, Id = item.id, Amount = item.amount, Quality = item.quality, State = (byte[])item.state.Clone() };
    }
    static void CheckSuccessfulTransfer()
    {
        var a = new PlayerInventory(); var b = new PlayerInventory(); var offer = AddOffer(a);
        var place = new TradePlacement { Offer = offer, Page = 2, X = 0, Y = 0 };
        var verify = TradeTransaction.Apply(a, b, new[] { offer }, Array.Empty<TradeOffer>(), Array.Empty<TradePlacement>(), new[] { place }, () => { });
        verify();
        if (a.items[2].items.Count != 0 || b.items[2].items.Count != 1 || !ReferenceEquals(b.items[2].items[0].item, offer.Item) || !b.items[2].items[0].item.state.SequenceEqual(new byte[] { 7, 8 }))
            throw new Exception("Успешная передача не сохранила предмет и его состояние.");
    }
    static void CheckInterruptionAfterRemoval()
    {
        var a = new PlayerInventory(); var b = new PlayerInventory(); var offer = AddOffer(a);
        a.AfterRemove = () => { throw new InvalidOperationException("Контролируемый сбой после удаления."); };
        bool interrupted = false;
        try { TradeTransaction.Apply(a, b, new[] { offer }, Array.Empty<TradeOffer>(), Array.Empty<TradePlacement>(), new[] { new TradePlacement { Offer = offer, Page = 2 } }, () => { }); }
        catch (InvalidOperationException) { interrupted = true; }
        if (!interrupted || a.items[2].items.Count != 1 || b.items[2].items.Count != 0 || !ReferenceEquals(a.items[2].items[0].item, offer.Item))
            throw new Exception("Предмет не восстановлен после сбоя удаления.");
        Console.WriteLine("Предмет восстановлен после синхронной ошибки удаления.");
    }
    static void CheckInterruptionWithChangedInventory()
    {
        var a = new PlayerInventory(); var b = new PlayerInventory(); var offer = AddOffer(a);
        a.AfterRemove = () => { AddOffer(b); throw new InvalidOperationException("Вмешательство при удалении."); };
        bool interrupted = false;
        try { TradeTransaction.Apply(a, b, new[] { offer }, Array.Empty<TradeOffer>(), Array.Empty<TradePlacement>(), new[] { new TradePlacement { Offer = offer, Page = 2 } }, () => { }); }
        catch (InvalidOperationException) { interrupted = true; }
        if (!interrupted || a.items[2].items.Any(j => ReferenceEquals(j.item, offer.Item)) || b.items[2].items.Any(j => ReferenceEquals(j.item, offer.Item)))
            throw new Exception("Откат не должен выполняться при изменении инвентаря второй стороны.");
        Console.WriteLine("При постороннем изменении автоматический откат остановлен.");
    }
    static void CheckRollbackAfterSecondRemoval()
    {
        var a = new PlayerInventory(); var b = new PlayerInventory(); var oa = AddOffer(a); var ob = AddOffer(b);
        b.AfterRemove = () => { throw new InvalidOperationException("Сбой удаления второй стороны."); };
        bool interrupted = false;
        try { TradeTransaction.Apply(a, b, new[] { oa }, new[] { ob }, new[] { new TradePlacement { Offer = ob, Page = 2 } }, new[] { new TradePlacement { Offer = oa, Page = 2 } }, () => { }); }
        catch (InvalidOperationException) { interrupted = true; }
        if (!interrupted || a.items[2].items.Count != 1 || b.items[2].items.Count != 1 || !ReferenceEquals(a.items[2].items[0].item, oa.Item) || !ReferenceEquals(b.items[2].items[0].item, ob.Item))
            throw new Exception("Откат после удаления второй стороны не вернул оба предмета.");
        Console.WriteLine("Откат после ошибки удаления второй стороны вернул оба предмета.");
    }
    static void CheckRollbackAfterFailedDelivery()
    {
        var a = new PlayerInventory(); var b = new PlayerInventory(); var oa = AddOffer(a); var ob = AddOffer(b);
        bool once = true;
        b.BeforeAdd = () => { if (once) { once = false; throw new InvalidOperationException("Сбой выдачи."); } };
        bool interrupted = false;
        try { TradeTransaction.Apply(a, b, new[] { oa }, new[] { ob }, new[] { new TradePlacement { Offer = ob, Page = 2 } }, new[] { new TradePlacement { Offer = oa, Page = 2 } }, () => { }); }
        catch (InvalidOperationException) { interrupted = true; }
        if (!interrupted || a.items[2].items.Count != 1 || b.items[2].items.Count != 1 || !ReferenceEquals(a.items[2].items[0].item, oa.Item) || !ReferenceEquals(b.items[2].items[0].item, ob.Item))
            throw new Exception("Откат частичной выдачи не вернул оба предмета.");
        Console.WriteLine("Откат после частичной выдачи вернул оба предмета.");
    }
    static void CheckMissingLockWithPendingJournal()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mre-trade-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new TradeJournal(directory);
            if (journal.Blocked(123)) throw new Exception("Пустой журнал заблокирован.");
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "test.pending"), "test");
            if (!journal.Blocked(123)) throw new Exception("Незавершённый журнал без lock не остановил обмен.");
            Console.WriteLine("Незавершённый журнал блокирует обмен даже без lock.");
        }
        finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
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
        public bool checkSpaceEmpty(byte x,byte y,byte w,byte h,byte rot)=>getIndex(x,y)==byte.MaxValue;
    }
    public class PlayerInventory
    {
        public const byte SLOTS=2,STORAGE=7; public Items[] items=Enumerable.Range(0,9).Select(i=>new Items((byte)i)).ToArray(); public Action AfterRemove, BeforeAdd;
        public void removeItem(byte page,byte index){items[page].items.RemoveAt(index);AfterRemove?.Invoke();}
        public bool tryAddItem(Item item,byte x,byte y,byte page,byte rot){BeforeAdd?.Invoke();items[page].loadItem(x,y,rot,item);return true;}
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
