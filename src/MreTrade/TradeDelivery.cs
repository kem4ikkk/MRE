using System;
using System.Collections.Generic;
using System.Linq;
using SDG.Unturned;

namespace MreTrade
{
    internal sealed class TradePlacement
    {
        internal TradeOffer Offer;
        internal byte Page, X, Y, Rotation;
    }
    internal static class TradeDelivery
    {
        internal static bool Valid(PlayerInventory inventory, IList<TradeOffer> offers)
        {
            foreach (var offer in offers)
            {
                if (offer.Page < PlayerInventory.SLOTS || offer.Page >= PlayerInventory.STORAGE || inventory.items[offer.Page] == null) return false;
                byte index = inventory.items[offer.Page].getIndex(offer.X, offer.Y);
                if (index == byte.MaxValue) return false;
                var jar = inventory.items[offer.Page].getItem(index); var item = jar == null ? null : jar.item;
                if (!Object.ReferenceEquals(jar, offer.Jar) || !Object.ReferenceEquals(item, offer.Item) || item == null || item.id != offer.Id || item.amount != offer.Amount || item.quality != offer.Quality || item.state == null || !item.state.SequenceEqual(offer.State)) return false;
            }
            return true;
        }
        internal static List<TradePlacement> Plan(PlayerInventory target, IList<TradeOffer> outgoing, IList<TradeOffer> incoming)
        {
            var pages = new Dictionary<byte, Items>();
            for (byte page = PlayerInventory.SLOTS; page < PlayerInventory.STORAGE; page++)
            {
                Items original = target.items[page]; if (original == null || original.width == 0 || original.height == 0) continue;
                var clone = new Items(page); clone.loadSize(original.width, original.height);
                foreach (var jar in original.items) if (!outgoing.Any(o => Object.ReferenceEquals(o.Jar, jar))) clone.loadItem(jar.x, jar.y, jar.rot, jar.item);
                pages.Add(page, clone);
            }
            var result = new List<TradePlacement>();
            foreach (var offer in incoming)
            {
                Item item = offer.Item as Item; var asset = item == null ? null : item.GetAsset();
                if (asset == null) throw new InvalidOperationException("Предмет недоступен на сервере.");
                bool found = false;
                foreach (var page in pages)
                {
                    byte x, y, rot; if (!page.Value.tryFindSpace(asset.size_x, asset.size_y, out x, out y, out rot)) continue;
                    page.Value.loadItem(x, y, rot, item); result.Add(new TradePlacement { Offer = offer, Page = page.Key, X = x, Y = y, Rotation = rot }); found = true; break;
                }
                if (!found) return null;
            }
            return result;
        }
        internal static void Remove(PlayerInventory inventory, IList<TradeOffer> offers)
        {
            foreach (var offer in offers)
            {
                if (!Valid(inventory, new[] { offer })) throw new InvalidOperationException("Предмет обмена изменён.");
                byte index = inventory.items[offer.Page].getIndex(offer.X, offer.Y);
                inventory.removeItem(offer.Page, index);
            }
        }
        internal static void Add(PlayerInventory inventory, IList<TradePlacement> placements)
        {
            foreach (var placement in placements)
            {
                var offer = placement.Offer; var item = offer.Item as Item;
                if (item == null || item.id != offer.Id || item.amount != offer.Amount || item.quality != offer.Quality || item.state == null || !item.state.SequenceEqual(offer.State)) throw new InvalidOperationException("Состояние предмета изменилось во время обмена.");
                var guard = TradeMutationGuard.Active;
                if (guard != null) guard.Permit(inventory.items[placement.Page], item);
                try { if (!inventory.tryAddItem(item, placement.X, placement.Y, placement.Page, placement.Rotation)) throw new InvalidOperationException("Не удалось разместить предмет обмена."); }
                finally { if (guard != null) guard.EndPermit(); }
            }
        }
    }
}
