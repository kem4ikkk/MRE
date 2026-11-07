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
        internal readonly Guid SessionId;
        internal long Revision { get; private set; }
        internal bool Accepted, ConfirmedA, ConfirmedB, Committing;
        internal readonly List<TradeOffer> OffersA = new List<TradeOffer>();
        internal readonly List<TradeOffer> OffersB = new List<TradeOffer>();
        private sealed class ConfirmationTicket
        {
            internal Guid SessionId;
            internal long Revision, Generation;
            internal string Nonce;
        }
        private ConfirmationTicket ticketA, ticketB;
        private long generationA, generationB;
        internal TradeSession(ulong a, ulong b, DateTime expiresUtc)
        {
            if (a == 0 || b == 0 || a == b) throw new ArgumentException("Нужны два разных игрока.");
            A = a; B = b; ExpiresUtc = expiresUtc; SessionId = Guid.NewGuid();
            Revision = 1;
        }
        internal bool Contains(ulong id) { return id == A || id == B; }
        internal ulong Other(ulong id) { if (id == A) return B; if (id == B) return A; throw new UnauthorizedAccessException(); }
        internal List<TradeOffer> Own(ulong id) { if (id == A) return OffersA; if (id == B) return OffersB; throw new UnauthorizedAccessException(); }
        internal void Accept(ulong id) { if (id != B || Accepted || Committing) throw new InvalidOperationException(); Accepted = true; InvalidateConfirmationTicket(A); InvalidateConfirmationTicket(B); }
        internal void Changed()
        {
            if (!Accepted || Committing) throw new InvalidOperationException();
            Revision = checked(Revision + 1);
            ConfirmedA = false;
            ConfirmedB = false;
            InvalidateConfirmationTicket(A);
            InvalidateConfirmationTicket(B);
        }
        internal string GetOrIssueConfirmationTicket(ulong id)
        {
            if (!Accepted || Committing || !Contains(id)) throw new InvalidOperationException();
            if (IsConfirmed(id)) return null;
            ConfirmationTicket current = id == A ? ticketA : ticketB;
            long generation = Generation(id);
            if (current != null && current.SessionId == SessionId && current.Revision == Revision && current.Generation == generation)
                return current.Nonce;
            var ticket = new ConfirmationTicket { SessionId = SessionId, Revision = Revision, Generation = generation, Nonce = Guid.NewGuid().ToString("N") };
            if (id == A) ticketA = ticket; else ticketB = ticket;
            return ticket.Nonce;
        }
        internal void InvalidateConfirmationTicket(ulong id)
        {
            if (!Contains(id)) throw new UnauthorizedAccessException();
            if (id == A) { generationA = checked(generationA + 1); ticketA = null; }
            else { generationB = checked(generationB + 1); ticketB = null; }
        }
        // The nonce is captured by the UI at click time and is bound to this session, revision and UI generation.
        internal bool TryConfirm(ulong id, string nonce, out bool complete)
        {
            complete = false;
            if (!Accepted || Committing || !Contains(id)) throw new InvalidOperationException();
            if (IsConfirmed(id) || String.IsNullOrEmpty(nonce)) return false;
            ConfirmationTicket ticket = id == A ? ticketA : ticketB;
            if (ticket == null || ticket.SessionId != SessionId || ticket.Revision != Revision || ticket.Generation != Generation(id) ||
                !String.Equals(ticket.Nonce, nonce, StringComparison.Ordinal)) return false;
            if (id == A) { ConfirmedA = true; ticketA = null; generationA = checked(generationA + 1); }
            else { ConfirmedB = true; ticketB = null; generationB = checked(generationB + 1); }
            complete = ConfirmedA && ConfirmedB;
            return true;
        }
        private long Generation(ulong id) { return id == A ? generationA : id == B ? generationB : throw new UnauthorizedAccessException(); }
        private bool IsConfirmed(ulong id) { return id == A ? ConfirmedA : id == B ? ConfirmedB : throw new UnauthorizedAccessException(); }
    }
}
