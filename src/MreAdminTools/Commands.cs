using System;
using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;

namespace MreAdminTools
{
    public sealed class CommandGive : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "give"; } }
        public string Help { get { return "Выдать предмет подключённому игроку (административная команда MRE)."; } }
        public string Syntax { get { return "<SteamID64> <ID предмета> <количество>"; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.admin.give" }; } }

        public void Execute(IRocketPlayer caller, string[] args)
        {
            UnturnedPlayer actor = caller as UnturnedPlayer;
            MreAdminToolsPlugin plugin = MreAdminToolsPlugin.Instance;
            if (actor == null || plugin == null) return;
            try
            {
                ulong target;
                ushort item;
                byte amount;
                if (args == null || args.Length != 3 || !UInt64.TryParse(args[0], out target) || target == 0 ||
                    !UInt16.TryParse(args[1], out item) || item == 0 || !Byte.TryParse(args[2], out amount) || amount == 0)
                    throw new ArgumentException("Использование: /give " + Syntax);
                UnturnedChat.Say(actor, "MRE: " + plugin.GiveItem(actor, target, item, amount));
            }
            catch (Exception error) { UnturnedChat.Say(actor, "MRE: " + error.Message); }
        }
    }

    public sealed class CommandVehicle : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "veh"; } }
        public string Help { get { return "Создать транспорт рядом с администратором."; } }
        public string Syntax { get { return "<ID транспорта>"; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string> { "mre.admin.veh" }; } }

        public void Execute(IRocketPlayer caller, string[] args)
        {
            UnturnedPlayer actor = caller as UnturnedPlayer;
            MreAdminToolsPlugin plugin = MreAdminToolsPlugin.Instance;
            if (actor == null || plugin == null) return;
            try
            {
                ushort vehicle;
                if (args == null || args.Length != 1 || !UInt16.TryParse(args[0], out vehicle) || vehicle == 0)
                    throw new ArgumentException("Использование: /veh " + Syntax);
                UnturnedChat.Say(actor, "MRE: " + plugin.SpawnVehicle(actor, vehicle));
            }
            catch (Exception error) { UnturnedChat.Say(actor, "MRE: " + error.Message); }
        }
    }
}
