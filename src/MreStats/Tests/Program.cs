using System;
using System.IO;
using MreStats;

class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mre-stats-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            string path = Path.Combine(dir, "stats.bin");
            var store = new StatsStore(path, "s1");
            store.Death(1, 2); store.Death(1, 1); store.Death(2, 0); store.AddSeconds(1, 37);
            Check(store.Snapshot(1).Deaths == 2 && store.Snapshot(1).Kills == 0 && store.Snapshot(1).SecondsPlayed == 37, "victim/self/time");
            Check(store.Snapshot(2).Kills == 1 && store.Snapshot(2).Deaths == 1 && store.Snapshot(2).Kd == 1, "killer and kd");
            store.Zombie(2); store.Zombie(2);
            Check(store.Snapshot(2).Zombies == 2 && store.TopKills(1)[0].SteamId == 2, "zombies and ranking");
            var restored = new StatsStore(path, "s1");
            Check(restored.Snapshot(1).Deaths == 2 && restored.Snapshot(2).Kills == 1 && restored.Snapshot(1).SecondsPlayed == 37 && restored.Snapshot(2).Zombies == 2, "restart");
            bool mismatch = false; try { new StatsStore(path, "s2"); } catch (InvalidDataException) { mismatch = true; }
            Check(mismatch, "season mismatch must fail closed");
            string archive = Path.Combine(dir, "archive.bin"); File.Copy(path, archive); File.Delete(path);
            var next = new StatsStore(path, "s2"); Check(next.Snapshot(1).Deaths == 0, "new season");
            File.Copy(archive, path); Check(new StatsStore(path, "s1").Snapshot(1).Deaths == 2, "restore");
            File.WriteAllBytes(path, new byte[] { 0 }); bool corrupt = false;
            try { new StatsStore(path, "s1"); } catch (Exception e) when (e is IOException || e is FormatException || e is InvalidDataException) { corrupt = true; }
            Check(corrupt, "corruption must not reset silently");
            string blockedPath = Path.Combine(dir, "blocked"); Directory.CreateDirectory(blockedPath);
            var blocked = new StatsStore(blockedPath, "s1"); bool failed = false;
            try { blocked.AddSecondsBatch(new System.Collections.Generic.Dictionary<ulong,long> {{1, 30}, {2, 45}}); } catch (IOException) { failed = true; }
            Check(failed && blocked.Snapshot(1).SecondsPlayed == 0 && blocked.Snapshot(2).SecondsPlayed == 0, "failed batch rolls back both players");
            Directory.Delete(blockedPath); blocked.AddSecondsBatch(new System.Collections.Generic.Dictionary<ulong,long> {{1, 30}, {2, 45}});
            Check(new StatsStore(blockedPath, "s1").Snapshot(2).SecondsPlayed == 45, "retry batch persists");
            string deferredPath = Path.Combine(dir, "deferred"); Directory.CreateDirectory(deferredPath);
            var deferred = new StatsStore(deferredPath, "s1"); deferred.StageDeath(1, 2); deferred.StageZombie(2);
            Check(deferred.HasPending && deferred.Snapshot(1).Deaths == 1 && deferred.Snapshot(2).Kills == 1 && deferred.Snapshot(2).Zombies == 1, "pending snapshots");
            failed = false; try { deferred.FlushPending(); } catch (IOException) { failed = true; }
            Check(failed && deferred.HasPending && deferred.Snapshot(2).Zombies == 1, "failed flush preserves pending");
            Directory.Delete(deferredPath); deferred.FlushPending();
            var reloaded = new StatsStore(deferredPath, "s1");
            Check(!deferred.HasPending && reloaded.Snapshot(1).Deaths == 1 && reloaded.Snapshot(2).Kills == 1 && reloaded.Snapshot(2).Zombies == 1, "retry once");
            deferred.ObserveName(2, "<b>Игрок</b>\n\u202e"); deferred.FlushPending();
            Check(new StatsStore(deferredPath, "s1").Snapshot(2).Name == "bИгрок/b", "name persists without markup or controls");
            Check(TopModel.SafeName("<>\u202e\u2028\u2029", 2) == "Игрок" && TopModel.SafeName(new string('a', 100), 2).Length == 32, "safe fallback and bounded name");
            Check(deferred.Top(TopCategory.Deaths, 1)[0].SteamId == 1 && deferred.Top(TopCategory.Zombies, 1)[0].SteamId == 2 && deferred.Top(TopCategory.Kd, 1)[0].SteamId == 2, "category sorting");
            TopCategory category;
            Check(TopModel.ParseCategory(new string[0], out category) && category == TopCategory.Kills && TopModel.ParseCategory(new[] { "TIME" }, out category) && category == TopCategory.Time && !TopModel.ParseCategory(new[] { "other" }, out category) && !TopModel.ParseCategory(new[] { "kd", "x" }, out category), "category parsing");
            store.AddSeconds(2, 100); Check(store.Top(TopCategory.Time, 1)[0].SteamId == 2 && store.Top(TopCategory.Kills, 1)[0].SteamId == 2, "time and kills sorting");
            Check(TopModel.Format(store.Snapshot(2), TopCategory.Time) == "0 ч 1 мин" && TopModel.Format(store.Snapshot(2), TopCategory.Kd) == "1.00", "formatting");
            Console.WriteLine("MreStats: 18 checks passed");
        } finally { Directory.Delete(dir, true); }
    }
}
