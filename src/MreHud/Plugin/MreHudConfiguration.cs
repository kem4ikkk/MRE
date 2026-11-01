using Rocket.API;

namespace MreHud
{
    public sealed class MreHudConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled = true;
        // Включается только после сборки, установки bundle и проверки чистым клиентом.
        public bool AssetsVerified = false;
        public bool HideNativeHud = false;
        public ushort EffectId = 48621;
        public short EffectKey = 14821;
        public string EffectGuid = "4f11731bb53e4a55b588b97c2c2f61b5";
        public float RefreshIntervalSeconds = 0.1f;
        public string AcceptKeyLabel = "Б по умолч. · <plugin_0/>";
        public string DenyKeyLabel = "Ю по умолч. · <plugin_1/>";

        public void LoadDefaults() { }
    }
}
