using MreShared;
using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using Logger=Rocket.Core.Logging.Logger;

namespace MreModeration
{
    public sealed class StaffCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller {get{return AllowedCaller.Player;}}
        public string Name {get{return "staff";}}
        public string Help {get{return "Профили, история и модерация MRE: /staff help";}}
        public string Syntax {get{return "<действие> [SteamID64] [длительность] [причина]";}}
        public List<string> Aliases {get{return new List<string>{"mrestaff"};}}
        public List<string> Permissions {get{return new List<string>{"mre.staff"};}}
        public void Execute(IRocketPlayer caller,string[] args)
        {
            var actor=caller as UnturnedPlayer;var plugin=MreModerationPlugin.Instance;if(actor==null||plugin==null)return;
            try {
                if(args.Length==0||args[0].Equals("help",StringComparison.OrdinalIgnoreCase)) {Say(actor,"Действия: "+String.Join(", ",plugin.GetStaffActions(actor))+". Для действий с причиной используйте SteamID64; ban/mute: <ID> <30m|2h|7d|perm> <причина>. revoke: <ID> <id записи> <причина>.");return;}
                string action=args[0].ToLowerInvariant();
                if(action=="list") {var page=plugin.SearchProfiles(actor,args.Length>1?String.Join(" ",args.Skip(1).ToArray()):null,null,0,20);Say(actor,"Профилей: "+page.Total+". Первые "+page.Items.Length+"; полный поиск — API панели.");foreach(var item in page.Items)Say(actor,item.SteamId+" | "+item.Name+" | trust "+item.Trustscore);return;}
                if(args.Length<2)throw new ArgumentException("Нужна цель: SteamID64 либо точное имя.");
                ulong target=MreModerationPlugin.ResolveTarget(args[1]);
                if(action=="profile") {var p=plugin.GetProfile(actor,target);Say(actor,p.Name+" ["+p.SteamId+"] / "+p.Rank+" / trust "+p.Trustscore+" / часов "+(p.PlaySeconds/3600.0).ToString("0.0")+" / предупреждений "+p.Warnings+" / благодарностей "+p.Commendations);Say(actor,"Первый вход "+Moscow(p.FirstSeenUtc)+"; последний "+Moscow(p.LastSeenUtc));Say(actor,"Имена: "+String.Join("; ",p.Names.Take(10).Select(n=>n.Name).ToArray()));return;}
                if(action=="history") {var page=plugin.SearchHistory(actor,target,args.Length>2?args[2]:null,null,0,0,15);Say(actor,"Записей: "+page.Total);foreach(var row in page.Items)Say(actor,row.Record.Id+" | "+row.Record.Kind+" / "+row.Record.Status+" | "+Moscow(row.Record.AtUtc)+" | admin "+row.Record.ActorId+" | "+row.Record.Reason);return;}
                if(action=="logs") {var page=plugin.SearchMessages(actor,target,args.Length>2?args[2]:null,null,null,0,15);Say(actor,"Строк журнала: "+page.Total);foreach(var row in page.Items)Say(actor,Moscow(row.Message.AtUtc)+" "+row.Message.Kind+"/"+row.Message.Channel+" | "+row.Message.Text);return;}
                if(action=="revoke") {if(args.Length<4)throw new ArgumentException("/staff revoke <SteamID64> <id записи> <причина>");Say(actor,plugin.RevokeRecord(actor,target,args[2],String.Join(" ",args.Skip(3).ToArray())));return;}
                if(action=="unban"||action=="unmute") {if(args.Length<3)throw new ArgumentException("/staff "+action+" <SteamID64> <причина>");Say(actor,plugin.RevokePunishment(actor,target,action=="unban"?"ban":"mute",String.Join(" ",args.Skip(2).ToArray())));return;}
                int duration=0,reasonStart=2;if(action=="ban"||action=="mute"){if(args.Length<4)throw new ArgumentException("/staff "+action+" <SteamID64> <30m|2h|7d|perm> <причина>");duration=StaffPolicy.DurationSeconds(args[2]);reasonStart=3;}
                if(args.Length<=reasonStart)throw new ArgumentException("Нужна причина действия.");
                Say(actor,plugin.ApplyModeration(actor,target,action,String.Join(" ",args.Skip(reasonStart).ToArray()),Guid.NewGuid().ToString("N"),duration));
            }catch(Exception error){Say(actor,"Действие не выполнено: "+error.Message);Logger.LogException(error,"[MRE] Ошибка/отказ /staff.");}
        }
        private static string Moscow(DateTime utc) {return utc.ToUniversalTime().AddHours(3).ToString("dd.MM.yyyy HH:mm")+" МСК";}
        private static void Say(UnturnedPlayer actor,string text) {MreChat.Say(actor,"MRE: "+MreModerationPlugin.Plain(text));}
    }
    public sealed class MyTrustscoreCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller {get{return AllowedCaller.Player;}}
        public string Name {get{return "mytrustscore";}}
        public string Help {get{return "Показать свой trustscore MRE";}}
        public string Syntax {get{return "";}}
        public List<string> Aliases {get{return new List<string>{"trustscore"};}}
        public List<string> Permissions {get{return new List<string>{"mre.trustscore"};}}
        public void Execute(IRocketPlayer caller,string[] args) {var actor=caller as UnturnedPlayer;if(actor==null||MreModerationPlugin.Instance==null)return;try{var p=MreModerationPlugin.Instance.GetMyTrustscore(actor);MreChat.Say(actor,"MRE: trustscore "+p.Trustscore+"/100; "+p.Rank+". Игровое время, благодарности и история предупреждений влияют на оценку.");}catch(Exception error){MreChat.Say(actor,"MRE: "+error.Message);}}
    }
}
