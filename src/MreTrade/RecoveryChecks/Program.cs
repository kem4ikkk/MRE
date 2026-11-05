using System;
using System.IO;
using MreTrade;
using SDG.Unturned;

internal static class RecoveryChecks
{
    private static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mre-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new TradeJournal(directory);
            Require(!journal.Blocked(1), "Пустой журнал заблокирован.");
            var inventory = new PlayerInventory();
            string operation = journal.Prepare(1, 2, inventory, new PlayerInventory(), Array.Empty<TradePlacement>(), Array.Empty<TradePlacement>());
            Require(journal.Blocked(3), "Открытая операция не блокирует обмены.");
            journal.Complete(operation, 1, 2);
            Require(!journal.Blocked(1), "Завершённая операция заблокировала текущий процесс.");
            var afterRestart = new TradeJournal(directory);
            Require(afterRestart.Blocked(1) && afterRestart.Blocked(3), "После перезапуска .done не блокирует обмены.");
            File.Delete(Path.Combine(directory, operation + ".done"));
            Require(afterRestart.Blocked(1), "Удаление .done сняло блокировку без перезапуска.");
            Require(!new TradeJournal(directory).Blocked(1), "После сверки и перезапуска блокировка сохранилась.");
            string pending = Path.Combine(directory, "unfinished.pending");
            File.WriteAllText(pending, "partial");
            var interrupted = new TradeJournal(directory);
            File.Delete(pending);
            Require(interrupted.Blocked(1), "Незавершённая операция после перезапуска не требует сверки.");
            Console.WriteLine("Проверки журнала после перезапуска пройдены.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
