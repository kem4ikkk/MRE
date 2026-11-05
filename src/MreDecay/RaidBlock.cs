using System;
using System.Collections.Generic;

namespace MreDecay
{
    // Caller supplies a nonnegative monotonic timestamp in seconds.
    public sealed class RaidBlock
    {
        public const long DurationSeconds = 600;
        private readonly Dictionary<ulong, Entry> owners = new Dictionary<ulong, Entry>();
        private readonly Dictionary<ulong, Entry> groups = new Dictionary<ulong, Entry>();
        private sealed class Entry { public long At; public long Remaining; }
        private long? lastClock;
        private bool clockFault;

        private bool AcceptClock(long? now)
        {
            if (clockFault || !now.HasValue || now.Value < 0 || (lastClock.HasValue && now.Value < lastClock.Value))
            {
                clockFault = true;
                return false;
            }
            lastClock = now.Value;
            return true;
        }

        // Only call when actual player-caused damage was confirmed by the game.
        // Null or false means no qualifying event; null invalidates state fail-closed.
        public bool Record(ulong owner, ulong group, bool? confirmedPlayerDamage, long? now)
        {
            if (!AcceptClock(now) || !confirmedPlayerDamage.HasValue || owner == 0) { clockFault = true; return false; }
            if (!confirmedPlayerDamage.Value) return true;
            owners[owner] = new Entry { At = now.Value, Remaining = DurationSeconds };
            if (group != 0) groups[group] = new Entry { At = now.Value, Remaining = DurationSeconds };
            return true;
        }

        // null = unknown: repair must be denied. Boundary at 600 seconds is unblocked.
        public bool? IsBlocked(ulong owner, ulong group, long? now)
        {
            if (!AcceptClock(now) || owner == 0) { clockFault = true; return null; }
            Entry entry;
            if (owners.TryGetValue(owner, out entry) && now.Value - entry.At < entry.Remaining) return true;
            if (group != 0 && groups.TryGetValue(group, out entry) && now.Value - entry.At < entry.Remaining) return true;
            return false;
        }

        public Snapshot Capture(long? now)
        {
            if (!AcceptClock(now)) return null;
            var snapshot = new Snapshot();
            CaptureEntries(owners, snapshot.Owners, now.Value);
            CaptureEntries(groups, snapshot.Groups, now.Value);
            return snapshot;
        }

        private static void CaptureEntries(Dictionary<ulong, Entry> source, Dictionary<ulong, long> target, long now)
        {
            foreach (var pair in source) {
                long elapsed = now - pair.Value.At;
                if (elapsed < pair.Value.Remaining) target.Add(pair.Key, pair.Value.Remaining - elapsed);
            }
        }

        // Restore only into a new instance; malformed data permanently fails closed.
        public bool Restore(Snapshot snapshot, long? now)
        {
            if (lastClock.HasValue || clockFault || !AcceptClock(now) || snapshot == null ||
                !ValidEntries(snapshot.Owners) || !ValidEntries(snapshot.Groups)) { clockFault = true; return false; }
            foreach (var pair in snapshot.Owners) owners.Add(pair.Key, new Entry { At = now.Value, Remaining = pair.Value });
            foreach (var pair in snapshot.Groups) groups.Add(pair.Key, new Entry { At = now.Value, Remaining = pair.Value });
            return true;
        }

        private static bool ValidEntries(Dictionary<ulong, long> entries)
        {
            if (entries == null) return false;
            foreach (var pair in entries)
                if (pair.Key == 0 || pair.Value <= 0 || pair.Value > DurationSeconds) return false;
            return true;
        }
    }

    public sealed class Snapshot
    {
        public Dictionary<ulong, long> Owners = new Dictionary<ulong, long>();
        public Dictionary<ulong, long> Groups = new Dictionary<ulong, long>();
    }
}
