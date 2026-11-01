using MreKits;

internal static class Program
{
    private static int passed;
    private static string root;
    private static readonly DateTime Now = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
    private sealed class Delivery : IKitDelivery
    {
        public bool Space = true, Success = true, Undo = true;
        public bool ThrowOnDeliver, ThrowOnRollback;
        public int Calls, Undos;
        public Action OnDeliver;
        public bool Preflight() { return Space; }
        public bool Deliver() { Calls++; OnDeliver?.Invoke(); if (ThrowOnDeliver) throw new Exception("injected delivery failure"); return Success; }
        public bool Rollback() { Undos++; if (ThrowOnRollback) throw new Exception("injected rollback failure"); return Undo; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void ExpectError(Action action)
    {
        bool threw = false;
        try { action(); } catch { threw = true; }
        Check(threw, "expected exception");
    }
    private static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS: " + name); }
    private static string PathFor(string name) { return Path.Combine(root, name, "claims.json"); }
    public static void Main()
    {
        root = Path.Combine(Path.GetTempPath(), "mre-kit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            Test("start retains legacy starter cooldown and pending", () => {
                string path = PathFor("legacy"); var l = new KitLedger(path); l.Reserve(1, "starter", Now, 86400);
                Check(!l.Available(1, KitNames.LedgerName("START"), Now.AddDays(2)), "legacy pending blocks start");
                l.Complete(1, "starter"); l = new KitLedger(path);
                Check(!l.Available(1, KitNames.LedgerName("start"), Now.AddHours(1)), "legacy cooldown preserved");
                Check(KitNames.LedgerName("other") == "other", "other kit names unchanged");
            });
            Test("successful claim persists cooldown and blocks duplicate", () => {
                string path = PathFor("success"); var ledger = new KitLedger(path); var d = new Delivery();
                Check(ClaimTransaction.Execute(ledger, 1, "starter", Now, 86400, d) == "completed", "claim result");
                Check(d.Calls == 1 && d.Undos == 0, "one delivery");
                ledger = new KitLedger(path);
                Check(ledger.Get(1, "STARTER").NextClaimUtc == Now.AddDays(1), "UTC and case normalization");
                Check(ClaimTransaction.Execute(ledger, 1, "starter", Now.AddMinutes(1), 86400, d) == "unavailable", "duplicate blocked");
                Check(d.Calls == 1, "duplicate never invokes inventory");
            });
            Test("cooldown expires at exact UTC boundary", () => {
                var l = new KitLedger(PathFor("boundary")); l.Reserve(1, "a", Now, 10); l.Complete(1, "a");
                Check(!l.Available(1, "a", Now.AddSeconds(9)), "too early");
                Check(l.Available(1, "a", Now.AddSeconds(10)), "exact boundary");
            });
            Test("cooldowns isolate players and kits", () => {
                var l = new KitLedger(PathFor("isolation")); l.Reserve(1, "a", Now, 10);
                Check(l.Available(2, "a", Now), "second player"); Check(l.Available(1, "b", Now), "second kit");
            });
            Test("inventory full reserves nothing and changes no inventory", () => {
                string path = PathFor("full"); var l = new KitLedger(path); var d = new Delivery { Space = false };
                Check(ClaimTransaction.Execute(l, 1, "a", Now, 10, d) == "inventory_full", "full result");
                Check(!File.Exists(path) && l.Get(1, "a") == null && d.Calls == 0, "no side effects");
            });
            Test("reservation is durable before inventory callback", () => {
                string path = PathFor("order"); var l = new KitLedger(path);
                var d = new Delivery { OnDeliver = () => Check(new KitLedger(path).Get(1, "a").Status == "pending", "durable reservation") };
                ClaimTransaction.Execute(l, 1, "a", Now, 10, d);
            });
            Test("crash-pending survives restart and never auto-retries", () => {
                string path = PathFor("crash"); var l = new KitLedger(path); l.Reserve(1, "a", Now, 10);
                l = new KitLedger(path); var d = new Delivery();
                Check(ClaimTransaction.Execute(l, 1, "a", Now.AddYears(1), 10, d) == "unavailable", "pending remains blocked");
                Check(d.Calls == 0, "no duplicate after crash");
            });
            Test("partial delivery rejection rolls back before releasing reservation", () => {
                string path = PathFor("partial"); var l = new KitLedger(path); var d = new Delivery { Success = false };
                ExpectError(() => ClaimTransaction.Execute(l, 1, "a", Now, 10, d));
                Check(d.Undos == 1 && new KitLedger(path).Get(1, "a") == null, "rollback and release");
            });
            Test("delivery exception also rolls back", () => {
                var l = new KitLedger(PathFor("exception")); var d = new Delivery { ThrowOnDeliver = true };
                ExpectError(() => ClaimTransaction.Execute(l, 1, "a", Now, 10, d));
                Check(d.Undos == 1 && l.Get(1, "a") == null, "rollback after exception");
            });
            Test("uncertain rollback retains pending and blocks repeat", () => {
                string path = PathFor("rollback"); var l = new KitLedger(path); var d = new Delivery { Success = false, Undo = false };
                ExpectError(() => ClaimTransaction.Execute(l, 1, "a", Now, 10, d));
                Check(new KitLedger(path).Get(1, "a").Status == "pending", "pending retained");
                Check(!l.Available(1, "a", Now.AddDays(1)), "repeat blocked");
            });
            Test("rollback exception retains durable pending", () => {
                string path = PathFor("rollbackthrow"); var l = new KitLedger(path); var d = new Delivery { Success = false, ThrowOnRollback = true };
                ExpectError(() => ClaimTransaction.Execute(l, 1, "a", Now, 10, d));
                Check(new KitLedger(path).Get(1, "a").Status == "pending", "pending on rollback crash");
            });
            Test("journal write failure never touches inventory", () => {
                string directory = Path.Combine(root, "badroot"); File.WriteAllText(directory, "file blocks directory");
                var l = new KitLedger(Path.Combine(directory, "claims.json")); var d = new Delivery();
                ExpectError(() => ClaimTransaction.Execute(l, 1, "a", Now, 10, d));
                Check(d.Calls == 0 && l.Get(1, "a") == null, "no item before write");
            });
            Test("corrupt and null journals fail closed", () => {
                string path = PathFor("corrupt"); Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "not json"); ExpectError(() => new KitLedger(path));
                File.WriteAllText(path, "null"); ExpectError(() => new KitLedger(path));
                File.WriteAllText(path, "{\"1:a\":{\"Status\":\"unknown\"}}"); ExpectError(() => new KitLedger(path));
            });
            Test("external mutation of returned record does not change ledger", () => {
                var l = new KitLedger(PathFor("snapshot")); l.Reserve(1, "a", Now, 10);
                l.Get(1, "a").Status = "completed";
                Check(l.Get(1, "a").Status == "pending", "defensive record copy");
            });
            Test("invalid UTC/cooldown rejected without journal", () => {
                string path = PathFor("invalid"); var l = new KitLedger(path);
                ExpectError(() => l.Reserve(1, "a", DateTime.SpecifyKind(Now, DateTimeKind.Local), 10));
                ExpectError(() => l.Reserve(1, "a", Now, 0));
                Check(!File.Exists(path), "invalid input never writes");
            });
            Console.WriteLine("Passed " + passed + " MreKits ledger/transaction behavioral tests. Actual Unity inventory requires in-game acceptance.");
        } finally { Directory.Delete(root, true); }
    }
}
