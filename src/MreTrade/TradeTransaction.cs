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
            foreach (var offer in offersA) { verify(); TradeDelivery.Remove(a, new[] { offer }); stateA.Remove(offer); verify(); }
            foreach (var offer in offersB) { verify(); TradeDelivery.Remove(b, new[] { offer }); stateB.Remove(offer); verify(); }
            foreach (var placement in toA) { verify(); TradeDelivery.Add(a, new[] { placement }); stateA.Add(placement); verify(); }
            foreach (var placement in toB) { verify(); TradeDelivery.Add(b, new[] { placement }); stateB.Add(placement); verify(); }
            return verify;
        }
    }
}
