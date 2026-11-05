using System;
using System.Collections.Generic;
using System.Linq;
using SDG.Unturned;

namespace MreTrade
{
    // Проверяем полный личный инвентарь после каждого синхронного callback.
    internal sealed class TradeInventoryState
    {
        private sealed class Entry { internal Item Item; internal byte Page, X, Y, Rotation, Amount, Quality; internal ushort Id; internal byte[] State; }
        private readonly List<Entry> expected = new List<Entry>();
        private TradeInventoryState() { }
        internal TradeInventoryState Copy()
        {
            var copy = new TradeInventoryState();
            copy.expected.AddRange(expected);
            return copy;
        }
        internal void CopyFrom(TradeInventoryState source)
        {
            expected.Clear();
            expected.AddRange(source.expected);
        }
        internal TradeInventoryState(PlayerInventory inventory)
        {
            for (byte page = 0; page < PlayerInventory.STORAGE; page++) if (inventory.items[page] != null)
                foreach (var jar in inventory.items[page].items)
                {
                    var item = jar.item;
                    if (item == null || item.state == null || expected.Any(x => Object.ReferenceEquals(x.Item, item))) throw new InvalidOperationException("Некорректный инвентарь.");
                    expected.Add(new Entry { Item = item, Page = page, X = jar.x, Y = jar.y, Rotation = jar.rot, Id = item.id, Amount = item.amount, Quality = item.quality, State = (byte[])item.state.Clone() });
                }
        }
        internal void Remove(TradeOffer offer)
        {
            var entry = expected.SingleOrDefault(x => Object.ReferenceEquals(x.Item, offer.Item));
            if (entry == null) throw new InvalidOperationException("Не найден исходный предмет."); expected.Remove(entry);
        }
        internal void Add(TradePlacement placement)
        {
            var offer = placement.Offer;
            if (expected.Any(x => Object.ReferenceEquals(x.Item, offer.Item))) throw new InvalidOperationException("Предмет уже существует в инвентаре.");
            expected.Add(new Entry { Item = (Item)offer.Item, Page = placement.Page, X = placement.X, Y = placement.Y, Rotation = placement.Rotation, Id = offer.Id, Amount = offer.Amount, Quality = offer.Quality, State = (byte[])offer.State.Clone() });
        }
        internal bool Matches(PlayerInventory inventory) { return MatchesExcept(inventory, null); }
        internal bool MatchesExcept(PlayerInventory inventory, TradeOffer missing)
        {
            var actual = new List<Tuple<byte, ItemJar>>();
            for (byte page = 0; page < PlayerInventory.STORAGE; page++) if (inventory.items[page] != null)
                foreach (var jar in inventory.items[page].items) actual.Add(Tuple.Create(page, jar));
            if (actual.Count != expected.Count - (missing == null ? 0 : 1)) return false;
            foreach (var entry in expected)
            {
                if (missing != null && Object.ReferenceEquals(entry.Item, missing.Item)) continue;
                var matches = actual.Where(p => Object.ReferenceEquals(p.Item2.item, entry.Item)).ToArray();
                if (matches.Length != 1) return false;
                var match = matches[0]; var item = match.Item2.item;
                if (match.Item1 != entry.Page || match.Item2.x != entry.X || match.Item2.y != entry.Y || match.Item2.rot != entry.Rotation || item.id != entry.Id || item.amount != entry.Amount || item.quality != entry.Quality || item.state == null || !item.state.SequenceEqual(entry.State)) return false;
            }
            return true;
        }
    }
}
