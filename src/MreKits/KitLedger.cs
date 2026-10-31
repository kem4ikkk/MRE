using System;
using System.Globalization;
using System.IO;

namespace MreKits
{
    // Неподтверждённая выдача блокирует повтор даже после перезапуска.
    internal sealed class KitLedger
    {
        private readonly string directory;
        internal KitLedger(string directory) { this.directory = directory; System.IO.Directory.CreateDirectory(directory); }
        private string Pending(ulong id) { return Path.Combine(directory, id.ToString(CultureInfo.InvariantCulture) + ".pending"); }
        internal DateTime LastClaim(ulong id)
        {
            long latest = 0;
            string prefix = id.ToString(CultureInfo.InvariantCulture) + ".";
            foreach (string path in System.IO.Directory.GetFiles(directory, prefix + "*.done"))
            {
                string name = Path.GetFileName(path);
                long ticks;
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || !long.TryParse(name.Substring(prefix.Length, name.Length - prefix.Length - 5), NumberStyles.None, CultureInfo.InvariantCulture, out ticks) || ticks < 1 || ticks > DateTime.MaxValue.Ticks)
                    throw new InvalidDataException("Повреждён журнал наборов.");
                if (ticks > latest) latest = ticks;
            }
            return latest == 0 ? DateTime.MinValue : new DateTime(latest, DateTimeKind.Utc);
        }
        internal bool HasPending(ulong id) { return File.Exists(Pending(id)); }
        internal void Reserve(ulong id)
        {
            using (var stream = new FileStream(Pending(id), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { byte[] bytes = System.Text.Encoding.UTF8.GetBytes("pending\n"); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        internal void Complete(ulong id, DateTime now)
        {
            string done = Path.Combine(directory, id.ToString(CultureInfo.InvariantCulture) + "." + now.Ticks.ToString(CultureInfo.InvariantCulture) + ".done");
            File.Move(Pending(id), done);
        }
        internal void Cancel(ulong id) { File.Delete(Pending(id)); }
    }
}
