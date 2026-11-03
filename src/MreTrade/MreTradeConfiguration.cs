using Rocket.API;

namespace MreTrade
{
    public sealed class MreTradeConfiguration : IRocketPluginConfiguration
    {
        // Включать только после проверки всех interceptors и клиентского bundle.
        public bool UiEnabled;
        public bool AssetsVerified;
        public void LoadDefaults() { UiEnabled = false; AssetsVerified = false; }
    }
}
