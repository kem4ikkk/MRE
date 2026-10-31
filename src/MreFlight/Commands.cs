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
            if(args.Length>1||(args.Length==1&&args[0]!="on"&&args[0]!="off")){UnturnedChat.Say(actor,"Использование: /afly [on|off]");return;}
            try{UnturnedChat.Say(actor,"MRE: "+plugin.SetFlight(actor,args.Length==0?!plugin.IsFlying(actor):args[0]=="on"));}
            catch(Exception error){Logger.LogException(error,"[MRE] Ошибка команды полета.");UnturnedChat.Say(actor,"MRE: полет не изменен; проверьте журнал.");}
        }
    }
    public abstract class SpeedCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller {get{return AllowedCaller.Player;}}
        public abstract string Name {get;}
        protected abstract bool Vertical {get;}
        public string Help {get{return "Скорость физического полета";}}
        public string Syntax {get{return "<число>";}}
        public List<string> Aliases {get{return new List<string>();}}
        public List<string> Permissions {get{return new List<string>{"mre.admin.fly"};}}
        public void Execute(IRocketPlayer caller,string[] args)
        {
            var actor=caller as UnturnedPlayer;var plugin=MreFlightPlugin.Instance;if(actor==null||plugin==null)return;
            float value;
            if(args.Length!=1||!Single.TryParse(args[0],NumberStyles.Float,CultureInfo.InvariantCulture,out value)){UnturnedChat.Say(actor,"MRE: укажите число с точкой.");return;}
            try{float[] current=plugin.GetSpeed(actor);UnturnedChat.Say(actor,"MRE: "+plugin.SetSpeed(actor,Vertical?current[0]:value,Vertical?value:current[1]));}
            catch(ArgumentOutOfRangeException error){UnturnedChat.Say(actor,error.Message);}
        }
    }
    public sealed class AFlySpeedCommand : SpeedCommand {public override string Name {get{return "aflyspeed";}}protected override bool Vertical {get{return false;}}}
    public sealed class AFlyVerticalSpeedCommand : SpeedCommand {public override string Name {get{return "aflyvspeed";}}protected override bool Vertical {get{return true;}}}
}
