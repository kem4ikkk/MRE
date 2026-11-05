using Rocket.Core.Plugins;
using SDG.Unturned;
using UnityEngine;

namespace MreChatStyle
{
    public sealed class MreChatStylePlugin : RocketPlugin<MreChatStyleConfiguration>
    {
        public static MreChatStylePlugin Instance { get; private set; }
        private static MreChatStyleConfiguration settings;

        protected override void Load() { Instance = this; settings = Configuration.Instance; ChatManager.onServerSendingMessage += OnMessage; }
        protected override void Unload() { ChatManager.onServerSendingMessage -= OnMessage; settings = null; Instance = null; }

        private static void OnMessage(ref string text, ref Color color, SteamPlayer fromPlayer,
            SteamPlayer toPlayer, EChatMode mode, ref string iconUrl, ref bool richText)
        {
            // Broadcast ��������������� ����� ��������� ������� �� �����������. ���������
            // ������� �� ������� � �� ������ ���������� ��� ����� ���������� ������.
            // The broadcast is dispatched again per recipient; only format the recipient copy.
            if (fromPlayer != null || toPlayer == null) return;
            string category;
            text = ChatFormatting.Format(text, richText, out category);
            color = Color.white;
            richText = true;
            if (settings != null) iconUrl = settings.IconFor(category);
        }
    }
}
