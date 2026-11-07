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
        t.Accept(2); long firstRevision = t.Revision;
        string firstA = t.GetOrIssueConfirmationTicket(1), firstB = t.GetOrIssueConfirmationTicket(2);
        Check(firstA.Length == 32 && firstB.Length == 32 && firstA != firstB);
        bool complete;
        Check(!t.TryConfirm(1, firstB, out complete)); // the partner's token cannot confirm this side
        Check(t.TryConfirm(1, firstA, out complete) && !complete);
        Check(t.ConfirmedA && !t.ConfirmedB);
        Check(!t.TryConfirm(1, firstA, out complete)); // consumed tokens cannot be replayed
        Check(t.TryConfirm(2, firstB, out complete) && complete);
        t.Committing = true;
        failed = false; try { t.GetOrIssueConfirmationTicket(1); } catch (InvalidOperationException) { failed = true; } Check(failed);

        var changed = new TradeSession(11, 22, DateTime.UtcNow.AddMinutes(2)); changed.Accept(22);
        string oldRevisionToken = changed.GetOrIssueConfirmationTicket(11);
        changed.Changed(); Check(changed.Revision == 2 && !changed.ConfirmedA && !changed.ConfirmedB);
        Check(!changed.TryConfirm(11, oldRevisionToken, out complete)); // delayed click from the previous offer
        string currentRevisionToken = changed.GetOrIssueConfirmationTicket(11);
        string currentRevisionTokenB = changed.GetOrIssueConfirmationTicket(22);
        Check(currentRevisionToken != oldRevisionToken);
        Check(changed.TryConfirm(22, currentRevisionTokenB, out complete) && !complete); // a fresh ticket confirms the current revision
        Check(!changed.ConfirmedA && changed.ConfirmedB);
        changed.InvalidateConfirmationTicket(11); // UI closed or entered inventory
        Check(!changed.TryConfirm(11, currentRevisionToken, out complete));
        string reopenedUiToken = changed.GetOrIssueConfirmationTicket(11);
        Check(reopenedUiToken != currentRevisionToken);
        Check(changed.TryConfirm(11, reopenedUiToken, out complete) && complete);

        var replacement = new TradeSession(11, 22, DateTime.UtcNow.AddMinutes(2)); replacement.Accept(22);
        string replacementToken = replacement.GetOrIssueConfirmationTicket(11);
        Check(!replacement.TryConfirm(11, reopenedUiToken, out complete)); // stale callback from a closed session
        Check(replacement.TryConfirm(11, replacementToken, out complete) && !complete);
        failed = false; try { replacement.GetOrIssueConfirmationTicket(33); } catch (InvalidOperationException) { failed = true; } Check(failed);
        Console.WriteLine("Nonce подтверждения привязан к игроку, сессии, ревизии и поколению UI; устаревшие токены отклоняются.");
    }
}
