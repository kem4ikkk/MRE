using MreShared;
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using Logger=Rocket.Core.Logging.Logger;

namespace MreFlight
{
    public sealed class AFlyCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller {get{return AllowedCaller.Player;}}
        public string Name {get{return "afly";}}
        public string Help {get{return "Физический полет администратора";}}
        public string Syntax {get{return "[on|off]";}}
        public List<string> Aliases {get{return new List<string>{"fly"};}}
        public List<string> Permissions {get{return new List<string>();}}
        public void Execute(IRocketPlayer caller,string[] args)
        {
            var actor=caller as UnturnedPlayer;var plugin=MreFlightPlugin.Instance;
            if(actor==null||plugin==null)return;
            if(args.Length>1||(args.Length==1&&args[0]!="on"&&args[0]!="off")){MreChat.SayAdmin(actor,"Использование: /afly [on|off]");return;}
            try{MreChat.SayAdmin(actor,"MRE: "+plugin.SetFlight(actor,args.Length==0?!plugin.IsFlying(actor):args[0]=="on"));}
            catch(Exception error){Logger.LogException(error,"[MRE] Ошибка команды полета.");MreChat.SayAdmin(actor,"MRE: полет не изменен; проверьте журнал.");}
        }
    }
    public abstract class SpeedCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller {get{return AllowedCaller.Player;}}
        public abstract string Name {get;}
        protected abstract bool Vertical {get;}
        public string Help {get{return "Скорость физического полета";}}
        public string Syntax {get{return "<число>";}}
        public List<string> Aliases {get{return Vertical?new List<string>{"aflyvs"}:new List<string>{"aflys"};}}
        public List<string> Permissions {get{return new List<string>{"mre.admin.fly"};}}
        public void Execute(IRocketPlayer caller,string[] args)
        {
            var actor=caller as UnturnedPlayer;var plugin=MreFlightPlugin.Instance;if(actor==null||plugin==null)return;
            float value;
            if(args.Length!=1||!Single.TryParse(args[0],NumberStyles.Float,CultureInfo.InvariantCulture,out value)){MreChat.SayAdmin(actor,"MRE: укажите число с точкой.");return;}
            try{float[] current=plugin.GetSpeed(actor);MreChat.SayAdmin(actor,"MRE: "+plugin.SetSpeed(actor,Vertical?current[0]:value,Vertical?value:current[1]));}
            catch(ArgumentOutOfRangeException error){MreChat.SayAdmin(actor,error.Message);}
        }
    }
    public sealed class AFlySpeedCommand : SpeedCommand {public override string Name {get{return "aflyspeed";}}protected override bool Vertical {get{return false;}}}
    public sealed class AFlyVerticalSpeedCommand : SpeedCommand {public override string Name {get{return "aflyvspeed";}}protected override bool Vertical {get{return true;}}}
}
