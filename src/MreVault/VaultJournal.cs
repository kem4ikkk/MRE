using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MreVault
{
    // Изолированный прототип; не подключён к игровым предметам.
    public sealed class VaultItem
    {
        public ushort Id;
        public byte Amount, Quality, Page, X, Y, Rotation;
        public byte[] State;
    }
    public sealed class VaultSnapshot
    {
        public long Version;
        public VaultItem[] Items;
    }
    public sealed class PendingOperation
    {
        public ulong Owner;
        public Guid Operation;
        public VaultSnapshot VaultBefore, VaultAfter, InventoryBefore, InventoryAfter;
    }
    public sealed class VaultJournal
    {
        private const int MaxItems = 256, MaxState = 65535, MaxRecordBytes = 1024 * 1024;
        private readonly string directory;
        public VaultJournal(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("directory");
            this.directory = directory;
            System.IO.Directory.CreateDirectory(directory);
        }
        private string PathFor(ulong owner)
        {
            if (owner == 0) throw new ArgumentOutOfRangeException("owner");
            return Path.Combine(directory, owner.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".pending");
        }
        public bool IsBlocked(ulong owner) { return File.Exists(PathFor(owner)); }

        // Адаптер обязан получить согласованные снимки на главном потоке. Копии
        // защищают запись от последующих изменений вызывающей стороной.
        public void Prepare(ulong actor, ulong owner, Guid operation, VaultSnapshot vaultBefore, VaultSnapshot vaultAfter, VaultSnapshot inventoryBefore, VaultSnapshot inventoryAfter)
        {
            if (actor != owner || actor == 0) throw new UnauthorizedAccessException();
            if (operation == Guid.Empty) throw new ArgumentException("operation");
            int budget = 0;
            var record = new PendingOperation { Owner = owner, Operation = operation,
                VaultBefore = Copy(vaultBefore, ref budget), VaultAfter = Copy(vaultAfter, ref budget),
                InventoryBefore = Copy(inventoryBefore, ref budget), InventoryAfter = Copy(inventoryAfter, ref budget) };
            Validate(record);
            byte[] payload = Serialize(record);
            if (payload.Length > MaxRecordBytes) throw new InvalidDataException("Журнал операции слишком велик.");
            byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(payload);
            // CreateNew является блокировкой на уровне файловой системы. Частичная
            // запись сохраняется и блокирует повтор даже при обрыве или ошибке.
            using (var stream = new FileStream(PathFor(owner), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(payload.Length); writer.Write(payload); writer.Write(hash);
                writer.Flush(); stream.Flush(true);
            }
        }
        // Ошибка чтения никогда не снимает блокировку; файл остаётся для сверки.
        public PendingOperation ReadPending(ulong owner)
        {
            using (var stream = new FileStream(PathFor(owner), FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                int length = reader.ReadInt32();
                if (length < 1 || length > MaxRecordBytes || stream.Length != 4L + length + 32) throw new InvalidDataException("Некорректный размер журнала.");
                byte[] payload = ReadExact(reader, length), expected = ReadExact(reader, 32), actual;
                using (var sha = SHA256.Create()) actual = sha.ComputeHash(payload);
                int difference = 0; for (int i = 0; i < 32; i++) difference |= expected[i] ^ actual[i];
                if (difference != 0) throw new InvalidDataException("Контрольная сумма журнала не совпадает.");
                using (var data = new MemoryStream(payload, false))
                using (var input = new BinaryReader(data, Encoding.UTF8, true))
                {
                    if (input.ReadInt32() != 0x4D524556 || input.ReadByte() != 2) throw new InvalidDataException("Неизвестный формат журнала.");
                    var result = new PendingOperation { Owner = input.ReadUInt64(), Operation = new Guid(ReadExact(input, 16)),
                        VaultBefore = ReadSnapshot(input), VaultAfter = ReadSnapshot(input),
                        InventoryBefore = ReadSnapshot(input), InventoryAfter = ReadSnapshot(input) };
                    if (data.Position != data.Length || result.Owner != owner || result.Operation == Guid.Empty) throw new InvalidDataException("Некорректный владелец или содержимое журнала.");
                    Validate(result); return result;
                }
            }
        }
        private static byte[] ReadExact(BinaryReader reader, int length)
        {
            byte[] value = reader.ReadBytes(length); if (value.Length != length) throw new EndOfStreamException(); return value;
        }
        private static VaultSnapshot Copy(VaultSnapshot source, ref int budget)
        {
            if (source == null || source.Version < 0 || source.Items == null || source.Items.Length > MaxItems) throw new ArgumentException("Некорректный снимок.");
            var items = new VaultItem[source.Items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                VaultItem item = source.Items[i];
                if (item == null || item.State == null || item.State.Length > MaxState) throw new ArgumentException("Некорректный предмет.");
                budget = checked(budget + item.State.Length + 16);
                if (budget > MaxRecordBytes - 128) throw new InvalidDataException("Журнал операции слишком велик.");
                items[i] = new VaultItem { Id = item.Id, Amount = item.Amount, Quality = item.Quality, Page = item.Page, X = item.X, Y = item.Y, Rotation = item.Rotation, State = (byte[])item.State.Clone() };
            }
            return new VaultSnapshot { Version = source.Version, Items = items };
        }
        private static void Validate(PendingOperation r)
        {
            ValidateSnapshot(r.VaultBefore, true); ValidateSnapshot(r.VaultAfter, true);
            ValidateSnapshot(r.InventoryBefore, false); ValidateSnapshot(r.InventoryAfter, false);
            // Нет авторитетного committed state: сейчас допускается лишь первая
            // операция с пустого состояния. Следующие операции не поддерживаются.
            if (r.VaultBefore.Version != 0 || r.VaultBefore.Items.Length != 0 || r.VaultAfter.Version != 1)
                throw new InvalidOperationException("Нет подтверждённого состояния хранилища.");
            var sums = new Dictionary<string, long>();
            Accumulate(sums, r.VaultBefore, 1); Accumulate(sums, r.InventoryBefore, 1);
            Accumulate(sums, r.VaultAfter, -1); Accumulate(sums, r.InventoryAfter, -1);
            foreach (var count in sums.Values) if (count != 0) throw new InvalidOperationException("Количество или состояние предметов изменилось.");
        }
        private static void Accumulate(Dictionary<string, long> sums, VaultSnapshot snapshot, int sign)
        {
            foreach (var item in snapshot.Items)
            {
                string key = item.Id + ":" + item.Quality + ":" + Convert.ToBase64String(item.State);
                long value; sums.TryGetValue(key, out value); sums[key] = checked(value + (long)sign * item.Amount);
            }
        }
        private static void ValidateSnapshot(VaultSnapshot snapshot, bool vault)
        {
            if (snapshot == null || snapshot.Version < 0 || snapshot.Items == null || snapshot.Items.Length > MaxItems) throw new ArgumentException("Некорректный снимок.");
            var positions = new HashSet<string>();
            foreach (VaultItem item in snapshot.Items)
            {
                if (item == null || item.Id == 0 || item.Amount == 0 || item.Quality > 100 || item.Rotation > 3 || item.State == null || item.State.Length > MaxState) throw new ArgumentException("Некорректный предмет.");
                if (vault && (item.Page != 0 || item.X >= 4 || item.Y >= 4)) throw new ArgumentException("Предмет вне хранилища 4×4.");
                string pos = item.Page + ":" + item.X + ":" + item.Y;
                if (!positions.Add(pos)) throw new ArgumentException("Повторная позиция предмета.");
            }
        }
        private static byte[] Serialize(PendingOperation r)
        {
            using (var data = new MemoryStream())
            using (var writer = new BinaryWriter(data, Encoding.UTF8, true))
            {
                writer.Write(0x4D524556); writer.Write((byte)2); writer.Write(r.Owner); writer.Write(r.Operation.ToByteArray());
                WriteSnapshot(writer, r.VaultBefore); WriteSnapshot(writer, r.VaultAfter); WriteSnapshot(writer, r.InventoryBefore); WriteSnapshot(writer, r.InventoryAfter);
                writer.Flush(); return data.ToArray();
            }
        }
        private static void WriteSnapshot(BinaryWriter writer, VaultSnapshot s)
        {
            writer.Write(s.Version); writer.Write(s.Items.Length);
            foreach (VaultItem i in s.Items) { writer.Write(i.Id); writer.Write(i.Amount); writer.Write(i.Quality); writer.Write(i.Page); writer.Write(i.X); writer.Write(i.Y); writer.Write(i.Rotation); writer.Write(i.State.Length); writer.Write(i.State); }
        }
        private static VaultSnapshot ReadSnapshot(BinaryReader reader)
        {
            long version = reader.ReadInt64(); int count = reader.ReadInt32();
            if (version < 0 || count < 0 || count > MaxItems) throw new InvalidDataException("Некорректный снимок.");
            var items = new VaultItem[count];
            for (int n = 0; n < count; n++)
            {
                var item = new VaultItem { Id = reader.ReadUInt16(), Amount = reader.ReadByte(), Quality = reader.ReadByte(), Page = reader.ReadByte(), X = reader.ReadByte(), Y = reader.ReadByte(), Rotation = reader.ReadByte() };
                int size = reader.ReadInt32(); if (size < 0 || size > MaxState) throw new InvalidDataException("Некорректный размер состояния предмета.");
                item.State = ReadExact(reader, size); items[n] = item;
            }
            return new VaultSnapshot { Version = version, Items = items };
        }
    }
}
