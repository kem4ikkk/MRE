using System;

namespace MreEssentials
{
    // Прототип: адаптер передаёт оба состояния из одного simulation и серверное монотонное время.
    // Игровой runtime не использует этот класс.
    public sealed class VoteDoubleTapKeys
    {
        public enum Choice { None, Yes, No }
        private enum Phase { NeedRelease, Ready, FirstHeld, Released }
        private readonly byte yesKey, noKey;
        private readonly double window, maxGap;
        private object session;
        private bool hasFrame;
        private uint lastFrame;
        private double lastTime, firstPress;
        private Choice selected;
        private Phase phase = Phase.NeedRelease;

        public VoteDoubleTapKeys(byte yesKey, byte noKey, byte keyCount, double windowSeconds, double maxGapSeconds)
        {
            if (yesKey >= keyCount || noKey >= keyCount || yesKey == noKey ||
                double.IsNaN(windowSeconds) || double.IsInfinity(windowSeconds) || windowSeconds <= 0 ||
                double.IsNaN(maxGapSeconds) || double.IsInfinity(maxGapSeconds) || maxGapSeconds <= 0)
                throw new ArgumentOutOfRangeException();
            this.yesKey = yesKey; this.noKey = noKey; window = windowSeconds; maxGap = maxGapSeconds;
        }
        public byte YesKey { get { return yesKey; } }
        public byte NoKey { get { return noKey; } }

        // Вызывается при отключении, паузе, завершении и начале голосования.
        public void Reset()
        { session = null; hasFrame = false; phase = Phase.NeedRelease; selected = Choice.None; }

        public Choice Observe(object connectionSession, uint frame, double monotonicSeconds, bool yes, bool no)
        {
            if (connectionSession == null || double.IsNaN(monotonicSeconds) || double.IsInfinity(monotonicSeconds))
                throw new ArgumentException("Нужны текущая сессия и монотонное время.");
            if (!ReferenceEquals(connectionSession, session))
            { Reset(); session = connectionSession; }
            if (hasFrame && (unchecked((int)(frame - lastFrame)) <= 0 || monotonicSeconds < lastTime)) return Choice.None;
            bool gap = hasFrame && monotonicSeconds - lastTime > maxGap;
            hasFrame = true; lastFrame = frame; lastTime = monotonicSeconds;
            if (gap) { phase = Phase.NeedRelease; selected = Choice.None; }
            if (yes && no) { phase = Phase.NeedRelease; selected = Choice.None; return Choice.None; }
            Choice current = yes ? Choice.Yes : no ? Choice.No : Choice.None;
            if (phase == Phase.NeedRelease)
            { if (current == Choice.None) phase = Phase.Ready; return Choice.None; }
            if (phase == Phase.Ready)
            {
                if (current != Choice.None) { selected = current; firstPress = monotonicSeconds; phase = Phase.FirstHeld; }
                return Choice.None;
            }
            if (monotonicSeconds - firstPress > window)
            { selected = Choice.None; phase = current == Choice.None ? Phase.Ready : Phase.NeedRelease; return Choice.None; }
            if (phase == Phase.FirstHeld)
            {
                if (current == Choice.None) phase = Phase.Released;
                else if (current != selected) { phase = Phase.NeedRelease; selected = Choice.None; }
                return Choice.None;
            }
            if (current == Choice.None) return Choice.None;
            Choice result = current == selected ? current : Choice.None;
            phase = Phase.NeedRelease; selected = Choice.None;
            return result;
        }
    }
}
