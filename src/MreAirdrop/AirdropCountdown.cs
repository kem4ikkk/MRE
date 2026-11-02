using System;
using System.Globalization;

namespace MreAirdrop
{
    internal static class AirdropCountdown
    {
        internal static string Message(TimeSpan remaining, bool retryPending)
        {
            return retryPending
                ? "MRE: безопасная точка не найдена. Следующая проверка через " + Format(remaining) + "."
                : "MRE: запуск самолёта с аирдропом через " + Format(remaining) + ".";
        }
        internal static string Format(TimeSpan remaining)
        {
            // Доли секунды округляются вверх: ноль показывается только после срока.
            long ticks = Math.Max(0L, remaining.Ticks);
            long seconds = ticks / TimeSpan.TicksPerSecond;
            if (ticks % TimeSpan.TicksPerSecond != 0) seconds++;
            return String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}",
                seconds / 3600, seconds / 60 % 60, seconds % 60);
        }
    }
}
