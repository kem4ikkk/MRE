using System;
using MreAirdrop;

internal static class Program
{
    private static void Check(TimeSpan time, string expected)
    {
        string actual = AirdropCountdown.Format(time);
        if (actual != expected) throw new Exception("Ожидалось " + expected + ", получено " + actual);
    }

    private static void Main()
    {
        Check(TimeSpan.Zero, "00:00:00");
        Check(TimeSpan.FromTicks(-1), "00:00:00");
        Check(TimeSpan.FromTicks(1), "00:00:01");
        Check(TimeSpan.FromSeconds(59), "00:00:59");
        Check(TimeSpan.FromMilliseconds(59001), "00:01:00");
        Check(TimeSpan.FromSeconds(2963), "00:49:23");
        Check(TimeSpan.FromHours(24), "24:00:00");
        Check(TimeSpan.FromDays(7), "168:00:00");
        Check(TimeSpan.MaxValue, "256204778:48:06");
        if (AirdropCountdown.Message(TimeSpan.FromSeconds(30), true) != "MRE: безопасная точка не найдена. Следующая проверка через 00:00:30.") throw new Exception("Неверное сообщение повтора.");
        if (AirdropCountdown.Message(TimeSpan.FromSeconds(30), false) != "MRE: запуск самолёта с аирдропом через 00:00:30.") throw new Exception("Неверное сообщение расписания.");
        Console.WriteLine("Проверки отсчёта до самолёта пройдены.");
    }
}
