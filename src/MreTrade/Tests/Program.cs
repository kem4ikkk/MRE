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
        failed = false; try { t.Acknowledge(1, t.Revision); } catch (InvalidOperationException) { failed = true; } Check(failed);
        t.Accept(2); long firstRevision = t.Revision;
        failed = false; try { t.Confirm(1); } catch (InvalidOperationException) { failed = true; } Check(failed); // list required first
        Check(t.Acknowledge(1, firstRevision)); Check(t.AcknowledgedRevision(1) == firstRevision);
        Check(!t.Confirm(1));
        Check(t.ConfirmedA && !t.ConfirmedB);
        Check(!t.Confirm(1)); // duplicate confirmation remains idempotent
        failed = false; try { t.Acknowledge(3, firstRevision); } catch (InvalidOperationException) { failed = true; } Check(failed);
        failed = false; try { t.Confirm(3); } catch (InvalidOperationException) { failed = true; } Check(failed);
        Check(t.Acknowledge(2, firstRevision)); t.Changed(); Check(t.Revision == firstRevision + 1); Check(!t.ConfirmedA && !t.ConfirmedB);
        Check(t.AcknowledgedRevision(1) != t.Revision && t.AcknowledgedRevision(2) != t.Revision);
        Check(!t.Acknowledge(1, firstRevision)); // a list captured before mutation cannot acknowledge a newer revision
        failed = false; try { t.Confirm(2); } catch (InvalidOperationException) { failed = true; } Check(failed);
        Check(t.Acknowledge(2, t.Revision)); Check(!t.Confirm(2)); Check(t.Acknowledge(1, t.Revision)); Check(t.Confirm(1)); t.Committing = true;
        failed = false; try { t.Acknowledge(1, t.Revision); } catch (InvalidOperationException) { failed = true; } Check(failed);
        failed = false; try { t.Confirm(2); } catch (InvalidOperationException) { failed = true; } Check(failed);
        Console.WriteLine("Состояния подтверждения обмена проверены.");
    }
}
