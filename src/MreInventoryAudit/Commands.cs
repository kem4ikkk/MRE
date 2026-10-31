using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using Logger=Rocket.Core.Logging.Logger;

namespace MreInventoryAudit
{
    public abstract class InventoryCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller {get{return AllowedCaller.Player;}}
        public abstract string Name {get;}
        public string Help {get{return "Административный просмотр и компенсация вещей MRE";}}
        public abstract string Syntax {get;}
        public List<string> Aliases {get{return new List<string>();}}
        public abstract List<string> Permissions {get;}
        public void Execute(IRocketPlayer caller,string[] args)
        {
            var player=caller as UnturnedPlayer;
            if(player==null||MreInventoryAuditPlugin.Instance==null)return;
            try{Run(player,args);}catch(Exception error){UnturnedChat.Say(player,"MRE: "+error.Message);Logger.LogException(error,"[MRE] Команда работы с инвентарем отклонена.");}
        }
        protected abstract void Run(UnturnedPlayer actor,string[] args);
        protected static void Print(UnturnedPlayer actor,InventoryView view)
        {
            UnturnedChat.Say(actor,"MRE: вещей "+view.Items.Length+"; снимок "+(view.SnapshotId??"текущий")+"; "+(view.RestoreStatus??"только чтение"));
            foreach(var item in view.Items.Take(30))UnturnedChat.Say(actor,"["+item.Id+"] "+item.Name+" x"+item.Amount+" / "+item.Quality+"%; "+(item.WornSlot??("страница "+item.Page)));
            if(view.Items.Length>30)UnturnedChat.Say(actor,"MRE: первые30 строк; полный список доступен в серверном API для UI.");
        }
    }
    public sealed class InspectInventoryCommand : InventoryCommand
    {
        public override string Name {get{return "inspectinventory";}}
        public override string Syntax {get{return "<SteamID64|полное имя>";}}
        public override List<string> Permissions {get{return new List<string>{"mre.inventory.inspect"};}}
        protected override void Run(UnturnedPlayer actor,string[] args) {if(args.Length==0)throw new ArgumentException("/inspectinventory "+Syntax);Print(actor,MreInventoryAuditPlugin.Instance.Inspect(actor,MreInventoryAuditPlugin.ResolveTarget(string.Join(" ",args))));}
    }
    public sealed class PreviewDeathCommand : InventoryCommand
    {
        public override string Name {get{return "previewdeath";}}
        public override string Syntax {get{return "<SteamID64|имя> [номер]";}}
        public override List<string> Permissions {get{return new List<string>{"mre.inventory.deaths"};}}
        protected override void Run(UnturnedPlayer actor,string[] args)
        {
            if(args.Length<1)throw new ArgumentException("/previewdeath "+Syntax);
            int number=0;bool numbered=args.Length>1&&Int32.TryParse(args[args.Length-1],out number);
            ulong id=MreInventoryAuditPlugin.ResolveTarget(string.Join(" ",numbered?args.Take(args.Length-1):args));
            InventoryView[] deaths=MreInventoryAuditPlugin.Instance.GetDeaths(actor,id);
            if(numbered){if(number<1||number>deaths.Length)throw new ArgumentException("Нет смерти с таким номером.");Print(actor,deaths[number-1]);}
            else {UnturnedChat.Say(actor,"MRE: снимков "+deaths.Length+". Просмотр: /previewdeath <SteamID64> <номер>.");for(int i=0;i<deaths.Length;i++)UnturnedChat.Say(actor,(i+1)+". "+deaths[i].AtUtc.ToUniversalTime().AddHours(3).ToString("dd.MM HH:mm")+" МСК / "+deaths[i].Items.Length+" вещей / "+deaths[i].RestoreStatus);}
        }
    }
    public sealed class RestoreDeathCommand : InventoryCommand
    {
        public override string Name {get{return "restoredeath";}}
        public override string Syntax {get{return "<SteamID64> <номер> | confirm <токен>";}}
        public override List<string> Permissions {get{return new List<string>{"mre.inventory.restore"};}}
        protected override void Run(UnturnedPlayer actor,string[] args)
        {
            if(args.Length==2&&args[0].Equals("confirm",StringComparison.OrdinalIgnoreCase)){UnturnedChat.Say(actor,"MRE: "+MreInventoryAuditPlugin.Instance.ConfirmRestore(actor,args[1]));return;}
            int number;ulong id;
            if(args.Length!=2||!UInt64.TryParse(args[0],out id)||!Int32.TryParse(args[1],out number))throw new ArgumentException("/restoredeath "+Syntax);
            RestorePreview preview=MreInventoryAuditPlugin.Instance.PrepareRestore(actor,id,number);
            UnturnedChat.Say(actor,"MRE: компенсация "+preview.Inventory.Items.Length+" вещей игроку "+id+". Это новая выдача; лут на месте смерти остается. Подтвердить в30с: /restoredeath confirm "+preview.Token);
        }
    }
}
