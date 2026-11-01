using Rocket.Core.Plugins;
using SDG.Unturned;
using UnityEngine;

namespace MreChatStyle
{
    public sealed class MreChatStylePlugin : RocketPlugin
    {
        private const string Icon = "https://raw.githubusercontent.com/kem4ikkk/MRE/994f96f53de120df31ae81d76e578ed6597371bb/assets/mre-server-logo-transparent.png";
        protected override void Load() { ChatManager.onServerSendingMessage += OnMessage; }
        protected override void Unload() { ChatManager.onServerSendingMessage -= OnMessage; }

        private static void OnMessage(ref string text, ref Color color, SteamPlayer fromPlayer,
            SteamPlayer toPlayer, EChatMode mode, ref string iconUrl, ref bool richText)
        {
            // Broadcast разворачивается игрой повторным вызовом по получателям. Сообщения
            // игроков не трогаем и не меняем получателя или канал приватного ответа.
            if (fromPlayer != null || toPlayer == null) return;
            text = ChatFormatting.Format(text, richText);
            color = Color.white;
            richText = true;
            iconUrl = Icon;
        }
    }
}
