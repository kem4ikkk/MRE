using System.Collections.Generic;
using Rocket.API;

namespace MreAirdrop
{
    public sealed class MreAirdropConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled;
        public int IntervalSeconds;
        public List<DropCategory> Categories;
        public void LoadDefaults() { Enabled = true; IntervalSeconds = 3600; Categories = LootPlan.Defaults(); }
    }
}
