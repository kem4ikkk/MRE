using Rocket.API;

namespace RestoreMonarchy.Teleportation
{
    public class TeleportationConfiguration : IRocketPluginConfiguration
    {
        public string MessageColor { get; set; }
        public string MessageIconUrl { get; set; } = "https://raw.githubusercontent.com/kem4ikkk/MRE/59d6b05/assets/server-chat.png";
        public double TPACooldown { get; set; }
        public double TPADelay { get; set; }
        public double TPADuration { get; set; }
        public bool AllowCave { get; set; } = true;
        public bool ShouldSerializeAllowCave() => !AllowCave;
        public bool AllowRaid { get; set; }
        public double RaidDuration { get; set; }
        public bool AllowCombat { get; set; }
        public double CombatDuration { get; set; }
        public bool UseUnsafeTeleport { get; set; }
        public bool CancelOnMove { get; set; }
        public float MoveMaxDistance { get; set; }
        public bool EnablePluginHotkeys { get; set; } = true;
        public byte AcceptPluginKey { get; set; } = 0;
        public byte DenyPluginKey { get; set; } = 1;

        public void LoadDefaults()
        {
            MessageColor = "gray";
            MessageIconUrl = "https://raw.githubusercontent.com/kem4ikkk/MRE/59d6b05/assets/server-chat.png";
            TPACooldown = 100;
            TPADelay = 5;
            TPADuration = 90;
            AllowCave = true;
            AllowRaid = false;
            RaidDuration = 30;
            AllowCombat = false;
            CombatDuration = 20;
            UseUnsafeTeleport = false;
            CancelOnMove = false;
            MoveMaxDistance = 0.5f;
            EnablePluginHotkeys = true;
            AcceptPluginKey = 0;
            DenyPluginKey = 1;
        }
    }
}
