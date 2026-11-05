using System;
using System.Collections.Generic;

namespace MreTips
{
    internal sealed class TipSchedule
    {
        private readonly double interval;
        private readonly List<string> messages;
        private double next;
        private int index;

        internal TipSchedule(int seconds, IList<string> configured, double now)
        {
            if (seconds < 60 || seconds > 86400) throw new ArgumentOutOfRangeException("seconds", "Интервал подсказок должен быть от 60 до 86400 секунд.");
            if (configured == null) throw new ArgumentException("Список подсказок отсутствует.");
            messages = new List<string>();
            foreach (string raw in configured)
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                string message = raw.Trim();
                if (message.Length > 160 || message.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    throw new ArgumentException("Подсказка должна помещаться в одну строку длиной не более 160 символов.");
                messages.Add(message);
            }
            interval = seconds;
            next = now + interval;
        }

        internal string Poll(double now, bool canSend)
        {
            if (now < next) return null;
            // Следующий срок отсчитывается от текущего времени: просроченные сообщения не накапливаются.
            next = now + interval;
            if (!canSend || messages.Count == 0) return null;
            string result = messages[index];
            index = (index + 1) % messages.Count;
            return result;
        }
    }
}
