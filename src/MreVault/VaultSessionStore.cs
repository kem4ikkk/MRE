using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MreVault
{
    internal sealed class VaultStoredItem
    {
        internal ushort Id;
        internal byte X, Y, Rotation, Amount, Quality;
        internal byte[] State;
    }
    internal sealed class VaultSessionStore
    {
        private readonly string root;
        internal VaultSessionStore(string root) { this.root = root; Directory.CreateDirectory(root); }
        private string Name(ulong id, string suffix) { if (id == 0) throw new ArgumentException("id"); return Path.Combine(root, id.ToString(System.Globalization.CultureInfo.InvariantCulture) + suffix); }
        internal bool Locked(ulong id) { return File.Exists(Name(id, ".pending")); }
        internal void Begin(ulong id)
        {
            using (var f = new FileStream(Name(id, ".pending"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { byte[] b = Encoding.UTF8.GetBytes("Открытая или прерванная сессия VAULT. Требуется сверка.\n"); f.Write(b, 0, b.Length); f.Flush(true); }
        }
        internal List<VaultStoredItem> Read(ulong id)
        {
            string path = Name(id, ".vault");
            if (!File.Exists(path)) return new List<VaultStoredItem>();
            byte[] all;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (f.Length < 46 || f.Length > 8192) throw new InvalidDataException("Некорректный размер VAULT.");
                all = new byte[(int)f.Length];
                int offset = 0;
                while (offset < all.Length) { int n = f.Read(all, offset, all.Length - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
            }
            int size = all.Length - 32; byte[] actual;
            using (var sha = SHA256.Create()) actual = sha.ComputeHash(all, 0, size);
            int diff = 0; for (int i = 0; i < 32; i++) diff |= actual[i] ^ all[size + i];
            if (diff != 0) throw new InvalidDataException("Неверная контрольная сумма VAULT.");
            using (var ms = new MemoryStream(all, 0, size, false)) using (var r = new BinaryReader(ms))
            {
                if (r.ReadInt32() != 0x4D565354 || r.ReadByte() != 1 || r.ReadUInt64() != id) throw new InvalidDataException("Неверный формат или владелец VAULT.");
                int count = r.ReadByte(); if (count > 16) throw new InvalidDataException("Слишком много предметов VAULT.");
                var result = new List<VaultStoredItem>();
                for (int i = 0; i < count; i++)
                {
                    var item = new VaultStoredItem { Id = r.ReadUInt16(), X = r.ReadByte(), Y = r.ReadByte(), Rotation = r.ReadByte(), Amount = r.ReadByte(), Quality = r.ReadByte() };
                    int length = r.ReadByte(); item.State = r.ReadBytes(length); if (item.State.Length != length) throw new EndOfStreamException();
                    Validate(item); result.Add(item);
                }
                if (ms.Position != ms.Length) throw new InvalidDataException("Лишние данные VAULT."); return result;
            }
        }
        internal void Write(ulong id, IList<VaultStoredItem> items)
        {
            if (!Locked(id) || items == null || items.Count > 16) throw new InvalidOperationException("Нет резервирования VAULT.");
            byte[] payload;
            using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                w.Write(0x4D565354); w.Write((byte)1); w.Write(id); w.Write((byte)items.Count);
                foreach (var i in items) { Validate(i); w.Write(i.Id); w.Write(i.X); w.Write(i.Y); w.Write(i.Rotation); w.Write(i.Amount); w.Write(i.Quality); w.Write((byte)i.State.Length); w.Write(i.State); }
                w.Flush(); payload = ms.ToArray();
            }
            byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(payload);
            string temporary = Name(id, "." + Guid.NewGuid().ToString("N") + ".tmp"), target = Name(id, ".vault");
            using (var f = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(payload, 0, payload.Length); f.Write(hash, 0, hash.Length); f.Flush(true); }
            if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
        }
        // Разрешено только после успешной фиксации состояния игрока и VAULT.
        internal void End(ulong id) { File.Delete(Name(id, ".pending")); }
        private static void Validate(VaultStoredItem item)
        {
            if (item == null || item.Id == 0 || item.X > 3 || item.Y > 3 || item.Rotation > 3 || item.Amount == 0 || item.Quality > 100 || item.State == null || item.State.Length > 255) throw new InvalidDataException("Некорректный предмет VAULT.");
        }
    }
}
