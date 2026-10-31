using System;
using System.Collections.Generic;
using MreInterface;

class Program
{
    static int passed;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
    static void Main()
    {
        var rights = new HashSet<string>();
        Check(!AdminPolicy.CanExecute("kick", rights.Contains), "No rights denies");
        rights.Add("mre.admin");
        Check(!AdminPolicy.CanExecute("kick", rights.Contains), "Open menu does not authorize kick");
        rights.Clear(); rights.Add("mre.admin.kick");
        Check(!AdminPolicy.CanExecute("kick", rights.Contains), "Action alone needs parent right");
        rights.Add("mre.admin");
        Check(AdminPolicy.CanExecute("kick", rights.Contains), "Two rights authorize action");
        Check(!AdminPolicy.CanExecute("heal", rights.Contains), "No sibling rights");
        rights.Remove("mre.admin.kick");
        Check(!AdminPolicy.CanExecute("kick", rights.Contains), "Revoked right denies next call");
        Check(!AdminPolicy.CanExecute("admin", _ => true), "Unknown action denied even with wildcard");
        Check(!AdminPolicy.CanExecute("shutdown", _ => true), "Console commands unavailable");
        Check(!AdminPolicy.CanExecute(null, _ => true), "Null action denied");
        Check(!AdminPolicy.CanExecute("owner", p => p == "mre.admin" || p == "mre.admin.remove"), "Remove permission does not imply owner inspection");
        Check(AdminPolicy.CanExecute("owner", p => p == "mre.admin" || p == "mre.admin.owner") &&
            !AdminPolicy.CanExecute("remove", p => p == "mre.admin" || p == "mre.admin.owner"), "Owner inspection does not grant removal");
        var history = new RecentRecords<int>(4);
        for (int i = 1; i <= 7; i++) history.Add(i);
        var values = history.Snapshot();
        Check(values.Length == 4 && values[0] == 7 && values[3] == 4, "History bounded, newest first");
        values[0] = -1;
        Check(history.Snapshot()[0] == 7, "Caller cannot change underlying queue");
        var cancelled = new DamageCapture(100);
        Check(cancelled.Damage == 0, "Cancelled without health event records nothing");
        var normal = new DamageCapture(100); normal.Observe(72);
        Check(normal.Damage == 28, "Actual HP delta after armor");
        var overkill = new DamageCapture(10); overkill.Observe(0);
        Check(overkill.Damage == 10, "Overkill limited by initial HP");
        overkill.Observe(100);
        Check(overkill.Damage == 10, "Early revive cannot overwrite lethal HP sample");
        var unchanged = new DamageCapture(100); unchanged.Observe(100);
        Check(unchanged.Damage == 0, "Health unchanged records zero");
        var healed = new DamageCapture(50); healed.Observe(75);
        Check(healed.Damage == 0, "Healing cannot record negative damage");
        int errors = 0, later = 0;
        Action<int> handlers = _ => { throw new InvalidOperationException(); };
        handlers += value => later += value;
        SafeEventDispatch.Publish(handlers, 3, _ => errors++);
        Check(errors == 1 && later == 3, "Failing listener cannot suppress later listener");
        var confirmations = new ConfirmationStore<object>();
        var now = DateTime.UtcNow;
        var first = new object(); var replacement = new object(); object consumed;
        string token = confirmations.Issue(10, first, now);
        Check(!confirmations.TryConsume(20, token, now, out consumed), "Other actor cannot consume removal confirmation");
        Check(confirmations.TryConsume(10, token, now.AddSeconds(14), out consumed) && Object.ReferenceEquals(first, consumed), "Token selects original exact object");
        Check(!confirmations.TryConsume(10, token, now.AddSeconds(14), out consumed), "Removal confirmation cannot replay");
        token = confirmations.Issue(10, first, now);
        Check(!confirmations.TryConsume(10, token, now.AddSeconds(15), out consumed), "Exact expiry boundary denies removal");
        token = confirmations.Issue(10, first, now);
        string nextToken = confirmations.Issue(10, replacement, now);
        Check(!confirmations.TryConsume(10, token, now, out consumed), "New preview replaces prior confirmation");
        Check(!confirmations.TryConsume(10, nextToken, now, out consumed), "Wrong attempt consumes pending confirmation");
        token = confirmations.Issue(10, first, now); confirmations.Cancel(10);
        Check(!confirmations.TryConsume(10, token, now, out consumed), "Disconnect or revocation cancellation denies confirmation");
        var restoredModes = new StaffModeRestoreState(true, true);
        restoredModes.RevokeOwnGrant("god");
        Check(!restoredModes.God && restoredModes.Vanish, "Revoked separate god grant not restored after observation");
        restoredModes.RevokeOwnGrant("vanish");
        Check(!restoredModes.God && !restoredModes.Vanish, "Revoked separate vanish grant not restored after observation");
        Check(AdminPolicy.CanExecute("freecam", p => p == "mre.admin" || p == "mre.admin.fly"), "Freecam alias uses the same flight permission");
        int menuErrors = 0, menusOpened = 0;
        Func<int, bool> menuHandlers = _ => { throw new InvalidOperationException(); };
        menuHandlers += _ => false;
        menuHandlers += actor => { menusOpened += actor; return true; };
        menuHandlers += _ => { throw new Exception("Must not render a second admin menu"); };
        Check(MenuRequestDispatch.TryOpen(menuHandlers, 2, _ => menuErrors++) && menuErrors == 1 && menusOpened == 2, "Failing UI handler falls through and one successful menu wins");
        Check(!MenuRequestDispatch.TryOpen<int>(null, 2, _ => { }) && !MenuRequestDispatch.TryOpen<int>(_ => false, 2, _ => { }), "Missing or unavailable UI returns help fallback");
        Console.WriteLine("MreInterface checks PASS: " + passed);
    }
}
