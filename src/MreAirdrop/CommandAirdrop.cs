using System.Collections.Generic;
using MreShared;
using Rocket.API;

namespace MreAirdrop
{
    public sealed class CommandAirdrop : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Both; } }
        public string Name { get { return "airdrop"; } }
        public string Help { get { return "Запустить случайный полный аирдроп без изменения часового расписания."; } }
        public string Syntax { get { return ""; } }
        public List<string> Aliases { get { return new List<string> { "mreairdrop" }; } }
        // Авторизация повторно выполняется в LaunchManual: native owner,
        // серверная консоль или делегированное mre.airdrop.admin.
        public List<string> Permissions { get { return new List<string>(); } }
        public void Execute(IRocketPlayer caller, string[] args)
        {
            if (args.Length != 0) { MreChat.Say(caller, "MRE: использование: /airdrop."); return; }
            var plugin = MreAirdropPlugin.Instance;
            if (plugin == null) { MreChat.Say(caller, "MRE: аирдропы временно недоступны."); return; }
            plugin.LaunchManual(caller);
        }
    }
}
