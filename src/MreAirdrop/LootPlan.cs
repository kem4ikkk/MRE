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
                    Bundle("Grizzly", 25, Entry(297, 1), Entry(1000, 1), Entry(298, 4), Entry(1003, 2), Entry(1192, 1), Entry(1193, 1)),
                    Bundle("Dragonfang", 25, Entry(132, 1), Entry(1000, 1), Entry(133, 2), Entry(1003, 2), Entry(1192, 1), Entry(1193, 1)),
                    Bundle("Timberwolf", 20, Entry(18, 1), Entry(126, 1), Entry(20, 3), Entry(127, 2), Entry(1192, 1), Entry(119, 1)),
                    Bundle("Ekho и Devil’s Bane", 20, Entry(1382, 1), Entry(1484, 1), Entry(1384, 3), Entry(1485, 2), Entry(1193, 1), Entry(113, 4)),
                    Bundle("Hell’s Fury и Matamorez", 10, Entry(1364, 1), Entry(1000, 1), Entry(1365, 1), Entry(1003, 2), Entry(1193, 2), Entry(119, 1)) } },
                new DropCategory { Name = "Строительный", Weight = 1, Bundles = new List<LootBundle> {
                    Bundle("Металлическая база", 40, Entry(1230, 1), Entry(1372, 1), Entry(371, 4), Entry(369, 2), Entry(370, 1), Entry(373, 1), Entry(285, 4)),
                    Bundle("Кирпичная база", 35, Entry(1230, 1), Entry(1215, 4), Entry(1979, 2), Entry(1210, 1), Entry(1984, 1), Entry(1212, 1), Entry(285, 3)),
                    Bundle("Укреплённая база", 25, Entry(1372, 2), Entry(1230, 1), Entry(1215, 4), Entry(1979, 2), Entry(1211, 1), Entry(1216, 1)) } },
                new DropCategory { Name = "Рейдовый", Weight = 1, Bundles = new List<LootBundle> {
                    Bundle("Рейдовый арсенал", 25, Entry(519, 1), Entry(520, 1), Entry(1240, 1), Entry(1241, 4), Entry(1100, 4)),
                    Bundle("Подрывной комплект", 25, Entry(519, 1), Entry(520, 1), Entry(1240, 1), Entry(1393, 3), Entry(1241, 3)),
                    Bundle("Ракетный комплект", 20, Entry(519, 1), Entry(520, 2), Entry(1393, 2), Entry(1240, 1), Entry(1100, 3)),
                    Bundle("Взрывные стрелы", 15, Entry(357, 1), Entry(1209, 4), Entry(1520, 3), Entry(1241, 2), Entry(1240, 1)),
                    Bundle("Штурмовой комплект", 15, Entry(363, 1), Entry(1200, 3), Entry(519, 1), Entry(520, 1), Entry(1520, 4), Entry(1240, 1), Entry(1393, 2)) } }
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
