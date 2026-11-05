using Rocket.API;

namespace MreTrade
{
    public sealed class MreTradeConfiguration : IRocketPluginConfiguration
    {
        // Включать только на изолированном сервере после проверки сохранений.
        public bool ExperimentalTradeEnabled;
        // Включать только после проверки всех interceptors и клиентского bundle.
        public bool UiEnabled;
        public bool AssetsVerified;
        public void LoadDefaults() { ExperimentalTradeEnabled = false; UiEnabled = false; AssetsVerified = false; }
    }
}
