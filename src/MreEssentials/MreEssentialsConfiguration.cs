using Rocket.API;

namespace MreEssentials
{
    public sealed class MreEssentialsConfiguration : IRocketPluginConfiguration
    {
        public bool VoteDayEnabled;
        public bool NightOnly;
        public int MinimumPlayers;
        public int RequiredPercent;
        public int VoteDurationSeconds;
        public int VoteCooldownSeconds;
        public bool AutoSaveEnabled;
        public int AutoSaveIntervalSeconds;
        public bool LocalControlEnabled;
        public string LocalControlDirectory;
        public bool MaxSkillsEnabled = true;

        public void LoadDefaults()
        {
            VoteDayEnabled = true;
            NightOnly = true;
            MinimumPlayers = 1;
            RequiredPercent = 60;
            VoteDurationSeconds = 60;
            VoteCooldownSeconds = 300;
            AutoSaveEnabled = true;
            AutoSaveIntervalSeconds = 300;
            LocalControlEnabled = true;
            LocalControlDirectory = "../control/MRE";
            MaxSkillsEnabled = true;
        }
    }
}
