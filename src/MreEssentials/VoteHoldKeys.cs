using System;

namespace MreEssentials
{
    // Адаптер передаёт обе клавиши одного simulation кадра и монотонное серверное время.
    public sealed class VoteHoldKeys
    {
        public enum Choice { None, Yes, No }
        private readonly byte yesKey, noKey;
        private readonly double duration, maxGap;
        private object session;
        private bool hasFrame, armed, fired;
        private uint lastFrame;
        private double lastTime, started;
        private Choice held;

        public VoteHoldKeys(byte yesKey, byte noKey, byte keyCount, double durationSeconds, double maxGapSeconds)
        {
            if (yesKey >= keyCount || noKey >= keyCount || yesKey == noKey ||
                double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds <= 0 ||
                double.IsNaN(maxGapSeconds) || double.IsInfinity(maxGapSeconds) || maxGapSeconds <= 0)
                throw new ArgumentOutOfRangeException();
            this.yesKey = yesKey; this.noKey = noKey; duration = durationSeconds; maxGap = maxGapSeconds;
        }
        public byte YesKey { get { return yesKey; } }
        public byte NoKey { get { return noKey; } }

        public Choice Observe(object connectionSession, uint frame, double monotonicSeconds, bool yes, bool no)
        {
            if (connectionSession == null || double.IsNaN(monotonicSeconds) || double.IsInfinity(monotonicSeconds))
                throw new ArgumentException("Нужны текущая сессия и монотонное время.");
            if (!ReferenceEquals(connectionSession, session))
            {
                session = connectionSession; hasFrame = false; armed = false; fired = false; held = Choice.None;
            }
            if (hasFrame && (unchecked((int)(frame - lastFrame)) <= 0 || monotonicSeconds < lastTime)) return Choice.None;
            bool gap = hasFrame && monotonicSeconds - lastTime > maxGap;
            hasFrame = true; lastFrame = frame; lastTime = monotonicSeconds;
            if (gap) { armed = false; fired = false; held = Choice.None; }
            if (!yes && !no) { armed = true; fired = false; held = Choice.None; return Choice.None; }
            if (yes && no) { armed = false; fired = false; held = Choice.None; return Choice.None; }
            if (!armed || fired) return Choice.None;
            Choice current = yes ? Choice.Yes : Choice.No;
            if (held != current) { held = current; started = monotonicSeconds; return Choice.None; }
            if (monotonicSeconds - started < duration) return Choice.None;
            fired = true;
            return current;
        }
    }
}
