using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace MreKits
{
    public sealed class ClaimRecord
    {
        public string Status;
        public DateTime ClaimedUtc;
        public DateTime NextClaimUtc;
    }

    // Все обращения идут с главного потока сервера. Pending после аварии требует сверки.
    public sealed class KitLedger
    {
        private readonly string path;
        private Dictionary<string, ClaimRecord> records;
        public KitLedger(string path)
        {
            this.path = path;
            if (File.Exists(path)) {
                JObject document;
                using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path)))) {
                    reader.DateParseHandling = DateParseHandling.None;
                    document = JObject.Load(reader);
                }
                records = new Dictionary<string, ClaimRecord>(StringComparer.Ordinal);
                foreach (JProperty entry in document.Properties()) {
                    var value = entry.Value as JObject;
                    if (value == null) throw new InvalidDataException("Поврежденная запись журнала kit.");
                    records.Add(entry.Name, new ClaimRecord { Status = (string)value["Status"],
                        ClaimedUtc = DateTime.ParseExact((string)value["ClaimedUtc"], "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                        NextClaimUtc = DateTime.ParseExact((string)value["NextClaimUtc"], "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) });
                }
                foreach (var pair in records)
                    if (pair.Value == null || (pair.Value.Status != "pending" && pair.Value.Status != "completed") ||
                        pair.Value.ClaimedUtc.Kind != DateTimeKind.Utc || pair.Value.NextClaimUtc.Kind != DateTimeKind.Utc ||
                        pair.Value.NextClaimUtc < pair.Value.ClaimedUtc)
                        throw new InvalidDataException("Поврежденная запись журнала kit.");
            } else records = new Dictionary<string, ClaimRecord>(StringComparer.Ordinal);
        }
        private static string Key(ulong steamId, string kit) { return steamId + ":" + kit.ToLowerInvariant(); }
        public ClaimRecord Get(ulong steamId, string kit)
        {
            ClaimRecord record;
            return records.TryGetValue(Key(steamId, kit), out record)
                ? new ClaimRecord { Status = record.Status, ClaimedUtc = record.ClaimedUtc, NextClaimUtc = record.NextClaimUtc } : null;
        }
        public bool Available(ulong steamId, string kit, DateTime now)
        {
            ClaimRecord record = Get(steamId, kit);
            return record == null || (record.Status == "completed" && now >= record.NextClaimUtc);
        }
        public bool Reserve(ulong steamId, string kit, DateTime now, int cooldownSeconds)
        {
            if (now.Kind != DateTimeKind.Utc || cooldownSeconds < 1) throw new ArgumentException("Некорректное время kit.");
            if (!Available(steamId, kit, now)) return false;
            var next = new Dictionary<string, ClaimRecord>(records);
            next[Key(steamId, kit)] = new ClaimRecord { Status = "pending", ClaimedUtc = now, NextClaimUtc = now.AddSeconds(cooldownSeconds) };
            Save(next);
            return true;
        }
        public void Complete(ulong steamId, string kit)
        {
            ClaimRecord record = Get(steamId, kit);
            if (record == null || record.Status != "pending") throw new InvalidOperationException("Нет резервирования kit.");
            var next = new Dictionary<string, ClaimRecord>(records);
            record.Status = "completed";
            next[Key(steamId, kit)] = record;
            Save(next);
        }
        public void Cancel(ulong steamId, string kit)
        {
            var next = new Dictionary<string, ClaimRecord>(records);
            next.Remove(Key(steamId, kit));
            Save(next);
        }
        private void Save(Dictionary<string, ClaimRecord> next)
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp." + Guid.NewGuid().ToString("N");
            try {
                var document = new JObject();
                foreach (var entry in next) document.Add(entry.Key, new JObject {
                    { "Status", entry.Value.Status },
                    { "ClaimedUtc", entry.Value.ClaimedUtc.ToString("o", CultureInfo.InvariantCulture) },
                    { "NextClaimUtc", entry.Value.NextClaimUtc.ToString("o", CultureInfo.InvariantCulture) }
                });
                byte[] bytes = new UTF8Encoding(false).GetBytes(document.ToString(Formatting.Indented));
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".previous");
                else File.Move(temp, path);
                records = next;
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
