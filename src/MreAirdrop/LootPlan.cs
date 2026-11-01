using System;
using System.Collections.Generic;
using System.Linq;

namespace MreAirdrop
{
    [Serializable] public sealed class LootEntry
    {
        public ushort Id;
        // Количество отдельных экземпляров, а не количество патронов в магазине.
        public byte Count;
    }
    [Serializable] public sealed class LootBundle
    {
        public string Name;
        public int Weight;
        public List<LootEntry> Items = new List<LootEntry>();
    }
    [Serializable] public sealed class DropCategory
    {
        public string Name;
        public int Weight;
        public List<LootBundle> Bundles = new List<LootBundle>();
    }
    internal static class LootPlan
    {
        private static LootEntry Entry(ushort id, byte count) { return new LootEntry { Id = id, Count = count }; }
        private static LootBundle Bundle(string name, int weight, params LootEntry[] items) { return new LootBundle { Name = name, Weight = weight, Items = items.ToList() }; }
        internal static List<DropCategory> Defaults()
        {
            return new List<DropCategory>
            {
                new DropCategory { Name = "Оружейный", Weight = 1, Bundles = new List<LootBundle> {
                    Bundle("Maplestrike", 40, Entry(363, 1), Entry(6, 3), Entry(95, 3)),
                    Bundle("Zubeknakov", 35, Entry(122, 1), Entry(123, 3), Entry(95, 3)),
                    Bundle("Timberwolf", 25, Entry(18, 1), Entry(20, 2), Entry(95, 3)) } },
                new DropCategory { Name = "Строительный", Weight = 1, Bundles = new List<LootBundle> {
                    Bundle("Листы и металл", 40, Entry(67, 6), Entry(68, 6), Entry(285, 4)),
                    Bundle("Прутья и металл", 35, Entry(67, 6), Entry(68, 4), Entry(285, 6)),
                    Bundle("Турель и материалы", 25, Entry(1372, 1), Entry(67, 5), Entry(68, 5), Entry(285, 5)) } },
                new DropCategory { Name = "Рейдовый", Weight = 1, Bundles = new List<LootBundle> {
                    Bundle("Взрывной запас", 45, Entry(254, 4), Entry(1100, 2), Entry(1241, 1), Entry(1240, 1)),
                    Bundle("Два заряда", 35, Entry(254, 3), Entry(1100, 2), Entry(1241, 2), Entry(1240, 1)),
                    Bundle("Ракетница", 20, Entry(519, 1), Entry(520, 1), Entry(254, 3), Entry(1100, 2)) } }
            };
        }
        internal static void Validate(IList<DropCategory> categories)
        {
            if (categories == null || categories.Count != 3) throw new ArgumentException("Нужны ровно три категории аирдропа.");
            var names = new HashSet<string>(categories.Where(c => c != null).Select(c => c.Name), StringComparer.Ordinal);
            if (names.Count != 3 || !names.SetEquals(new[] { "Оружейный", "Строительный", "Рейдовый" })) throw new ArgumentException("Нужны уникальные категории Оружейный, Строительный и Рейдовый.");
            foreach (var category in categories)
            {
                if (category == null || String.IsNullOrWhiteSpace(category.Name) || category.Name.Length > 80 || category.Weight <= 0 || category.Weight > 100000 || category.Bundles == null || category.Bundles.Count < 1 || category.Bundles.Count > 100) throw new ArgumentException("Некорректная категория аирдропа.");
                foreach (var bundle in category.Bundles)
                {
                    if (bundle == null || String.IsNullOrWhiteSpace(bundle.Name) || bundle.Name.Length > 80 || bundle.Weight <= 0 || bundle.Weight > 100000 || bundle.Items == null || bundle.Items.Count < 1 || bundle.Items.Count > 16) throw new ArgumentException("Некорректный комплект.");
                    int count = 0;
                    foreach (var item in bundle.Items) { if (item == null || item.Id == 0 || item.Count == 0 || item.Count > 16) throw new ArgumentException("Некорректный предмет."); count += item.Count; }
                    if (count > 16) throw new ArgumentException("В комплекте больше 16 предметов.");
                }
            }
        }
        internal static Tuple<DropCategory, LootBundle> Select(IList<DropCategory> categories, double categoryRoll, double bundleRoll)
        {
            Validate(categories);
            DropCategory category = categories[LootRoll.WeightedIndex(categories.Select(x => (double)x.Weight).ToArray(), categoryRoll)];
            LootBundle bundle = category.Bundles[LootRoll.WeightedIndex(category.Bundles.Select(x => (double)x.Weight).ToArray(), bundleRoll)];
            return Tuple.Create(category, bundle);
        }
    }
}
