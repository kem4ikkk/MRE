using System;

namespace MreInventorySort
{
    // Experimental policy only. No transport hook calls this class.
    // The caller must verify the game/transport version, lane and authenticated connection.
    internal sealed class SequenceFence
    {
        private readonly object connection;
        private readonly ushort lane;
        private long floor;
        private string challenge;
        private bool awaiting;

        internal SequenceFence(object connection, ushort lane)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.lane = lane;
        }

        internal void Begin(string unpredictableChallenge)
        {
            if (awaiting || string.IsNullOrEmpty(unpredictableChallenge)) throw new InvalidOperationException();
            challenge = unpredictableChallenge;
            awaiting = true;
        }

        internal bool Acknowledge(object source, ushort messageLane, long number, string response)
        {
            if (!awaiting || !ReferenceEquals(source, connection) || messageLane != lane || number <= floor ||
                !string.Equals(challenge, response, StringComparison.Ordinal)) return false;
            floor = number;
            challenge = null;
            awaiting = false;
            return true;
        }

        internal bool AllowsMutation(object source, ushort messageLane, long number)
        {
            return !awaiting && ReferenceEquals(source, connection) && messageLane == lane && number > floor;
        }
    }
}
