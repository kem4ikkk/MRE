using System;
using Rocket.API;
using Rocket.Core.Logging;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;

namespace MreShared
{
    internal static class MreChat
    {
        // Доступен клиентам только после публикации assets/server-chat.png в ветке.
        private const string IconUrl = "https://raw.githubusercontent.com/kem4ikkk/MRE/feature/start-kit-chat-icon/assets/server-chat.png";

        public static void Say(string message)
        {
            Logger.Log("Broadcast: " + message, ConsoleColor.Gray);
            foreach (string line in UnturnedChat.wrapMessage(message))
                ChatManager.serverSendMessage(line, Palette.SERVER, fromPlayer: null, toPlayer: null,
                    mode: EChatMode.GLOBAL, iconURL: IconUrl, useRichTextFormatting: false);
        }

        public static void Say(IRocketPlayer recipient, string message)
        {
            // Сохраняем поведение Rocket для консоли и иных типов получателей.
            if (!(recipient is UnturnedPlayer)) { UnturnedChat.Say(recipient, message); return; }
            UnturnedPlayer player = (UnturnedPlayer)recipient;
            SteamPlayer target = PlayerTool.getSteamPlayer(player.CSteamID);
            // null у serverSendMessage означает рассылку всем: не раскрывать личный ответ.
            if (target == null) return;
            foreach (string line in UnturnedChat.wrapMessage(message))
                ChatManager.serverSendMessage(line, Palette.SERVER, fromPlayer: null, toPlayer: target,
                    mode: EChatMode.SAY, iconURL: IconUrl, useRichTextFormatting: false);
        }
    }
}
