using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MreStats
{
    public sealed class StatsSnapshot
    {
        public ulong SteamId { get; internal set; }
        public string Name { get; internal set; }
        public long Kills { get; internal set; }
        public long Deaths { get; internal set; }
        public long SecondsPlayed { get; internal set; }
        public long? Zombies { get; internal set; }
        public double Kd { get { return (double)Kills / Math.Max(1L, Deaths); } }
    }

    public sealed class StatsStore
    {
        private readonly string path;
        private readonly string season;
        private Dictionary<ulong, StatsSnapshot> rows = new Dictionary<ulong, StatsSnapshot>();
        private bool dirty;
        public StatsStore(string path, string season)
        {
            if (string.IsNullOrWhiteSpace(season) || season.Length > 80) throw new ArgumentException("Некорректный сезон.");
            this.path = path; this.season = season;
            if (File.Exists(path)) Load();
        }
        private void Load()
        {
            using (var f = File.OpenRead(path)) using (var r = new BinaryReader(f)) {
                string version = r.ReadString();
                if ((version != "MRE-STATS-2" && version != "MRE-STATS-3") || r.ReadString() != season) throw new InvalidDataException("Повреждены данные или сезон не совпадает: требуется явная смена сезона.");
                int count = r.ReadInt32(); if (count < 0 || count > 1000000) throw new InvalidDataException("Некорректное число игроков.");
                for (int i = 0; i < count; i++) {
                    var s = new StatsSnapshot { SteamId = r.ReadUInt64(), Kills = r.ReadInt64(), Deaths = r.ReadInt64(), SecondsPlayed = r.ReadInt64(), Zombies = r.ReadInt64() };
                    if (version == "MRE-STATS-3") { s.Name = r.ReadString(); if (s.Name.Length > 32 || s.Name != TopModel.SafeName(s.Name, s.SteamId)) throw new InvalidDataException("Некорректное имя."); }
                    if (s.SteamId == 0 || s.Kills < 0 || s.Deaths < 0 || s.SecondsPlayed < 0 || s.Zombies < 0 || rows.ContainsKey(s.SteamId)) throw new InvalidDataException("Некорректная статистика.");
                    rows.Add(s.SteamId, s);
                }
                if (f.Position != f.Length) throw new InvalidDataException("Лишние данные статистики.");
            }
        }
        private static StatsSnapshot Copy(StatsSnapshot s) { return new StatsSnapshot { SteamId=s.SteamId, Name=s.Name, Kills=s.Kills, Deaths=s.Deaths, SecondsPlayed=s.SecondsPlayed, Zombies=s.Zombies }; }
        public StatsSnapshot Snapshot(ulong id) { StatsSnapshot s; return rows.TryGetValue(id, out s) ? Copy(s) : new StatsSnapshot { SteamId=id, Zombies=0 }; }
        private StatsSnapshot Row(ulong id) { StatsSnapshot s; if (!rows.TryGetValue(id, out s)) rows.Add(id, s = new StatsSnapshot { SteamId=id, Zombies=0 }); return s; }
        public StatsSnapshot[] TopKills(int limit) { if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException("limit"); return rows.Values.OrderByDescending(x => x.Kills).ThenBy(x => x.SteamId).Take(limit).Select(Copy).ToArray(); }
        public StatsSnapshot[] Top(TopCategory category, int limit)
        {
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException("limit");
            if (!Enum.IsDefined(typeof(TopCategory), category)) throw new ArgumentOutOfRangeException("category");
            return rows.Values.OrderByDescending(x => TopModel.Value(x, category)).ThenBy(x => x.SteamId).Take(limit).Select(Copy).ToArray();
        }
        public void Zombie(ulong id) { if (id == 0) throw new ArgumentException("Неизвестный игрок."); Commit(() => { checked { Row(id).Zombies++; } }); }
        private void Commit(Action mutation)
        {
            var old = rows.ToDictionary(x => x.Key, x => Copy(x.Value));
            try { mutation(); Save(); dirty = false; } catch { rows = old; throw; }
        }
        public bool HasPending { get { return dirty; } }
        public void ObserveName(ulong id, string name) { if (id == 0) throw new ArgumentException("Неизвестный игрок."); string safe = TopModel.SafeName(name, id); var row = Row(id); if (row.Name != safe) { row.Name = safe; dirty = true; } }
        public void FlushPending() { if (!dirty) return; Save(); dirty = false; }
        public void StageDeath(ulong victim, ulong killer)
        {
            if (victim == 0) throw new ArgumentException("Неизвестная жертва.");
            var victimRow = Row(victim);
            var killerRow = killer != 0 && killer != victim ? Row(killer) : null;
            if (victimRow.Deaths == long.MaxValue || (killerRow != null && killerRow.Kills == long.MaxValue)) throw new OverflowException();
            victimRow.Deaths++; if (killerRow != null) killerRow.Kills++; dirty = true;
        }
        public void StageZombie(ulong id)
        {
            if (id == 0) throw new ArgumentException("Неизвестный игрок.");
            var row = Row(id); if (row.Zombies == long.MaxValue) throw new OverflowException();
            row.Zombies++; dirty = true;
        }
        public void Death(ulong victim, ulong killer)
        {
            if (victim == 0) throw new ArgumentException("Неизвестная жертва.");
            Commit(() => { checked { Row(victim).Deaths++; if (killer != 0 && killer != victim) Row(killer).Kills++; } });
        }
        public void AddSeconds(ulong id, long seconds)
        {
            if (id == 0 || seconds <= 0) throw new ArgumentException("Некорректное время.");
            Commit(() => { checked { Row(id).SecondsPlayed += seconds; } });
        }
        public void AddSecondsBatch(IDictionary<ulong, long> durations)
        {
            if (durations == null || durations.Any(x => x.Key == 0 || x.Value <= 0)) throw new ArgumentException("Некорректное время.");
            if (durations.Count == 0) return;
            Commit(() => { checked { foreach (var item in durations) Row(item.Key).SecondsPlayed += item.Value; } });
        }
        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) using (var w = new BinaryWriter(f)) {
                    w.Write("MRE-STATS-3"); w.Write(season); w.Write(rows.Count);
                    foreach (var s in rows.OrderBy(x => x.Key).Select(x => x.Value)) { w.Write(s.SteamId); w.Write(s.Kills); w.Write(s.Deaths); w.Write(s.SecondsPlayed); w.Write(s.Zombies.Value); w.Write(TopModel.SafeName(s.Name, s.SteamId)); }
                    w.Flush(); f.Flush(true);
                }
                if (File.Exists(path)) File.Replace(tmp, path, path + ".previous"); else File.Move(tmp, path);
            } finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
    }
}
