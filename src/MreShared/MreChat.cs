using System;
using System.Collections.Generic;
using Rocket.API;
using Rocket.Core.Logging;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;

namespace MreShared
{
    internal enum MreChatSection
    {
        System, Server, Home, Tpa, Kits, Trade, Vault, Vote, Airdrop, Staff, Admin
    }

    internal static class MreChat
    {
        // Until the category PNGs are published, retain the existing working server badge.
        private const string FallbackIconUrl = "https://raw.githubusercontent.com/kem4ikkk/MRE/994f96f53de120df31ae81d76e578ed6597371bb/assets/mre-server-logo-transparent.png";
        private const string MarkerStart = "\u001eMRECHAT:";
        private const int MaxBodyLength = 340;

        public static void SayAdmin(IRocketPlayer recipient, string message)
        {
            Send(recipient, MreChatSection.Admin, "[MRE-ADMIN] " + RemoveLegacyPrefix(message));
        }

        public static void SayStaff(IRocketPlayer recipient, string message)
        {
            Send(recipient, MreChatSection.Staff, "[MRE-ADMIN] " + RemoveLegacyPrefix(message));
        }

        public static void Say(string message) { Say(MreChatSection.System, message); }

        public static void SayServer(string message) { Say(MreChatSection.Server, message); }

        public static void SayServer(IRocketPlayer recipient, string message)
        {
            Send(recipient, MreChatSection.Server, message);
        }

        public static void Say(MreChatSection section, string message)
        {
            message = RemoveLegacyPrefix(message);
            Logger.Log("Broadcast: " + message, ConsoleColor.Gray);
            foreach (string part in Split(message))
                ChatManager.serverSendMessage(Marker(section) + part, Palette.SERVER, fromPlayer: null,
                    toPlayer: null, mode: EChatMode.GLOBAL, iconURL: FallbackIconUrl, useRichTextFormatting: false);
        }

        public static void Say(IRocketPlayer recipient, string message)
        {
            Send(recipient, MreChatSection.System, message);
        }

        public static void Say(MreChatSection section, IRocketPlayer recipient, string message)
        {
            Send(recipient, section, message);
        }

        private static void Send(IRocketPlayer recipient, MreChatSection section, string message)
        {
            message = RemoveLegacyPrefix(message);
            if (!(recipient is UnturnedPlayer))
            {
                if (recipient != null) UnturnedChat.Say(recipient, message);
                return;
            }

            UnturnedPlayer player = (UnturnedPlayer)recipient;
            SteamPlayer target = PlayerTool.getSteamPlayer(player.CSteamID);
            // null would broadcast to everyone, so never send a stale private response globally.
            if (target == null) return;
            foreach (string part in Split(message))
                ChatManager.serverSendMessage(Marker(section) + part, Palette.SERVER, fromPlayer: null,
                    toPlayer: target, mode: EChatMode.SAY, iconURL: FallbackIconUrl, useRichTextFormatting: false);
        }

        private static IEnumerable<string> Split(string message)
        {
            if (String.IsNullOrEmpty(message)) { yield return String.Empty; yield break; }
            int offset = 0;
            while (offset < message.Length)
            {
                int count = Math.Min(MaxBodyLength, message.Length - offset);
                if (offset + count < message.Length)
                {
                    int space = message.LastIndexOf(' ', offset + count - 1, count);
                    int newline = message.LastIndexOf('\n', offset + count - 1, count);
                    int boundary = Math.Max(space, newline);
                    if (boundary >= offset + 40) count = boundary - offset + 1;
                    if (char.IsHighSurrogate(message[offset + count - 1])) count--;
                }
                string part = message.Substring(offset, count).Trim();
                if (part.Length > 0) yield return part;
                offset += count;
            }
        }

        private static string Marker(MreChatSection section)
        {
            return MarkerStart + section.ToString().ToUpperInvariant() + "\u001e";
        }

        private static string RemoveLegacyPrefix(string message)
        {
            message = message ?? String.Empty;
            if (message.StartsWith("MRE:", StringComparison.OrdinalIgnoreCase)) return message.Substring(4).TrimStart();
            if (message.StartsWith("MRE /admin:", StringComparison.OrdinalIgnoreCase))
                return "/admin: " + message.Substring("MRE /admin:".Length).TrimStart();
            if (message.StartsWith("[MRE · НАБОРЫ]", StringComparison.OrdinalIgnoreCase))
                return message.Substring("[MRE · НАБОРЫ]".Length).TrimStart();
            return message;
        }
    }
}
