using System;
using System.IO;
using MreKits;

internal static class Program
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Проверка журнала не пройдена."); }
    private static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mre-kit-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new KitLedger(dir); ulong player = 123;
            Check(store.LastClaim(player) == DateTime.MinValue && !store.HasPending(player));
            store.Reserve(player);
            var afterRestart = new KitLedger(dir);
            Check(afterRestart.HasPending(player));
            bool duplicateRejected = false;
            try { afterRestart.Reserve(player); } catch (IOException) { duplicateRejected = true; }
            Check(duplicateRejected);
            afterRestart.Cancel(player); Check(!afterRestart.HasPending(player));
            afterRestart.Reserve(player);
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            afterRestart.Complete(player, now);
            Check(!afterRestart.HasPending(player) && new KitLedger(dir).LastClaim(player) == now);
            Check(new KitLedger(dir).LastClaim(456) == DateTime.MinValue);
            Console.WriteLine("Журнал наборов проверен.");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
