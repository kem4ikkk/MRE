using MreShared;
using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;

namespace MreInterface
{
    public sealed class CommandAdmin : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "admin"; } }
        public string Help { get { return "Управление MRE: /admin help"; } }
        public string Syntax { get { return "[help|players|tp|bring|heal|kick|kill|freeze|save|day|night|fly|freecam|spectate|god|vanish|workzone|stats|door|storage|remove] [цель|on|off]"; } }
        public List<string> Aliases { get { return new List<string> { "mreadmin" }; } }
        // Проверки выполняются внутри плагина; снятие собственного режима доступно
        // даже после отзыва mre.admin. Пустой список не разрешает другие действия.
        public List<string> Permissions { get { return new List<string>(); } }
        public void Execute(IRocketPlayer caller, string[] args)
        {
            var player = caller as UnturnedPlayer;
            var plugin = MreInterfacePlugin.Instance;
            if (plugin == null || player == null) { MreChat.Say(caller, "MRE: админка доступна только в игре."); return; }
            plugin.HandleAdminCommand(player, args);
        }
    }
}
