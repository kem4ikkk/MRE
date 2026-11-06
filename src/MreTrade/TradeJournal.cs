using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using SDG.Unturned;

namespace MreTrade
{
    // Две блокировки и журнал обеих сторон до первой мутации.
    internal sealed class TradeJournal
    {
        private readonly string directory;
        private readonly bool recoveryRequired;
        private bool completionFailed;
        private readonly System.Action afterRename;
        internal TradeJournal(string directory, System.Action afterRename = null)
        {
            this.directory = directory;
            this.afterRename = afterRename;
            System.IO.Directory.CreateDirectory(directory);
            // .done подтверждает только выполнение в памяти, но не сохранение обоих
            // инвентарей игрой. После перезапуска даже штатно завершённый обмен
            // требует сверки; удаление файлов во время работы не снимает блокировку.
            try { recoveryRequired = Directory.EnumerateFiles(directory, "*.done").Any() || Directory.EnumerateFiles(directory, "*.pending").Any() || Directory.EnumerateFiles(directory, "*.lock").Any(); }
            catch (IOException) { recoveryRequired = true; }
            catch (UnauthorizedAccessException) { recoveryRequired = true; }
            catch (System.Security.SecurityException) { recoveryRequired = true; }
        }
        private string Lock(ulong id) { return Path.Combine(directory, id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".lock"); }
        internal bool Blocked(ulong id)
        {
            // Незавершённая операция без lock может появиться после неполного восстановления копии.
            // Не разрешать новые обмены, пока журнал целиком не сверили вручную.
            try { return recoveryRequired || completionFailed || Directory.EnumerateFiles(directory, "*.lock").Any() || Directory.EnumerateFiles(directory, "*.pending").Any(); }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
            catch (System.Security.SecurityException) { return true; }
        }
        private void Reserve(ulong id, string transaction)
        {
            using (var f = new FileStream(Lock(id), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { byte[] bytes = Encoding.ASCII.GetBytes(transaction); f.Write(bytes, 0, bytes.Length); f.Flush(true); }
        }
        internal string Prepare(ulong a, ulong b, PlayerInventory ia, PlayerInventory ib, IList<TradePlacement> toA, IList<TradePlacement> toB)
        {
            string operation = Guid.NewGuid().ToString("N"), path = Path.Combine(directory, operation + ".pending");
            ulong first = Math.Min(a, b), second = Math.Max(a, b);
            Reserve(first, operation);
            try { Reserve(second, operation); }
            catch { File.Delete(Lock(first)); throw; }
            // После успешного резервирования любые ошибки оставляют блокировки.
            byte[] payload;
            using (var memory = new MemoryStream()) using (var w = new BinaryWriter(memory, Encoding.UTF8, true))
            {
                w.Write(0x4D545244); w.Write((byte)1); w.Write(a); w.Write(b); w.Write(DateTime.UtcNow.Ticks);
                WriteInventory(w, ia); WriteInventory(w, ib); WritePlacements(w, toA); WritePlacements(w, toB); w.Flush(); payload = memory.ToArray();
            }
            if (payload.Length > 1024 * 1024) throw new InvalidDataException("Снимок обмена слишком велик.");
            byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(payload);
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(payload, 0, payload.Length); f.Write(hash, 0, hash.Length); f.Flush(true); }
            return operation;
        }
        private static void WritePlacements(BinaryWriter w, IList<TradePlacement> placements)
        {
            w.Write(placements.Count);
            foreach (var p in placements) { w.Write(p.Offer.Page); w.Write(p.Offer.X); w.Write(p.Offer.Y); w.Write(p.Page); w.Write(p.X); w.Write(p.Y); w.Write(p.Rotation); }
        }
        private static void WriteInventory(BinaryWriter w, PlayerInventory inventory)
        {
            var all = Enumerable.Range(0, PlayerInventory.STORAGE).SelectMany(page => inventory.items[page] == null ? Enumerable.Empty<Tuple<byte, ItemJar>>() : inventory.items[page].items.Select(j => Tuple.Create((byte)page, j))).ToArray();
            if (all.Length > 1000) throw new InvalidDataException("Слишком много предметов.");
            w.Write(all.Length);
            foreach (var pair in all)
            {
                var j = pair.Item2; var i = j.item;
                if (i == null || i.state == null || i.state.Length > 255) throw new InvalidDataException("Некорректное состояние предмета.");
                w.Write(pair.Item1); w.Write(j.x); w.Write(j.y); w.Write(j.rot); w.Write(i.id); w.Write(i.amount); w.Write(i.quality); w.Write((byte)i.state.Length); w.Write(i.state);
            }
        }
        internal void Complete(string operation, ulong a, ulong b)
        {
            try
            {
                File.Move(Path.Combine(directory, operation + ".pending"), Path.Combine(directory, operation + ".done"));
                if (afterRename != null) afterRename(); // Контролируемый сбой для проверки восстановления.
                File.Delete(Lock(a)); File.Delete(Lock(b));
            }
            catch
            {
                completionFailed = true;
                throw;
            }
        }
    }
}
