using System;
using MreTrade;
internal static class Program
{
    private static void Check(bool value) { if (!value) throw new Exception("Ошибка проверки обмена."); }
    private static void Main()
    {
        var t = new TradeSession(1, 2, DateTime.UtcNow.AddMinutes(2));
        Check(t.Revision == 1);
        bool failed = false; try { t.Accept(1); } catch (InvalidOperationException) { failed = true; } Check(failed);
        t.Accept(2); Guid firstSession = t.SessionId; long firstRevision = t.Revision;
        Check(!t.Confirm(1, firstSession, firstRevision));
        Check(t.ConfirmedA && !t.ConfirmedB);
        Check(!t.Confirm(1, firstSession, firstRevision)); // duplicate command stays idempotent
        failed = false; try { t.Confirm(3, firstSession, firstRevision); } catch (InvalidOperationException) { failed = true; } Check(failed);
        t.Changed(); Check(t.Revision == firstRevision + 1); Check(!t.ConfirmedA && !t.ConfirmedB);
        failed = false; try { t.Confirm(2, firstSession, firstRevision); } catch (InvalidOperationException) { failed = true; } Check(failed);
        Check(!t.Confirm(2, t.SessionId, t.Revision)); Check(t.Confirm(1, t.SessionId, t.Revision)); t.Committing = true;
        failed = false; try { t.Confirm(2, firstSession, firstRevision); } catch (InvalidOperationException) { failed = true; } Check(failed);
        var next = new TradeSession(1, 2, DateTime.UtcNow.AddMinutes(2)); next.Accept(2);
        Check(next.Revision == firstRevision); Check(next.SessionId != firstSession);
        failed = false; try { next.Confirm(1, firstSession, firstRevision); } catch (InvalidOperationException) { failed = true; } Check(failed);
        Console.WriteLine("Состояния подтверждения обмена проверены.");
    }
}
