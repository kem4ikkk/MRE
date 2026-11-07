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
            Require(afterRestart.Blocked(3), "Блокировка .done должна быть глобальной.");
            File.Delete(Path.Combine(directory, operation + ".done"));
            Require(afterRestart.Blocked(3), "Удаление .done сняло блокировку без перезапуска.");
            Require(!new TradeJournal(directory).Blocked(3), "После ручной сверки и перезапуска блокировка сохранилась.");

            string lockDirectory = Path.Combine(directory, "lock-case");
            Directory.CreateDirectory(lockDirectory);
            File.WriteAllText(Path.Combine(lockDirectory, "1.lock"), "partial");
            var loneLock = new TradeJournal(lockDirectory);
            Require(loneLock.Blocked(1) && loneLock.Blocked(3), "Одиночный lock не блокирует все обмены.");
            File.Delete(Path.Combine(lockDirectory, "1.lock"));
            Require(loneLock.Blocked(3), "Удаление lock сняло блокировку без перезапуска.");

            string pendingDirectory = Path.Combine(directory, "pending-case");
            Directory.CreateDirectory(pendingDirectory);
            string pending = Path.Combine(pendingDirectory, "unfinished.pending");
            File.WriteAllText(pending, "partial");
            var interrupted = new TradeJournal(pendingDirectory);
            File.Delete(pending);
            Require(interrupted.Blocked(1) && interrupted.Blocked(3), "Незавершённая операция не требует глобальной сверки.");

            string failureDirectory = Path.Combine(directory, "failure-case");
            var failing = new TradeJournal(failureDirectory, () => { throw new IOException("injected after rename"); });
            string failedOperation = failing.Prepare(11, 12, new PlayerInventory(), new PlayerInventory(), Array.Empty<TradePlacement>(), Array.Empty<TradePlacement>());
            bool threw = false;
            try { failing.Complete(failedOperation, 11, 12); }
            catch (IOException) { threw = true; }
            Require(threw && File.Exists(Path.Combine(failureDirectory, failedOperation + ".done")), "Сбой после переименования не воспроизведён.");
            Require(File.Exists(Path.Combine(failureDirectory, "11.lock")) && File.Exists(Path.Combine(failureDirectory, "12.lock")), "При сбое потеряны блокировки.");
            File.Delete(Path.Combine(failureDirectory, "11.lock"));
            File.Delete(Path.Combine(failureDirectory, "12.lock"));
            Require(failing.Blocked(11) && failing.Blocked(999), "Сбой Complete не закрепил глобальную блокировку в памяти.");
            Require(new TradeJournal(failureDirectory).Blocked(999), "После перезапуска .done не блокирует новые обмены.");
            Console.WriteLine("Проверки журнала после перезапуска пройдены.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
