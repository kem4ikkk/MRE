using RestoreMonarchy.MoreHomes.Models;
using Rocket.API;
using System.Collections.Generic;

namespace RestoreMonarchy.MoreHomes
{
    public class MoreHomesConfiguration : IRocketPluginConfiguration
    {
        public string MessageColor { get; set; }
        public string MessageIconUrl { get; set; } = "https://i.imgur.com/9TF5aB1.png";
        public int DefaultHomeCooldown { get; set; }
        public int DefaultHomeDelay { get; set; }
        public int DefaultMaxHomes { get; set; }
        public float TeleportHeight { get; set; }
        public bool CancelOnMove { get; set; }
        public float MoveMaxDistance { get; set; }
        public bool BlockUnderground { get; set; } = false;
        public List<VIPPermission> VIPCooldowns { get; set; }
        public List<VIPPermission> VIPDelays { get; set; }
        public List<VIPPermission> VIPMaxHomes { get; set; }

        public void LoadDefaults()
        {
            MessageColor = "yellow";
            MessageIconUrl = "https://i.imgur.com/9TF5aB1.png";
            DefaultHomeCooldown = 80;
            DefaultHomeDelay = 5;
            DefaultMaxHomes = 1;
            TeleportHeight = 0.5f;
            CancelOnMove = false;
            MoveMaxDistance = 0.5f;
            BlockUnderground = true;

            VIPCooldowns = new List<VIPPermission>();
            VIPDelays = new List<VIPPermission>();
            VIPMaxHomes = new List<VIPPermission>()
            {
                new VIPPermission("morehomes.vip", 3),
                new VIPPermission("morehomes.star", 4)
            };
        }
    }
}
