using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MreModeration
{
    public sealed class ProfileLedger
    {
        private readonly string directory;
        public ProfileLedger(string directory) {this.directory=directory;Directory.CreateDirectory(directory);}
        private string FilePath(ulong id) {if(id==0)throw new ArgumentException("Нулевой SteamID.");return Path.Combine(directory,id.ToString(CultureInfo.InvariantCulture)+".json");}
        private static string Date(DateTime value) {if(value.Kind!=DateTimeKind.Utc)throw new ArgumentException("Требуется UTC.");return value.ToString("o",CultureInfo.InvariantCulture);}
        private static DateTime ReadDate(JToken value) {var result=DateTime.ParseExact((string)value,"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);if(result.Kind!=DateTimeKind.Utc)throw new InvalidDataException("Требуется дата UTC.");return result;}
        public bool Exists(ulong id) {return File.Exists(FilePath(id));}
        public PlayerProfile Get(ulong id)
        {
            string path=FilePath(id);if(!File.Exists(path))return null;JObject root;
            using(var reader=new JsonTextReader(new StringReader(File.ReadAllText(path)))){reader.DateParseHandling=DateParseHandling.None;root=JObject.Load(reader);}
            if((int)root["schema"]!=1||UInt64.Parse((string)root["steam_id"],CultureInfo.InvariantCulture)!=id)throw new InvalidDataException("Несовместимый профиль.");
            var profile=new PlayerProfile {SteamId=id,FirstSeenUtc=ReadDate(root["first_seen"]),LastSeenUtc=ReadDate(root["last_seen"]),PlaySeconds=(long)root["play_seconds"],ManagedTrustGroup=(string)root["trust_group"]};
            if(profile.PlaySeconds<0||profile.LastSeenUtc<profile.FirstSeenUtc||(!String.IsNullOrEmpty(profile.ManagedTrustGroup)&&!StaffPolicy.SafeTrustGroup(profile.ManagedTrustGroup)))throw new InvalidDataException("Поврежденный профиль.");
            foreach(JObject row in (JArray)root["names"])profile.Names.Add(new NameRecord {Name=(string)row["name"],AtUtc=ReadDate(row["at"])});
            var ids=new HashSet<string>();
            foreach(JObject row in (JArray)root["records"]) {
                var entry=new ModerationRecord {Id=(string)row["id"],Kind=(string)row["kind"],Reason=(string)row["reason"],Status=(string)row["status"],ActorId=UInt64.Parse((string)row["actor"],CultureInfo.InvariantCulture),AtUtc=ReadDate(row["at"]),RevokedBy=(string)row["revoked_by"],RevokeReason=(string)row["revoke_reason"],ExpiresUtc=row["expires_utc"]==null||row["expires_utc"].Type==JTokenType.Null?(DateTime?)null:ReadDate(row["expires_utc"])};
                Guid parsed;if(!Guid.TryParseExact(entry.Id,"N",out parsed)||!ids.Add(entry.Id)||!new[]{"note","warn","commend","kick","ban","mute"}.Contains(entry.Kind)||!new[]{"recorded","pending","applied","failed","active","expired","revoked"}.Contains(entry.Status)||String.IsNullOrEmpty(entry.Reason)||entry.Reason.Length>512||(entry.ExpiresUtc.HasValue&&entry.ExpiresUtc.Value<=entry.AtUtc))throw new InvalidDataException("Повреждена история модерации.");
                profile.Records.Add(entry);
            }
            foreach(JObject row in (JArray)root["messages"])profile.Messages.Add(new MessageRecord {Kind=(string)row["kind"],Text=(string)row["text"],Channel=(string)row["channel"],AtUtc=ReadDate(row["at"])});
            if(profile.Names.Count>50||profile.Records.Count>1000||profile.Messages.Count>200)throw new InvalidDataException("Превышены границы профиля.");return profile;
        }
        private void Save(PlayerProfile profile)
        {
            var names=new JArray();foreach(var n in profile.Names)names.Add(new JObject { {"name",n.Name},{"at",Date(n.AtUtc)} });
            var records=new JArray();foreach(var r in profile.Records)records.Add(new JObject { {"id",r.Id},{"kind",r.Kind},{"reason",r.Reason},{"status",r.Status},{"actor",r.ActorId.ToString(CultureInfo.InvariantCulture)},{"at",Date(r.AtUtc)},{"expires_utc",r.ExpiresUtc.HasValue?Date(r.ExpiresUtc.Value):null},{"revoked_by",r.RevokedBy},{"revoke_reason",r.RevokeReason} });
            var messages=new JArray();foreach(var m in profile.Messages)messages.Add(new JObject { {"kind",m.Kind},{"text",m.Text},{"channel",m.Channel},{"at",Date(m.AtUtc)} });
            var root=new JObject { {"schema",1},{"steam_id",profile.SteamId.ToString(CultureInfo.InvariantCulture)},{"first_seen",Date(profile.FirstSeenUtc)},{"last_seen",Date(profile.LastSeenUtc)},{"play_seconds",profile.PlaySeconds},{"trust_group",profile.ManagedTrustGroup},{"names",names},{"records",records},{"messages",messages} };
            string path=FilePath(profile.SteamId),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {byte[] bytes=new UTF8Encoding(false).GetBytes(root.ToString(Formatting.None));using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public PlayerProfile Visit(ulong id,string name,DateTime now)
        {
            Date(now);var profile=Get(id)??new PlayerProfile {SteamId=id,FirstSeenUtc=now,LastSeenUtc=now};
            if(now<profile.LastSeenUtc)now=profile.LastSeenUtc;profile.LastSeenUtc=now;name=StaffPolicy.Clean(name,128);
            if(name.Length>0&&(profile.Names.Count==0||profile.Names[0].Name!=name)){profile.Names.Insert(0,new NameRecord {Name=name,AtUtc=now});if(profile.Names.Count>50)profile.Names.RemoveAt(50);}
            Save(profile);return profile.Copy();
        }
        public void AddPlaytime(ulong id,long seconds,DateTime now)
        {
            Date(now);if(seconds<0||seconds>86400)throw new ArgumentOutOfRangeException("seconds");var profile=Get(id);if(profile==null)throw new InvalidOperationException("Профиль не найден.");
            profile.PlaySeconds=checked(profile.PlaySeconds+seconds);if(now>profile.LastSeenUtc)profile.LastSeenUtc=now;Save(profile);
        }
        public bool Record(ulong target,ulong actor,string kind,string reason,string operationId,DateTime now,int durationSeconds=0)
        {
            Date(now);Guid parsed;if(!Guid.TryParseExact(operationId,"N",out parsed))throw new ArgumentException("Некорректный id действия.");
            if(!new[]{"note","warn","commend","kick","ban","mute"}.Contains(kind)||actor==0||durationSeconds<0||durationSeconds>31536000)throw new ArgumentException("Недопустимое действие.");
            reason=StaffPolicy.Clean(reason,512);if(reason.Length==0)throw new ArgumentException("Нужна причина.");
            var profile=Get(target);if(profile==null)throw new InvalidOperationException("Профиль не найден.");
            var existing=profile.Records.FirstOrDefault(r=>r.Id==operationId);
            if(existing!=null){if(existing.ActorId!=actor||existing.Kind!=kind||existing.Reason!=reason||((kind=="ban"||kind=="mute")&&((durationSeconds==0&&existing.ExpiresUtc.HasValue)||(durationSeconds>0&&(!existing.ExpiresUtc.HasValue||(existing.ExpiresUtc.Value-existing.AtUtc).TotalSeconds!=durationSeconds)))))throw new InvalidOperationException("Id действия уже использован для других данных.");return false;}
            if(profile.Records.Count>=1000)throw new InvalidOperationException("История достигла 1000 действий; требуется архивирование владельцем.");
            profile.Records.Insert(0,new ModerationRecord {Id=operationId,Kind=kind,Reason=reason,Status=(kind=="kick"||kind=="ban")?"pending":kind=="mute"?"active":"recorded",ActorId=actor,AtUtc=now,ExpiresUtc=(kind=="ban"||kind=="mute")&&durationSeconds>0?(DateTime?)now.AddSeconds(durationSeconds):null});Save(profile);return true;
        }
        public void SetKickResult(ulong target,string id,bool applied)
        {
            var profile=Get(target);var entry=profile==null?null:profile.Records.FirstOrDefault(r=>r.Id==id);
            if(entry==null||entry.Kind!="kick"||entry.Status!="pending")throw new InvalidOperationException("Нет ожидающего kick.");entry.Status=applied?"applied":"failed";Save(profile);
        }
        public void SetBanApplied(ulong target,string id) {var profile=Get(target);var entry=profile==null?null:profile.Records.FirstOrDefault(r=>r.Id==id);if(entry==null||entry.Kind!="ban"||entry.Status!="pending")throw new InvalidOperationException("Нет ожидающего ban.");entry.Status="active";Save(profile);}
        public ModerationRecord ActivePunishment(ulong target,string kind,DateTime now) {var profile=Get(target);return profile==null?null:profile.Records.FirstOrDefault(r=>r.Kind==kind&&String.IsNullOrEmpty(r.RevokedBy)&&(r.Status=="active"||r.Status=="pending")&&(!r.ExpiresUtc.HasValue||r.ExpiresUtc.Value>now));}
        public ModerationRecord[] Expire(ulong target,DateTime now)
        {
            var profile=Get(target);if(profile==null)return new ModerationRecord[0];var expired=profile.Records.Where(r=>(r.Kind=="ban"||r.Kind=="mute")&&String.IsNullOrEmpty(r.RevokedBy)&&(r.Status=="active"||r.Status=="pending")&&r.ExpiresUtc.HasValue&&r.ExpiresUtc.Value<=now).ToArray();
            foreach(var row in expired)row.Status="expired";if(expired.Length>0)Save(profile);return expired.Select(e=>e.Copy()).ToArray();
        }
        public void Revoke(ulong target,string recordId,ulong actor,string reason)
        {
            reason=StaffPolicy.Clean(reason,512);if(actor==0||reason.Length==0)throw new ArgumentException("Укажите причину отмены.");
            var profile=Get(target);var entry=profile==null?null:profile.Records.FirstOrDefault(r=>r.Id==recordId);if(entry==null)throw new InvalidOperationException("Запись не найдена.");
            if(!String.IsNullOrEmpty(entry.RevokedBy))throw new InvalidOperationException("Запись уже отменена.");entry.RevokedBy=actor.ToString(CultureInfo.InvariantCulture);entry.RevokeReason=reason;if(entry.Kind=="ban"||entry.Kind=="mute")entry.Status="revoked";Save(profile);
        }
        public void Log(ulong target,string kind,string channel,string text,DateTime now)
        {
            Date(now);if(kind!="chat"&&kind!="command")throw new ArgumentException("Некорректный вид журнала.");var profile=Get(target);if(profile==null)return;
            profile.Messages.Insert(0,new MessageRecord {Kind=kind,Channel=StaffPolicy.Clean(channel,32),Text=kind=="command"?StaffPolicy.SafeCommand(text):StaffPolicy.Clean(text,512),AtUtc=now});if(profile.Messages.Count>200)profile.Messages.RemoveAt(200);Save(profile);
        }
        public void SetManagedGroup(ulong target,string group) {if(!String.IsNullOrEmpty(group)&&!StaffPolicy.SafeTrustGroup(group))throw new InvalidOperationException("Недопустимая trust-группа.");var profile=Get(target);if(profile==null)return;profile.ManagedTrustGroup=group;Save(profile);}
        public ulong[] KnownPlayers(int limit) {return Directory.EnumerateFiles(directory,"*.json").Select(Path.GetFileNameWithoutExtension).Where(s=>{ulong id;return UInt64.TryParse(s,out id);}).Take(Math.Min(10000,Math.Max(1,limit))).Select(UInt64.Parse).ToArray();}
    }
}
