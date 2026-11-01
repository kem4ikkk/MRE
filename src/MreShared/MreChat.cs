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
        // Публичный PNG с прозрачным фоном; ссылка закреплена за коммитом.
        private const string IconUrl = "https://raw.githubusercontent.com/kem4ikkk/MRE/994f96f53de120df31ae81d76e578ed6597371bb/assets/mre-server-logo-transparent.png";

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
