using System;
using System.Collections.Generic;
using MreShared;
using Rocket.API;

namespace MreAirdrop
{
    public sealed class CommandWhenAirdrop : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Both; } }
        public string Name { get { return "whenairdrop"; } }
        public string Help { get { return "Время до следующего запланированного аирдропа."; } }
        public string Syntax { get { return ""; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.whenairdrop" }; } }
        public void Execute(IRocketPlayer caller, string[] args)
        {
            var plugin = MreAirdropPlugin.Instance;
            if (plugin == null || !plugin.Running) { MreChat.Say(caller, "MRE: расписание аирдропов временно недоступно."); return; }
            var left = plugin.NextUtc - DateTime.UtcNow;
            MreChat.Say(caller, "MRE: следующий аирдроп примерно через " + Math.Max(0, (int)Math.Ceiling(left.TotalMinutes)) + " мин.");
        }
    }
}
