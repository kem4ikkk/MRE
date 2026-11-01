using System;
using System.Collections.Generic;
using System.Linq;

namespace MreInventorySort
{
    public sealed class SortItem
    {
        public object JarToken, ItemToken;
        public ushort Id;
        public int X, Y, Width, Height, Rotation, Category, Amount, Quality;
        public byte[] State;
    }

    public sealed class SortPlacement
    {
        public SortItem Original;
        public int X, Y;
    }

    public sealed class SortPlan
    {
        public int Width, Height;
        public SortItem[] Before;
        public SortPlacement[] After;
        public int Changed => After.Count(p => p.X != p.Original.X || p.Y != p.Original.Y);
    }

    public static class SortPlanner
    {
        public static SortPlan Plan(int width, int height, SortItem[] items)
        {
            if (width < 0 || height < 0 || width > 255 || height > 255 || width * height > 4096)
                throw new InvalidOperationException("Размер страницы не поддерживается безопасной сортировкой.");
            if (items == null || items.Length > 200) throw new InvalidOperationException("Неверное число предметов на странице.");
            var occupied = new bool[width, height];
            var jars = new HashSet<object>();
            var identities = new HashSet<object>();
            foreach (var item in items)
            {
                if (item == null || item.JarToken == null || item.ItemToken == null || !jars.Add(item.JarToken) ||
                    !identities.Add(item.ItemToken) || item.Id == 0 || item.Width < 1 || item.Height < 1 ||
                    item.Rotation < 0 || item.Rotation > 3 || item.Amount < 0 || item.Amount > 255 ||
                    item.Quality < 0 || item.Quality > 255 || item.State == null || item.State.Length > 255)
                    throw new InvalidOperationException("Данные предметов не поддерживаются безопасной сортировкой.");
                if (!Fits(occupied, item.X, item.Y, item.Width, item.Height))
                    throw new InvalidOperationException("Предметы пересекаются или выходят за границы. Сортировка отменена.");
                Fill(occupied, item.X, item.Y, item.Width, item.Height);
            }
            occupied = new bool[width, height];
            var placements = new List<SortPlacement>();
            int attempts = 0;
            // Никакого поворота, объединения стопок или переноса между страницами.
            foreach (var item in items.OrderBy(i => i.Category).ThenBy(i => i.Id)
                .ThenByDescending(i => i.Quality).ThenByDescending(i => i.Amount))
            {
                bool found = false;
                for (int y = 0; y <= height - item.Height && !found; y++)
                    for (int x = 0; x <= width - item.Width && !found; x++)
                    {
                        if (++attempts > 500000) throw new InvalidOperationException("Слишком сложная раскладка. Вещи не изменены.");
                        if (!Fits(occupied, x, y, item.Width, item.Height)) continue;
                        Fill(occupied, x, y, item.Width, item.Height);
                        placements.Add(new SortPlacement { Original = item, X = x, Y = y });
                        found = true;
                    }
                if (!found) throw new InvalidOperationException("Без изменения поворотов вещи не помещаются в новый порядок. Вещи не изменены.");
            }
            return new SortPlan { Width = width, Height = height, Before = items, After = placements.ToArray() };
        }

        private static bool Fits(bool[,] grid, int x, int y, int width, int height)
        {
            if (x < 0 || y < 0 || x + width > grid.GetLength(0) || y + height > grid.GetLength(1)) return false;
            for (int xx = x; xx < x + width; xx++) for (int yy = y; yy < y + height; yy++) if (grid[xx, yy]) return false;
            return true;
        }
        private static void Fill(bool[,] grid, int x, int y, int width, int height)
        {
            for (int xx = x; xx < x + width; xx++) for (int yy = y; yy < y + height; yy++) grid[xx, yy] = true;
        }

        public static bool Same(SortItem[] current, SortPlan plan, bool plannedPositions)
        {
            if (current == null || current.Length != plan.Before.Length) return false;
            foreach (var original in plan.Before)
            {
                var matches = current.Where(i => ReferenceEquals(i.JarToken, original.JarToken)).ToArray();
                if (matches.Length != 1) return false;
                var item = matches[0];
                if (!MetadataEqual(item, original)) return false;
                var placement = plannedPositions ? plan.After.Single(p => ReferenceEquals(p.Original, original)) : null;
                if (item.X != (placement == null ? original.X : placement.X) || item.Y != (placement == null ? original.Y : placement.Y)) return false;
            }
            return true;
        }

        public static bool SameMetadata(SortItem[] current, SortPlan plan)
        {
            if (current == null || current.Length != plan.Before.Length) return false;
            foreach (var original in plan.Before)
            {
                var matches = current.Where(i => i != null && ReferenceEquals(i.JarToken, original.JarToken)).ToArray();
                if (matches.Length != 1 || !MetadataEqual(matches[0], original)) return false;
            }
            return true;
        }
        private static bool MetadataEqual(SortItem item, SortItem original)
        {
            return ReferenceEquals(item.ItemToken, original.ItemToken) && item.Id == original.Id &&
                item.Amount == original.Amount && item.Quality == original.Quality && item.Rotation == original.Rotation &&
                item.Width == original.Width && item.Height == original.Height && item.Category == original.Category &&
                item.State != null && item.State.SequenceEqual(original.State);
        }
    }

    public interface ISortSurface
    {
        int Width { get; }
        int Height { get; }
        bool IsCurrent();
        SortItem[] Capture();
        void SetPosition(object jarToken, int x, int y);
        void Rebuild();
        void Persist();
        void Publish(SortPlan plan, bool restoring);
    }

    public sealed class SortOutcome
    {
        public bool Success, RolledBack, NeedsReview;
        public int Moved;
        public string Message;
    }

    public static class SortTransaction
    {
        public static SortOutcome Execute(ISortSurface[] surfaces)
        {
            if (surfaces == null || surfaces.Length == 0) return new SortOutcome { Success = true, Message = "Сортировать нечего." };
            var plans = new List<SortPlan>();
            try
            {
                foreach (var surface in surfaces)
                {
                    if (!surface.IsCurrent()) throw new InvalidOperationException("Инвентарь или хранилище уже недоступны.");
                    plans.Add(SortPlanner.Plan(surface.Width, surface.Height, surface.Capture()));
                }
                var allTokens = new HashSet<object>();
                foreach (var item in plans.SelectMany(p => p.Before))
                    if (!allTokens.Add(item.ItemToken)) throw new InvalidOperationException("Повторяющаяся ссылка предмета. Сортировка заблокирована.");
                for (int i = 0; i < surfaces.Length; i++)
                    if (!surfaces[i].IsCurrent() || surfaces[i].Width != plans[i].Width || surfaces[i].Height != plans[i].Height ||
                        !SortPlanner.Same(surfaces[i].Capture(), plans[i], false))
                        throw new InvalidOperationException("Содержимое изменилось во время подготовки. Попробуйте ещё раз.");
            }
            catch (Exception error) { return new SortOutcome { Message = error.Message }; }

            int moved = plans.Sum(p => p.Changed);
            if (moved == 0) return new SortOutcome { Success = true, Message = "Вещи уже упорядочены." };
            bool touched = false;
            try
            {
                for (int i = 0; i < surfaces.Length; i++)
                {
                    if (plans[i].Changed == 0) continue;
                    touched = true;
                    foreach (var placement in plans[i].After) surfaces[i].SetPosition(placement.Original.JarToken, placement.X, placement.Y);
                    surfaces[i].Rebuild();
                }
                for (int i = 0; i < surfaces.Length; i++)
                    if (!surfaces[i].IsCurrent() || !SortPlanner.Same(surfaces[i].Capture(), plans[i], true))
                        throw new InvalidOperationException("Проверка сохранности после перестановки не пройдена.");
                for (int i = 0; i < surfaces.Length; i++) if (plans[i].Changed != 0) surfaces[i].Persist();
                for (int i = 0; i < surfaces.Length; i++) if (plans[i].Changed != 0) surfaces[i].Publish(plans[i], false);
                return new SortOutcome { Success = true, Moved = moved, Message = "Вещи упорядочены. Переставлено: " + moved + "." };
            }
            catch (Exception)
            {
                bool restored = touched;
                try
                {
                    // Возвращаем только координаты. Количество, качество и state никогда не восстанавливаем поверх чужого изменения.
                    for (int i = 0; i < surfaces.Length; i++)
                        if (!SortPlanner.SameMetadata(surfaces[i].Capture(), plans[i])) throw new InvalidOperationException("Изменились сами предметы.");
                    for (int i = 0; i < surfaces.Length; i++)
                    {
                        foreach (var original in plans[i].Before) surfaces[i].SetPosition(original.JarToken, original.X, original.Y);
                        surfaces[i].Rebuild(); surfaces[i].Persist();
                    }
                    for (int i = 0; i < surfaces.Length; i++)
                        if (!SortPlanner.Same(surfaces[i].Capture(), plans[i], false)) throw new InvalidOperationException("Откат координат не подтверждён.");
                }
                catch { restored = false; }
                bool resynced = restored;
                if (restored) for (int i = 0; i < surfaces.Length; i++) try { surfaces[i].Publish(plans[i], true); } catch { resynced = false; }
                return new SortOutcome { RolledBack = restored, NeedsReview = !restored || !resynced,
                    Message = restored && resynced ? "Сортировка отменена; прежнее расположение восстановлено." :
                    "Сортировка остановлена. Переподключитесь и сообщите администрации: требуется сверка инвентаря.", };
            }
        }
    }
}
