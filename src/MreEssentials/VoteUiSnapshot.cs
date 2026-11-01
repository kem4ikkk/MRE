using System;
using System.Collections.Generic;

namespace MreEssentials
{
    // Неизменяемый снимок: чтение HUD не добавляет голоса и не завершает голосование.
    public sealed class VoteUiSnapshot
    {
        public string Phase { get; private set; }
        public string Topic { get; private set; }
        public string InitiatorName { get; private set; }
        public int YesVotes { get; private set; }
        public int RequiredVotes { get; private set; }
        public int EligibleCount { get; private set; }
        public double RemainingSeconds { get; private set; }
        public double DurationSeconds { get; private set; }
        public bool CanVote { get; private set; }
        public bool HasVoted { get; private set; }

        public static VoteUiSnapshot Capture(VoteSession session, IEnumerable<ulong> online,
            ulong viewer, string initiator, double duration, DateTime nowUtc)
        {
            if (session == null) return null;
            var connected = new HashSet<ulong>(online);
            double remaining = Math.Max(0, (session.DeadlineUtc - nowUtc).TotalSeconds);
            return new VoteUiSnapshot {
                Phase = "active", Topic = "Наступление дня", InitiatorName = initiator ?? "Игрок",
                YesVotes = session.CountOnlineVotes(connected), RequiredVotes = session.RequiredVotes,
                EligibleCount = session.EligibleCount, RemainingSeconds = remaining,
                DurationSeconds = Math.Max(1, duration), HasVoted = session.HasVoted(viewer),
                CanVote = remaining > 0 && session.IsEligible(viewer) && !session.HasVoted(viewer) && connected.Contains(viewer)
            };
        }

        public VoteUiSnapshot WithPhase(string phase)
        {
            if (phase != "passed" && phase != "expired" && phase != "canceled") throw new ArgumentOutOfRangeException("phase");
            return new VoteUiSnapshot {
                Phase = phase, Topic = Topic, InitiatorName = InitiatorName,
                YesVotes = YesVotes, RequiredVotes = RequiredVotes, EligibleCount = EligibleCount,
                RemainingSeconds = 0, DurationSeconds = DurationSeconds, HasVoted = HasVoted, CanVote = false
            };
        }
    }
}
