using System.Collections.Generic;
using MreShared;
using Rocket.API;
using Rocket.Unturned.Player;

namespace MreKits
{
    public sealed class CommandKits : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "kits"; } }
        public string Help { get { return "Доступные наборы."; } }
        public string Syntax { get { return ""; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.kit" }; } }
        public void Execute(IRocketPlayer caller, string[] args)
        {
            MreChat.Say(caller, "MRE: /kit start — лук, 5 стрел, одежда, спальник, 2 бинта, топор, 2 консервы и 2 воды. Раз в час.");
        }
    }
    public sealed class CommandKit : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "kit"; } }
        public string Help { get { return "Получить стартовый набор."; } }
        public string Syntax { get { return "start"; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.kit" }; } }
        public void Execute(IRocketPlayer caller, string[] args)
        {
            var player = caller as UnturnedPlayer;
            if (player == null) return;
            if (args.Length != 1 || !args[0].Equals("start", System.StringComparison.OrdinalIgnoreCase))
            { MreChat.Say(player, "MRE: использование: /kit start. Список: /kits."); return; }
            if (MreKitsPlugin.Instance == null) { MreChat.Say(player, "MRE: наборы временно недоступны."); return; }
            MreKitsPlugin.Instance.Claim(player);
        }
    }
}
