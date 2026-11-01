using System;
using System.Collections.Generic;

namespace MreEssentials
{
    public enum VoteResult { Added, Duplicate, NotEligible, Disconnected, Expired }
    public enum VoteOutcome { Active, Passed, Expired }

    // Чистая логика кворума; не зависит от игровых DLL или системного времени.
    public sealed class VoteSession
    {
        private readonly HashSet<ulong> eligible;
        private readonly HashSet<ulong> votes = new HashSet<ulong>();
        public int RequiredVotes { get; private set; }
        public int EligibleCount { get { return eligible.Count; } }
        public DateTime DeadlineUtc { get; private set; }
        public bool IsEligible(ulong player) { return eligible.Contains(player); }
        public bool HasVoted(ulong player) { return votes.Contains(player); }

        public VoteSession(IEnumerable<ulong> eligiblePlayers, int requiredPercent, DateTime deadlineUtc)
        {
            if (requiredPercent < 50 || requiredPercent > 100)
                throw new ArgumentOutOfRangeException("requiredPercent");
            eligible = new HashSet<ulong>(eligiblePlayers);
            if (eligible.Count < 1) throw new ArgumentException("Необходим минимум один участник.");
            RequiredVotes = (int)Math.Ceiling(eligible.Count * (requiredPercent / 100.0));
            DeadlineUtc = deadlineUtc;
        }

        public VoteResult TryVote(ulong player, IEnumerable<ulong> online, DateTime nowUtc)
        {
            if (nowUtc >= DeadlineUtc) return VoteResult.Expired;
            if (!eligible.Contains(player)) return VoteResult.NotEligible;
            if (!new HashSet<ulong>(online).Contains(player)) return VoteResult.Disconnected;
            if (!votes.Add(player)) return VoteResult.Duplicate;
            return VoteResult.Added;
        }

        public int CountOnlineVotes(IEnumerable<ulong> online)
        {
            var connected = new HashSet<ulong>(online);
            int count = 0;
            foreach (ulong voter in votes) if (connected.Contains(voter)) count++;
            return count;
        }

        public VoteOutcome Evaluate(IEnumerable<ulong> online, DateTime nowUtc)
        {
            if (nowUtc >= DeadlineUtc) return VoteOutcome.Expired;
            return CountOnlineVotes(online) >= RequiredVotes ? VoteOutcome.Passed : VoteOutcome.Active;
        }
    }
}
