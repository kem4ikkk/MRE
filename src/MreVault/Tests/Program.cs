using System;
using System.IO;
using System.Threading.Tasks;
using MreVault;

internal static class Program
{
    private static void Check(bool value) { if (!value) throw new Exception("Проверка журнала VAULT не пройдена."); }
    private static VaultSnapshot Empty(long version) { return new VaultSnapshot { Version = version, Items = new VaultItem[0] }; }
    private static VaultSnapshot One(long version, byte x = 0) { return new VaultSnapshot { Version = version, Items = new[] { new VaultItem { Id = 42, Amount = 1, Quality = 99, X = x, State = new byte[] { 0, 1, 255 } } } }; }
    private static bool Reject(Action action) { try { action(); return false; } catch (Exception e) when (e is ArgumentException || e is UnauthorizedAccessException || e is InvalidOperationException || e is IOException || e is InvalidDataException) { return true; } }
    private static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mre-vault-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(VaultAlias.IsMatch(".ы") && VaultAlias.IsMatch(" .Ы "));
            Check(!VaultAlias.IsMatch(null) && !VaultAlias.IsMatch(".ы текст") && !VaultAlias.IsMatch("/s") && !VaultAlias.IsMatch(".s"));
            var journal = new VaultJournal(dir); ulong owner = 123;
            var before = Empty(0); var after = One(1, 3); var inventory = One(0);
            Check(Reject(() => journal.Prepare(456, owner, Guid.NewGuid(), before, after, inventory, Empty(1))));
            Check(Reject(() => journal.Prepare(owner, owner, Guid.NewGuid(), before, One(1, 4), inventory, Empty(1))));
            Check(Reject(() => journal.Prepare(owner, owner, Guid.NewGuid(), before, after, Empty(0), Empty(1))));
            Check(Reject(() => journal.Prepare(owner, owner, Guid.NewGuid(), Empty(1), Empty(2), Empty(0), Empty(1))));
            var large = new VaultItem[17];
            for (int i = 0; i < large.Length; i++) large[i] = new VaultItem { Id = 1, Amount = 1, Quality = 100, X = (byte)i, State = new byte[65535] };
            Check(Reject(() => journal.Prepare(owner, owner, Guid.NewGuid(), before, Empty(1), new VaultSnapshot { Items = large }, Empty(1))));
            Check(!journal.IsBlocked(owner));
            journal.Prepare(owner, owner, Guid.NewGuid(), before, after, inventory, Empty(1));
            after.Items[0].State[0] = 99;
            var read = new VaultJournal(dir).ReadPending(owner);
            Check(read.Owner == owner && read.VaultAfter.Items[0].State[0] == 0 && read.VaultAfter.Items[0].State[2] == 255);
            Check(Reject(() => journal.Prepare(owner, owner, Guid.NewGuid(), before, One(1), inventory, Empty(1))));
            string file = Path.Combine(dir, "123.pending"); byte[] bytes = File.ReadAllBytes(file); bytes[8] ^= 1; File.WriteAllBytes(file, bytes);
            Check(Reject(() => journal.ReadPending(owner)) && journal.IsBlocked(owner));
            int successes = 0; ulong other = 456;
            Parallel.For(0, 8, i => { try { new VaultJournal(dir).Prepare(other, other, Guid.NewGuid(), Empty(0), One(1), One(0), Empty(1)); System.Threading.Interlocked.Increment(ref successes); } catch (IOException) {} });
            Check(successes == 1);
            var store = new VaultSessionStore(Path.Combine(dir, "store"));
            Check(store.Read(owner).Count == 0 && !store.Locked(owner));
            store.Begin(owner); Check(store.Locked(owner));
            Check(Reject(() => store.Begin(owner)));
            store.Write(owner, new [] { new VaultStoredItem { Id = 42, X = 3, Y = 2, Rotation = 1, Amount = 3, Quality = 94, State = new byte[] { 2, 3, 4 } } });
            Check(new VaultSessionStore(Path.Combine(dir, "store")).Locked(owner));
            var result = store.Read(owner); Check(result.Count == 1 && result[0].Id == 42 && result[0].Amount == 3 && result[0].State[2] == 4);
            store.End(owner); Check(!store.Locked(owner));
            string vaultPath = Path.Combine(dir, "store", "123.vault"); byte[] vaultBytes = File.ReadAllBytes(vaultPath);
            File.Delete(vaultPath); Check(Reject(() => store.Read(owner))); File.WriteAllBytes(vaultPath, vaultBytes);
            vaultBytes[5] ^= 1; File.WriteAllBytes(vaultPath, vaultBytes);
            Check(Reject(() => store.Read(owner)));
            Console.WriteLine("Проверки ограниченного журнала и файла VAULT пройдены.");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
