using Rocket.API;
namespace MreImmunity
{
    public sealed class MreImmunityConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled = false;
        public bool Verified = false;
        public float RadiationSyncSeconds = 2f;
        public void LoadDefaults() { Enabled = false; Verified = false; RadiationSyncSeconds = 2f; }
    }
}
