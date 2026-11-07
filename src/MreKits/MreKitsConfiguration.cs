using System.Collections.Generic;
using Rocket.API;

namespace MreKits
{
    public sealed class KitItem
    {
        public ushort Id;
        public int Count;
    }
    public sealed class KitDefinition
    {
        public string Name;
        public string Description;
        public string Permission;
        public int CooldownSeconds;
        public bool AutoEquipClothing;
        public List<KitItem> Items = new List<KitItem>();
    }
    public sealed class MreKitsConfiguration : IRocketPluginConfiguration
    {
        public List<KitDefinition> Kits = new List<KitDefinition>();
        public void LoadDefaults()
        {
            Kits = new List<KitDefinition> {
                new KitDefinition { Name = "start", Description = "Стартовый набор выживания", Permission = "mre.kit", CooldownSeconds = 86400, AutoEquipClothing = true,
                    Items = new List<KitItem> { new KitItem { Id = 353, Count = 1 }, new KitItem { Id = 348, Count = 5 },
                        new KitItem { Id = 162, Count = 1 }, new KitItem { Id = 208, Count = 1 },
                        new KitItem { Id = 294, Count = 1 }, new KitItem { Id = 95, Count = 2 },
                        new KitItem { Id = 16, Count = 1 }, new KitItem { Id = 13, Count = 2 },
                        new KitItem { Id = 14, Count = 2 } } },
                new KitDefinition { Name = "home", Description = "Базовый строительный набор", Permission = "mre.kit", CooldownSeconds = 172800, AutoEquipClothing = true,
                    Items = new List<KitItem> { new KitItem { Id = 55, Count = 1 }, new KitItem { Id = 51, Count = 1 },
                        new KitItem { Id = 57, Count = 3 }, new KitItem { Id = 53, Count = 4 },
                        new KitItem { Id = 49, Count = 1 }, new KitItem { Id = 378, Count = 1 },
                        new KitItem { Id = 1916, Count = 1 }, new KitItem { Id = 1764, Count = 1 },
                        new KitItem { Id = 328, Count = 1 }, new KitItem { Id = 290, Count = 1 },
                        new KitItem { Id = 162, Count = 1 }, new KitItem { Id = 208, Count = 1 } } },
                new KitDefinition { Name = "vip", Description = "Небольшой набор строительных материалов и расходников", Permission = "mre.kit.vip", CooldownSeconds = 86400,
                    Items = new List<KitItem> { new KitItem { Id = 55, Count = 2 }, new KitItem { Id = 51, Count = 2 },
                        new KitItem { Id = 57, Count = 4 }, new KitItem { Id = 53, Count = 2 }, new KitItem { Id = 95, Count = 2 },
                        new KitItem { Id = 13, Count = 2 }, new KitItem { Id = 14, Count = 2 } } },
                new KitDefinition { Name = "premium", Description = "Строительные материалы и расходники выживания", Permission = "mre.kit.premium", CooldownSeconds = 86400,
                    Items = new List<KitItem> { new KitItem { Id = 55, Count = 3 }, new KitItem { Id = 51, Count = 3 },
                        new KitItem { Id = 57, Count = 6 }, new KitItem { Id = 53, Count = 3 }, new KitItem { Id = 49, Count = 1 },
                        new KitItem { Id = 95, Count = 3 }, new KitItem { Id = 13, Count = 3 }, new KitItem { Id = 14, Count = 3 } } },
                new KitDefinition { Name = "elite", Description = "Расширенный строительный набор и расходники выживания", Permission = "mre.kit.elite", CooldownSeconds = 86400,
                    Items = new List<KitItem> { new KitItem { Id = 55, Count = 4 }, new KitItem { Id = 51, Count = 4 },
                        new KitItem { Id = 57, Count = 8 }, new KitItem { Id = 53, Count = 4 }, new KitItem { Id = 49, Count = 2 },
                        new KitItem { Id = 95, Count = 4 }, new KitItem { Id = 13, Count = 4 }, new KitItem { Id = 14, Count = 4 } } }
            };
        }
    }
}
