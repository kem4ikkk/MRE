using System;
using System.Collections.Generic;
using System.Linq;

namespace MreModeration
{
    public sealed class NameRecord
    {
        public string Name;
        public DateTime AtUtc;
        public NameRecord Copy() {return new NameRecord {Name=Name,AtUtc=AtUtc};}
    }
    public sealed class ModerationRecord
    {
        public string Id,Kind,Reason,Status;
        public ulong ActorId;
        public DateTime AtUtc;
        public DateTime? ExpiresUtc;
        public string RevokedBy,RevokeReason;
        public ModerationRecord Copy() {return (ModerationRecord)MemberwiseClone();}
    }
    public sealed class MessageRecord
    {
        public string Kind,Text,Channel;
        public DateTime AtUtc;
        public MessageRecord Copy() {return (MessageRecord)MemberwiseClone();}
    }
    public sealed class PlayerProfile
    {
        public ulong SteamId;
        public DateTime FirstSeenUtc,LastSeenUtc;
        public long PlaySeconds;
        public string ManagedTrustGroup;
        public List<NameRecord> Names=new List<NameRecord>();
        public List<ModerationRecord> Records=new List<ModerationRecord>();
        public List<MessageRecord> Messages=new List<MessageRecord>();
        public PlayerProfile Copy() {return new PlayerProfile {SteamId=SteamId,FirstSeenUtc=FirstSeenUtc,LastSeenUtc=LastSeenUtc,PlaySeconds=PlaySeconds,ManagedTrustGroup=ManagedTrustGroup,Names=Names.Select(n=>n.Copy()).ToList(),Records=Records.Select(r=>r.Copy()).ToList(),Messages=Messages.Select(m=>m.Copy()).ToList()};}
    }
    public sealed class TrustPolicy
    {
        public int BaseScore=50,PointsPerHour=2,MaximumPlaytimePoints=25,CommendPoints=5,WarnPenalty=10,KickPenalty=15;
        public int Calculate(PlayerProfile profile)
        {
            long play=Math.Min(MaximumPlaytimePoints,(profile.PlaySeconds/3600)*(long)PointsPerHour);
            long result=BaseScore+play;
            foreach(var record in profile.Records.Where(r=>String.IsNullOrEmpty(r.RevokedBy))) {
                if(record.Kind=="commend")result+=CommendPoints;
                else if(record.Kind=="warn")result-=WarnPenalty;
                else if(record.Kind=="kick"&&record.Status=="applied")result-=KickPenalty;
            }
            return (int)Math.Max(0,Math.Min(100,result));
        }
        public void Validate() {if(BaseScore<0||BaseScore>100||PointsPerHour<0||PointsPerHour>100||MaximumPlaytimePoints<0||MaximumPlaytimePoints>100||CommendPoints<0||CommendPoints>100||WarnPenalty<0||WarnPenalty>100||KickPenalty<0||KickPenalty>100)throw new ArgumentException("Некорректные коэффициенты trustscore.");}
    }
    public sealed class ProfileSummary
    {
        public ulong SteamId {get;internal set;}
        public string Name {get;internal set;}
        public DateTime FirstSeenUtc {get;internal set;}
        public DateTime LastSeenUtc {get;internal set;}
        public long PlaySeconds {get;internal set;}
        public int Trustscore {get;internal set;}
        public string Rank {get;internal set;}
        public NameRecord[] Names {get;internal set;}
        public int Warnings {get;internal set;}
        public int Commendations {get;internal set;}
    }
    public static class StaffPolicy
    {
        public static readonly string[] Actions={"list","profile","history","note","warn","commend","kick","logs","revoke","ban","mute","unban","unmute"};
        public static bool Can(string action,Func<string,bool> permission) {return Array.IndexOf(Actions,action)>=0&&permission!=null&&permission("mre.staff")&&permission("mre.staff."+action);}
        public static bool SafeTrustGroup(string name) {return !String.IsNullOrEmpty(name)&&System.Text.RegularExpressions.Regex.IsMatch(name,"^mre_trust_[a-z0-9_]{1,32}$");}
        public static string Clean(string text,int limit)
        {
            if(text==null)return "";string clean=new string(text.Where(c=>!Char.IsControl(c)).ToArray()).Trim();return clean.Length>limit?clean.Substring(0,limit):clean;
        }
        public static string SafeCommand(string command)
        {
            string clean=Clean(command,512).TrimStart('/','@');int space=clean.IndexOf(' ');string name=(space<0?clean:clean.Substring(0,space)).ToLowerInvariant();
            // Команды могут содержать пароли, токены, ключи сторонних плагинов.
            string[] allow={"home","homes","tpa","vote","cv","kit","kits","admin","mreadmin","staff","mytrustscore","inspectinventory","previewdeath","restoredeath","compass"};
            return Array.IndexOf(allow,name)>=0?clean:name+" [аргументы скрыты]";
        }
        public static int DurationSeconds(string text)
        {
            if(String.Equals(text,"perm",StringComparison.OrdinalIgnoreCase))return 0;
            if(String.IsNullOrEmpty(text))throw new ArgumentException("Укажите длительность: 30m, 2h, 7d или perm.");
            string value=text.Trim().ToLowerInvariant();int multiplier=1;
            if(Char.IsLetter(value[value.Length-1])) {char suffix=value[value.Length-1];multiplier=suffix=='s'?1:suffix=='m'?60:suffix=='h'?3600:suffix=='d'?86400:0;value=value.Substring(0,value.Length-1);}
            long count,result;if(multiplier==0||!Int64.TryParse(value,out count)||count<=0||count>31536000||(result=count*multiplier)>31536000)throw new ArgumentException("Длительность: от1с до365д либо perm.");return (int)result;
        }
    }
    public sealed class ResultPage<T> {public T[] Items {get;internal set;}public int Total {get;internal set;}public int Skip {get;internal set;}public int Take {get;internal set;}}
}
