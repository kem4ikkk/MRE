using System;
using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using UnityEngine;

namespace MreInventorySort
{
    public sealed class CommandSort : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "sort";
        public string Help => "Упорядочить личные вещи с пустой рукой или открытое хранилище";
        public string Syntax => "[inventory|storage]";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "mre.sort" };
        public void Execute(IRocketPlayer caller, string[] args)
        {
            var player = caller as UnturnedPlayer;
            if (player == null) return;
            var target = args.Length == 0 ? "inventory" : args[0].ToLowerInvariant();
            if (args.Length > 1 || (target != "inventory" && target != "storage"))
            {
                UnturnedChat.Say(player, "Использование: /sort inventory — личные вещи, /sort storage — открытое хранилище.", Color.yellow);
                return;
            }
            var plugin = MreInventorySortPlugin.Instance;
            UnturnedChat.Say(player, plugin == null ? "Сортировка недоступна." : plugin.Sort(player, target == "storage"), Color.yellow);
        }
    }
}
