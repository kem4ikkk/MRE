using MreShared;
using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;

namespace MreEssentials
{
    public sealed class CommandVote : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Both; } }
        public string Name { get { return "vote"; } }
        public string Help { get { return "Голосование за наступление дня: /vote day, /vote yes, /vote status."; } }
        public string Syntax { get { return "<day|yes|status>"; } }
        public List<string> Aliases { get { return new List<string> { "cv" }; } }
        public List<string> Permissions { get { return new List<string> { "mre.vote" }; } }

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var plugin = MreEssentialsPlugin.Instance;
            if (plugin == null) { MreChat.Say(caller, "MRE: плагин голосования пока недоступен."); return; }
            plugin.HandleVoteCommand(caller, command);
        }
    }
}
