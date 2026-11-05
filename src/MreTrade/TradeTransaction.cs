using System;
using System.Collections.Generic;
using SDG.Unturned;

namespace MreTrade
{
    internal static class TradeTransaction
    {
        private sealed class Change
        {
            internal PlayerInventory Inventory;
            internal TradeInventoryState State;
            internal TradeOffer Offer;
            internal TradePlacement Placement;
            internal bool Added;
        }

        // Откат выполняется только пока оба инвентаря и защитник подтверждают известное состояние.
        internal static System.Action Apply(PlayerInventory a, PlayerInventory b, IList<TradeOffer> offersA, IList<TradeOffer> offersB, IList<TradePlacement> toA, IList<TradePlacement> toB, System.Action guard)
        {
            var stateA = new TradeInventoryState(a); var stateB = new TradeInventoryState(b);
            System.Action verify = () => { guard(); if (!stateA.Matches(a) || !stateB.Matches(b)) throw new InvalidOperationException("Инвентарь изменился во время обмена."); };
            var changes = new List<Change>();
            Action<Change> execute = change =>
            {
                verify();
                var next = change.State.Copy();
                if (change.Added) next.Add(change.Placement); else next.Remove(change.Offer);
                try
                {
                    if (change.Added) TradeDelivery.Add(change.Inventory, new[] { change.Placement });
                    else TradeDelivery.Remove(change.Inventory, new[] { change.Offer });
                }
                catch
                {
                    // API может выбросить исключение как до мутации, так и после callback.
                    // Записываем операцию только если её результат целиком наблюдаем.
                    if (next.Matches(change.Inventory)) { change.State.CopyFrom(next); changes.Add(change); }
                    throw;
                }
                change.State.CopyFrom(next); changes.Add(change); verify();
            };
            try
            {
                verify();
                foreach (var offer in offersA) execute(new Change { Inventory = a, State = stateA, Offer = offer, Placement = Original(offer) });
                foreach (var offer in offersB) execute(new Change { Inventory = b, State = stateB, Offer = offer, Placement = Original(offer) });
                foreach (var placement in toA) execute(new Change { Inventory = a, State = stateA, Offer = placement.Offer, Placement = placement, Added = true });
                foreach (var placement in toB) execute(new Change { Inventory = b, State = stateB, Offer = placement.Offer, Placement = placement, Added = true });
            }
            catch
            {
                try
                {
                    for (int i = changes.Count - 1; i >= 0; --i)
                    {
                        verify();
                        var change = changes[i];
                        if (change.Added)
                        {
                            var page = change.Inventory.items[change.Placement.Page];
                            byte index = page.getIndex(change.Placement.X, change.Placement.Y);
                            var jar = index == byte.MaxValue ? null : page.getItem(index);
                            var offer = change.Offer;
                            var delivered = new TradeOffer { Page = change.Placement.Page, X = change.Placement.X, Y = change.Placement.Y, Jar = jar, Item = offer.Item, Id = offer.Id, Amount = offer.Amount, Quality = offer.Quality, State = offer.State };
                            TradeDelivery.Remove(change.Inventory, new[] { delivered });
                            change.State.Remove(offer);
                        }
                        else
                        {
                            var placement = change.Placement;
                            var page = change.Inventory.items[placement.Page];
                            var asset = ((Item)change.Offer.Item).GetAsset();
                            if (page == null || asset == null || !page.checkSpaceEmpty(placement.X, placement.Y, asset.size_x, asset.size_y, placement.Rotation)) throw new InvalidOperationException("Место для отката занято.");
                            TradeDelivery.Add(change.Inventory, new[] { placement });
                            change.State.Add(placement);
                        }
                        verify();
                    }
                }
                catch { /* Журнал и блокировки остаются для ручной сверки. */ }
                throw;
            }
            return verify;
        }
        private static TradePlacement Original(TradeOffer offer)
        {
            return new TradePlacement { Offer = offer, Page = offer.Page, X = offer.X, Y = offer.Y, Rotation = ((ItemJar)offer.Jar).rot };
        }
    }
}
