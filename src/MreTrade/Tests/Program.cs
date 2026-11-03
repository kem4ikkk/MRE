using System;
using MreTrade;
internal static class Program
{
    private static void Check(bool value) { if (!value) throw new Exception("Ошибка проверки обмена."); }
    private static void Main()
    {
        var t = new TradeSession(1, 2, DateTime.UtcNow.AddMinutes(2));
        bool failed = false; try { t.Accept(1); } catch (InvalidOperationException) { failed = true; } Check(failed);
        t.Accept(2); Check(!t.Confirm(1)); t.Changed(); Check(!t.ConfirmedA && !t.ConfirmedB);
        Check(!t.Confirm(2)); Check(t.Confirm(1)); t.Committing = true;
        failed = false; try { t.Confirm(2); } catch (InvalidOperationException) { failed = true; } Check(failed);
        Console.WriteLine("Состояния подтверждения обмена проверены.");
    }
}
