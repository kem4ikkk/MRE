using System;
using System.Collections.Generic;

namespace MreTrade
{
    internal sealed class TradeOffer
    {
        internal byte Page, X, Y;
        internal ushort Id;
        internal byte Amount, Quality;
        internal byte[] State;
        internal object Jar, Item;
    }
    internal sealed class TradeSession
    {
        internal readonly ulong A, B;
        internal readonly DateTime ExpiresUtc;
        internal long Revision { get; private set; }
        internal long AcknowledgedRevisionA { get; private set; }
        internal long AcknowledgedRevisionB { get; private set; }
        internal bool Accepted, ConfirmedA, ConfirmedB, Committing;
        internal readonly List<TradeOffer> OffersA = new List<TradeOffer>();
        internal readonly List<TradeOffer> OffersB = new List<TradeOffer>();
        internal TradeSession(ulong a, ulong b, DateTime expiresUtc)
        {
            if (a == 0 || b == 0 || a == b) throw new ArgumentException("Нужны два разных игрока.");
            A = a; B = b; ExpiresUtc = expiresUtc;
            Revision = 1;
        }
        internal bool Contains(ulong id) { return id == A || id == B; }
        internal ulong Other(ulong id) { if (id == A) return B; if (id == B) return A; throw new UnauthorizedAccessException(); }
        internal List<TradeOffer> Own(ulong id) { if (id == A) return OffersA; if (id == B) return OffersB; throw new UnauthorizedAccessException(); }
        internal void Accept(ulong id) { if (id != B || Accepted || Committing) throw new InvalidOperationException(); Accepted = true; }
        internal void Changed()
        {
            if (!Accepted || Committing) throw new InvalidOperationException();
            Revision = checked(Revision + 1);
            ConfirmedA = false;
            ConfirmedB = false;
            AcknowledgedRevisionA = 0;
            AcknowledgedRevisionB = 0;
        }
        internal bool Acknowledge(ulong id, long expectedRevision)
        {
            if (!Accepted || Committing || !Contains(id)) throw new InvalidOperationException();
            if (Revision != expectedRevision) return false;
            if (id == A) AcknowledgedRevisionA = Revision; else AcknowledgedRevisionB = Revision;
            return true;
        }
        // Confirmation is accepted only after the player listed the current offer revision.
        internal bool Confirm(ulong id)
        {
            if (!Accepted || Committing || !Contains(id) || AcknowledgedRevision(id) != Revision) throw new InvalidOperationException();
            if (id == A) ConfirmedA = true; else ConfirmedB = true;
            return ConfirmedA && ConfirmedB;
        }
        internal long AcknowledgedRevision(ulong id)
        {
            if (id == A) return AcknowledgedRevisionA;
            if (id == B) return AcknowledgedRevisionB;
            throw new UnauthorizedAccessException();
        }
    }
}
