using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SDG.Unturned;

namespace MreInventorySort
{
    internal sealed class NativeSortSurface : ISortSurface
    {
        private readonly PlayerInventory inventory;
        private readonly Items items;
        private readonly byte page;
        private readonly Func<bool> valid;
        private readonly InteractableStorage storage;
        internal static readonly MethodInfo SendRemove = typeof(PlayerInventory).GetMethod("sendItemRemove", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(byte), typeof(ItemJar) }, null);
        internal static readonly MethodInfo SendAdd = typeof(PlayerInventory).GetMethod("sendItemAdd", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(byte), typeof(ItemJar) }, null);
        internal static readonly MethodInfo Increment = typeof(PlayerInventory).GetMethod("incrementUpdateIndex", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        private readonly int width, height;

        internal NativeSortSurface(PlayerInventory inventory, byte page, Func<bool> valid, InteractableStorage storage = null)
        {
            this.inventory = inventory; this.page = page; this.valid = valid; this.storage = storage;
            items = inventory.items[page]; width = items.width; height = items.height;
            if (items.GetType() != typeof(Items)) throw new InvalidOperationException("Неизвестная реализация страницы. Сортировка отключена.");
        }
        public int Width => items.width;
        public int Height => items.height;
        public bool IsCurrent() => valid() && ReferenceEquals(inventory.items[page], items) && Width == width && Height == height;
        public SortItem[] Capture()
        {
            return items.items.Select(jar =>
            {
                var asset = jar == null ? null : jar.GetAsset();
                if (jar == null || jar.item == null || asset == null || jar.interactableItem != null ||
                    jar.size_x != asset.size_x || jar.size_y != asset.size_y)
                    throw new InvalidOperationException("Неизвестный предмет или размер. Вещи не изменены.");
                return new SortItem { JarToken = jar, ItemToken = jar.item, Id = jar.item.id,
                    X = jar.x, Y = jar.y, Rotation = jar.rot, Width = jar.rot % 2 == 0 ? jar.size_x : jar.size_y,
                    Height = jar.rot % 2 == 0 ? jar.size_y : jar.size_x, Category = (int)asset.type,
                    Amount = jar.item.amount, Quality = jar.item.quality,
                    State = jar.item.state == null ? null : (byte[])jar.item.state.Clone() };
            }).ToArray();
        }
        public void SetPosition(object jarToken, int x, int y)
        {
            var jar = jarToken as ItemJar;
            if (jar == null || !items.items.Contains(jar)) throw new InvalidOperationException("Предмет исчез во время сортировки.");
            jar.x = checked((byte)x); jar.y = checked((byte)y);
        }
        public void Rebuild()
        {
            // loadSize не вызывает callbacks, пока каждый исходный jar находится в границах.
            // Повреждённые координаты запрещены до этого вызова, в том числе при rollback.
            foreach (var jar in items.items)
            {
                int w = jar.rot % 2 == 0 ? jar.size_x : jar.size_y, h = jar.rot % 2 == 0 ? jar.size_y : jar.size_x;
                if (w == 0 || h == 0 || jar.x + w > width || jar.y + h > height)
                    throw new InvalidOperationException("Пересборка могла бы удалить предмет за границей и запрещена.");
            }
            items.loadSize((byte)width, (byte)height);
        }
        public void Persist()
        {
            if (storage != null)
            {
                if (storage.items != items || storage.onStateRebuilt != null)
                    throw new InvalidOperationException("Хранилище изменилось или перехвачено другим плагином.");
                storage.rebuildState();
            }
            Increment.Invoke(inventory, null);
        }
        public void Publish(SortPlan plan, bool restoring)
        {
            if (plan.Before.Length == 0) return;
            if (!IsCurrent())
            {
                if (restoring) return; // Закрытому/отключённому клиенту не отправляем содержимое контейнера.
                throw new InvalidOperationException("Клиент или открытое хранилище изменились до синхронизации.");
            }
            // Это только reliable server->owner RPC, не удаление серверных Item/ItemJar.
            // При rollback очищаем объединение старых и возможных новых координат клиента.
            var points = new HashSet<int>();
            foreach (var before in plan.Before)
                points.Add(before.X | (before.Y << 8));
            if (restoring) foreach (var after in plan.After) points.Add(after.X | (after.Y << 8));
            var exemplar = (ItemJar)plan.Before[0].JarToken;
            foreach (int point in points)
            {
                var envelope = new ItemJar((byte)(point & 255), (byte)(point >> 8), exemplar.rot, exemplar.item);
                SendRemove.Invoke(inventory, new object[] { page, envelope });
            }
            foreach (var jar in items.items) SendAdd.Invoke(inventory, new object[] { page, jar });
        }
    }
}
