using System;
using System.Collections.Generic;
using System.Diagnostics;
using Steamworks;

namespace RestoreMonarchy.Teleportation.Utils
{
    internal static class BlockDeadline
    {
        internal static long AfterSeconds(double seconds) =>
            Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);

        internal static double RemainingSeconds(Dictionary<CSteamID, long> deadlines, CSteamID player) =>
            RemainingSeconds(deadlines, player, Stopwatch.GetTimestamp());

        internal static double RemainingSeconds(Dictionary<CSteamID, long> deadlines, CSteamID player, long now)
        {
            long deadline;
            if (!deadlines.TryGetValue(player, out deadline) || deadline <= now) return 0;
            return (deadline - now) / (double)Stopwatch.Frequency;
        }
    }
}
