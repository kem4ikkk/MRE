using System;
using System.Collections.Generic;

namespace MreEssentials
{
    public enum VoteResult { Added, Duplicate, NotEligible, Disconnected, Expired }
    public enum VoteOutcome { Active, Passed, Expired }
    public enum VoteChoice { Yes, No }

    // Участники фиксируются в начале; голос закрепляется за SteamID до конца сессии.
    public sealed class VoteSession
    {
        private readonly HashSet<ulong> eligible;
        private readonly Dictionary<ulong, VoteChoice> votes = new Dictionary<ulong, VoteChoice>();
        public int RequiredVotes { get; private set; }
        public int EligibleCount { get { return eligible.Count; } }
        public DateTime DeadlineUtc { get; private set; }
        public bool IsEligible(ulong player) { return eligible.Contains(player); }
        public bool HasVoted(ulong player) { return votes.ContainsKey(player); }
        public bool TryGetChoice(ulong player, out VoteChoice choice) { return votes.TryGetValue(player, out choice); }

        public VoteSession(IEnumerable<ulong> eligiblePlayers, int requiredPercent, DateTime deadlineUtc)
        {
            if (requiredPercent < 50 || requiredPercent > 100) throw new ArgumentOutOfRangeException("requiredPercent");
            eligible = new HashSet<ulong>(eligiblePlayers);
            if (eligible.Count < 1) throw new ArgumentException("Необходим минимум один участник.");
            RequiredVotes = (int)Math.Ceiling(eligible.Count * (requiredPercent / 100.0));
            DeadlineUtc = deadlineUtc;
        }

        public VoteResult TryVote(ulong player, IEnumerable<ulong> online, DateTime nowUtc)
        { return TryVote(player, online, nowUtc, VoteChoice.Yes); }

        public VoteResult TryVote(ulong player, IEnumerable<ulong> online, DateTime nowUtc, VoteChoice choice)
        {
            if (choice != VoteChoice.Yes && choice != VoteChoice.No) throw new ArgumentOutOfRangeException("choice");
            if (nowUtc >= DeadlineUtc) return VoteResult.Expired;
            if (!eligible.Contains(player)) return VoteResult.NotEligible;
            if (!new HashSet<ulong>(online).Contains(player)) return VoteResult.Disconnected;
            if (votes.ContainsKey(player)) return VoteResult.Duplicate;
            votes.Add(player, choice);
            return VoteResult.Added;
        }

        public int CountOnlineVotes(IEnumerable<ulong> online) { return CountOnlineVotes(online, VoteChoice.Yes); }
        public int CountOnlineVotes(IEnumerable<ulong> online, VoteChoice choice)
        {
            var connected = new HashSet<ulong>(online);
            int count = 0;
            foreach (var vote in votes) if (vote.Value == choice && connected.Contains(vote.Key)) count++;
            return count;
        }

        public VoteOutcome Evaluate(IEnumerable<ulong> online, DateTime nowUtc)
        {
            if (nowUtc < DeadlineUtc) return VoteOutcome.Active;
            var connected = new HashSet<ulong>(online);
            int yes = CountOnlineVotes(connected, VoteChoice.Yes), no = CountOnlineVotes(connected, VoteChoice.No);
            return yes >= RequiredVotes && yes > no ? VoteOutcome.Passed : VoteOutcome.Expired;
        }
    }
}
