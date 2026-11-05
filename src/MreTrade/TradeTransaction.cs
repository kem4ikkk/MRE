using System;
using System.Collections.Generic;
using SDG.Unturned;

namespace MreTrade
{
    internal static class TradeTransaction
    {
        // Вызывать только после устойчивого резервирования обеих сторон.
        internal static System.Action Apply(PlayerInventory a, PlayerInventory b, IList<TradeOffer> offersA, IList<TradeOffer> offersB, IList<TradePlacement> toA, IList<TradePlacement> toB, System.Action guard)
        {
            var stateA = new TradeInventoryState(a); var stateB = new TradeInventoryState(b);
            System.Action verify = () => { guard(); if (!stateA.Matches(a) || !stateB.Matches(b)) throw new InvalidOperationException("Инвентарь изменился во время обмена."); };
            verify();
            foreach (var offer in offersA) { verify(); RemoveOrRestore(a, b, stateA, stateB, offer, guard); stateA.Remove(offer); verify(); }
            foreach (var offer in offersB) { verify(); RemoveOrRestore(b, a, stateB, stateA, offer, guard); stateB.Remove(offer); verify(); }
            foreach (var placement in toA) { verify(); TradeDelivery.Add(a, new[] { placement }); stateA.Add(placement); verify(); }
            foreach (var placement in toB) { verify(); TradeDelivery.Add(b, new[] { placement }); stateB.Add(placement); verify(); }
            return verify;
        }
        private static void RemoveOrRestore(PlayerInventory owner, PlayerInventory other, TradeInventoryState ownerState, TradeInventoryState otherState, TradeOffer offer, System.Action guard)
        {
            var jar = (ItemJar)offer.Jar;
            byte rotation = jar.rot;
            try { TradeDelivery.Remove(owner, new[] { offer }); }
            catch
            {
                // Не возвращать предмет, если сторонний callback изменил состояние,
                // защитник обнаружил вмешательство или исходная клетка занята.
                try
                {
                    guard();
                    var item = (Item)offer.Item;
                    var page = owner.items[offer.Page];
                    var asset = item.GetAsset();
                    if (page != null && asset != null && ownerState.MatchesExcept(owner, offer) && otherState.Matches(other) &&
                        page.checkSpaceEmpty(offer.X, offer.Y, asset.size_x, asset.size_y, rotation))
                    {
                        TradeDelivery.Add(owner, new[] { new TradePlacement { Offer = offer, Page = offer.Page, X = offer.X, Y = offer.Y, Rotation = rotation } });
                        guard();
                        if (!ownerState.Matches(owner) || !otherState.Matches(other)) throw new InvalidOperationException("Откат требует ручной проверки.");
                    }
                }
                catch { /* Блокировки и журнал сохраняются для ручной сверки. */ }
                throw;
            }
        }
    }
}
