using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rocket.API;
using Rocket.Core;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger=Rocket.Core.Logging.Logger;

namespace MreModeration
{
    public sealed class PlayerMessageView {public ulong PlayerId {get;internal set;}public string Name {get;internal set;}public MessageRecord Message {get;internal set;}}
    public sealed class ModerationEventView {public ulong PlayerId {get;internal set;}public string Name {get;internal set;}public ModerationRecord Record {get;internal set;}}
    public sealed class MreModerationPlugin : RocketPlugin<MreModerationConfiguration>
    {
        private sealed class Session {public DateTime CheckpointUtc;public string Name;}
        public static MreModerationPlugin Instance {get;private set;}
        public event Action<ulong> ProfileChanged;
        public event Action<ModerationEventView> StaffActionRecorded;
        private readonly Dictionary<ulong,Session> sessions=new Dictionary<ulong,Session>();
        private readonly Dictionary<ulong,DateTime> nextQuery=new Dictionary<ulong,DateTime>();
        private ProfileLedger ledger;
        private WebhookOutbox outbox;
        private Task webhookTask;
        private int gameThread;
        private DateTime nextTickUtc,nextExpiryUtc;
        protected override void Load()
        {
            Configuration.Instance.Validate();gameThread=Thread.CurrentThread.ManagedThreadId;
            ledger=new ProfileLedger(System.IO.Path.Combine(Directory,"profiles"));
            try{outbox=new WebhookOutbox(System.IO.Path.Combine(Directory,"webhook-outbox.json"));}catch(Exception error){outbox=null;Logger.LogException(error,"[MRE] Webhook отключен до сверки outbox; модерация продолжает работать.");}
            Provider.onServerDisconnected+=Disconnected;
            Provider.onCheckBanStatusWithHWID+=CheckBan;
            ChatManager.onChatted+=Chatted;
            ChatManager.onCheckPermissions+=CommandAttempt;
            R.Commands.OnExecuteCommand+=CommandDispatch;
            nextTickUtc=DateTime.MinValue;nextExpiryUtc=DateTime.MinValue;Instance=this;
            Logger.Log("[MRE] MreModeration: профили, /staff, trustscore, bans/mutes, история чата и команд. Webhook enabled="+Configuration.Instance.WebhookEnabled);
        }
        protected override void Unload()
        {
            Provider.onServerDisconnected-=Disconnected;Provider.onCheckBanStatusWithHWID-=CheckBan;ChatManager.onChatted-=Chatted;ChatManager.onCheckPermissions-=CommandAttempt;R.Commands.OnExecuteCommand-=CommandDispatch;
            foreach(ulong id in sessions.Keys.ToArray())Checkpoint(id,DateTime.UtcNow);sessions.Clear();Instance=null;
        }
        private void Update()
        {
            DateTime now=DateTime.UtcNow;if(now<nextTickUtc||!Provider.isServer||!Level.isLoaded)return;nextTickUtc=now.AddSeconds(1);
            foreach(SteamPlayer client in Provider.clients.ToArray()) {
                if(client==null||client.player==null)continue;ulong id=client.playerID.steamID.m_SteamID;Session session;
                if(!sessions.TryGetValue(id,out session)) {
                    try{ledger.Visit(id,client.playerID.characterName,now);sessions[id]=new Session {CheckpointUtc=now,Name=client.playerID.characterName};ApplyTrustGroup(UnturnedPlayer.FromPlayer(client.player));Changed(id);}catch(Exception error){Logger.LogException(error,"[MRE] Не удалось открыть профиль игрока.");}
                }else if((now-session.CheckpointUtc).TotalSeconds>=60) {Checkpoint(id,now);ApplyTrustGroup(UnturnedPlayer.FromPlayer(client.player));}
            }
            if(now>=nextExpiryUtc) {
                nextExpiryUtc=now.AddSeconds(30);
                foreach(ulong id in ledger.KnownPlayers(10000))try{foreach(var entry in ledger.Expire(id,now)){Publish(id,entry);Changed(id);}}catch(Exception error){Logger.LogException(error,"[MRE] Не удалось проверить срок наказания.");}
            }
            if(Configuration.Instance.WebhookEnabled&&outbox!=null&&outbox.Count>0&&(webhookTask==null||webhookTask.IsCompleted)) {
                if(webhookTask!=null&&webhookTask.IsFaulted)Logger.LogWarning("[MRE] Доставка webhook завершилась с ошибкой; очередь сохранена.");
                string endpoint=Configuration.Instance.WebhookUrl;webhookTask=Task.Run(()=>outbox.Pump(DateTime.UtcNow,content=>WebhookOutbox.SendDiscord(endpoint,content)));
            }
        }
        private void Checkpoint(ulong id,DateTime now)
        {
            Session session;if(!sessions.TryGetValue(id,out session))return;
            long seconds=(long)Math.Floor((now-session.CheckpointUtc).TotalSeconds);if(seconds<=0)return;
            try{ledger.AddPlaytime(id,Math.Min(86400,seconds),now);session.CheckpointUtc=session.CheckpointUtc.AddSeconds(seconds);SteamPlayer target=Provider.clients.FirstOrDefault(p=>p!=null&&p.playerID.steamID.m_SteamID==id);if(target!=null&&session.Name!=target.playerID.characterName){ledger.Visit(id,target.playerID.characterName,now);session.Name=target.playerID.characterName;}Changed(id);}catch(Exception error){Logger.LogException(error,"[MRE] Не удалось сохранить время игры.");}
        }
        private void Disconnected(CSteamID id) {Checkpoint(id.m_SteamID,DateTime.UtcNow);sessions.Remove(id.m_SteamID);nextQuery.Remove(id.m_SteamID);}
        private void RequireThread() {if(Thread.CurrentThread.ManagedThreadId!=gameThread)throw new InvalidOperationException("MRE API вызывается на главном игровом потоке.");}
        private static bool Online(UnturnedPlayer actor) {return actor!=null&&actor.Player!=null&&Provider.clients.Any(c=>c!=null&&c.player==actor.Player&&c.playerID.steamID==actor.CSteamID);}
        private void Authorize(UnturnedPlayer actor,string action)
        {
            RequireThread();if(!Online(actor)||!StaffPolicy.Can(action,p=>actor.IsAdmin||actor.HasPermission(p)))throw new UnauthorizedAccessException("Нет прав на действие /staff.");
        }
        private void LimitQuery(UnturnedPlayer actor) {DateTime next,now=DateTime.UtcNow;if(nextQuery.TryGetValue(actor.CSteamID.m_SteamID,out next)&&now<next)throw new InvalidOperationException("Подожди перед следующим поиском.");nextQuery[actor.CSteamID.m_SteamID]=now.AddMilliseconds(250);}
        private static bool Contains(string haystack,string needle) {return String.IsNullOrEmpty(needle)||(!String.IsNullOrEmpty(haystack)&&haystack.IndexOf(needle,StringComparison.OrdinalIgnoreCase)>=0);}
        private ProfileSummary Summary(PlayerProfile profile)
        {
            int score=Configuration.Instance.Trust.Calculate(profile);return new ProfileSummary {SteamId=profile.SteamId,Name=profile.Names.Count>0?profile.Names[0].Name:profile.SteamId.ToString(),FirstSeenUtc=profile.FirstSeenUtc,LastSeenUtc=profile.LastSeenUtc,PlaySeconds=profile.PlaySeconds,Trustscore=score,Rank=score>=75?"Доверенный":score>=40?"Обычный":"Низкое доверие",Names=profile.Names.Select(n=>n.Copy()).ToArray(),Warnings=profile.Records.Count(r=>r.Kind=="warn"&&String.IsNullOrEmpty(r.RevokedBy)),Commendations=profile.Records.Count(r=>r.Kind=="commend"&&String.IsNullOrEmpty(r.RevokedBy))};
        }
        public string[] GetStaffActions(UnturnedPlayer actor) {RequireThread();if(!Online(actor)||!(actor.IsAdmin||actor.HasPermission("mre.staff")))throw new UnauthorizedAccessException("Нет доступа к /staff.");return StaffPolicy.Actions.Where(action=>StaffPolicy.Can(action,p=>actor.IsAdmin||actor.HasPermission(p))).ToArray();}
        public ProfileSummary GetProfile(UnturnedPlayer actor,ulong target) {Authorize(actor,"profile");PlayerProfile profile=ledger.Get(target);if(profile==null)throw new InvalidOperationException("Профиль не найден.");return Summary(profile);}
        public ProfileSummary GetMyTrustscore(UnturnedPlayer actor)
        {
            RequireThread();if(!Online(actor)||!(actor.IsAdmin||actor.HasPermission("mre.trustscore")))throw new UnauthorizedAccessException("Нет права просмотра trustscore.");PlayerProfile profile=ledger.Get(actor.CSteamID.m_SteamID);if(profile==null)throw new InvalidOperationException("Профиль еще загружается.");return Summary(profile);
        }
        public ResultPage<ProfileSummary> SearchProfiles(UnturnedPlayer actor,string query,bool? online,int skip=0,int take=25,string sort="last_seen",bool descending=true)
        {
            Authorize(actor,"list");LimitQuery(actor);if(skip<0||take<1||take>50)throw new ArgumentOutOfRangeException("page");if(!new[]{"last_seen","first_seen","name","trust","playtime"}.Contains(sort))throw new ArgumentException("Неизвестная сортировка.");
            var onlineIds=new HashSet<ulong>(Provider.clients.Where(p=>p!=null).Select(p=>p.playerID.steamID.m_SteamID));
            var list=new List<ProfileSummary>();foreach(ulong id in ledger.KnownPlayers(10000)){if(online.HasValue&&online.Value!=onlineIds.Contains(id))continue;var profile=ledger.Get(id);if(profile!=null&&(Contains(id.ToString(),query)||profile.Names.Any(n=>Contains(n.Name,query))))list.Add(Summary(profile));}
            Func<ProfileSummary,IComparable> key=p=>sort=="name"?(IComparable)p.Name:sort=="trust"?(IComparable)p.Trustscore:sort=="playtime"?(IComparable)p.PlaySeconds:sort=="first_seen"?(IComparable)p.FirstSeenUtc:p.LastSeenUtc;
            IEnumerable<ProfileSummary> sorted=descending?list.OrderByDescending(key):list.OrderBy(key);return new ResultPage<ProfileSummary>{Items=sorted.Skip(skip).Take(take).ToArray(),Total=list.Count,Skip=skip,Take=take};
        }
        public ResultPage<ModerationEventView> SearchHistory(UnturnedPlayer actor,ulong target,string kind,string reasonQuery,ulong moderatorId,int skip=0,int take=25,bool newestFirst=true)
        {
            Authorize(actor,"history");LimitQuery(actor);if(skip<0||take<1||take>50)throw new ArgumentOutOfRangeException("page");var rows=new List<ModerationEventView>();
            foreach(ulong id in target==0?ledger.KnownPlayers(10000):new[]{target}){var profile=ledger.Get(id);if(profile==null)continue;foreach(var record in profile.Records.Where(r=>(String.IsNullOrEmpty(kind)||r.Kind==kind)&&Contains(r.Reason,reasonQuery)&&(moderatorId==0||r.ActorId==moderatorId)))rows.Add(new ModerationEventView {PlayerId=id,Name=profile.Names.Count>0?profile.Names[0].Name:id.ToString(),Record=record.Copy()});}
            IEnumerable<ModerationEventView> sorted=newestFirst?rows.OrderByDescending(r=>r.Record.AtUtc):rows.OrderBy(r=>r.Record.AtUtc);return new ResultPage<ModerationEventView>{Items=sorted.Skip(skip).Take(take).ToArray(),Total=rows.Count,Skip=skip,Take=take};
        }
        public ResultPage<PlayerMessageView> SearchMessages(UnturnedPlayer actor,ulong target,string kind,string textQuery,string nameQuery,int skip=0,int take=25,bool newestFirst=true)
        {
            Authorize(actor,"logs");LimitQuery(actor);if(skip<0||take<1||take>50||(!String.IsNullOrEmpty(kind)&&kind!="chat"&&kind!="command"))throw new ArgumentException("Некорректный фильтр журнала.");var rows=new List<PlayerMessageView>();
            foreach(ulong id in target==0?ledger.KnownPlayers(10000):new[]{target}){var profile=ledger.Get(id);if(profile==null||!profile.Names.Any(n=>Contains(n.Name,nameQuery)))continue;foreach(var m in profile.Messages.Where(m=>(String.IsNullOrEmpty(kind)||m.Kind==kind)&&Contains(m.Text,textQuery)))rows.Add(new PlayerMessageView {PlayerId=id,Name=profile.Names[0].Name,Message=m.Copy()});}
            IEnumerable<PlayerMessageView> sorted=newestFirst?rows.OrderByDescending(r=>r.Message.AtUtc):rows.OrderBy(r=>r.Message.AtUtc);return new ResultPage<PlayerMessageView>{Items=sorted.Skip(skip).Take(take).ToArray(),Total=rows.Count,Skip=skip,Take=take};
        }
        public string ApplyModeration(UnturnedPlayer actor,ulong target,string kind,string reason,string operationId,int durationSeconds=0)
        {
            Authorize(actor,kind);if(!new[]{"note","warn","commend","kick","ban","mute"}.Contains(kind))throw new ArgumentException("Недопустимое наказание.");
            if((kind=="ban"||kind=="kick"||kind=="mute")&&target==actor.CSteamID.m_SteamID)throw new InvalidOperationException("Нельзя применить это действие к себе.");
            SteamPlayer online=Provider.clients.FirstOrDefault(p=>p!=null&&p.playerID.steamID.m_SteamID==target);
            if(kind=="kick"&&online==null)throw new InvalidOperationException("Игрок уже отключен.");
            if(kind=="ban") {SteamBlacklistID external;if(SteamBlacklist.checkBanned(new CSteamID(target),out external))throw new InvalidOperationException("У игрока уже есть native-бан; существующая блокировка не заменяется.");}
            if((kind=="ban"||kind=="mute")&&ledger.ActivePunishment(target,kind,DateTime.UtcNow)!=null)throw new InvalidOperationException("У игрока уже действует такое наказание; сначала отмените его.");
            if(!ledger.Exists(target))ledger.Visit(target,online!=null?online.playerID.characterName:target.ToString(),DateTime.UtcNow);
            if(!ledger.Record(target,actor.CSteamID.m_SteamID,kind,reason,operationId,DateTime.UtcNow,durationSeconds))return "Действие уже записано; повтор не выполнен.";
            if(kind=="kick") {
                try{Provider.kick(new CSteamID(target),"MRE: "+StaffPolicy.Clean(reason,300));ledger.SetKickResult(target,operationId,true);}catch{ledger.SetKickResult(target,operationId,false);throw;}
            }else if(kind=="ban") {
                // Сначала durable pending; при неясном результате повтор автоматически не выполняется.
                string nativeReason="[MRE:"+operationId+"] "+StaffPolicy.Clean(reason,300);
                SteamBlacklist.ban(new CSteamID(target),actor.CSteamID,nativeReason,durationSeconds==0?SteamBlacklist.PERMANENT:(uint)durationSeconds);SteamBlacklist.save();ledger.SetBanApplied(target,operationId);
            }else if((kind=="warn"||kind=="mute")&&online!=null)UnturnedChat.Say(UnturnedPlayer.FromPlayer(online.player),"MRE: "+(kind=="warn"?"Предупреждение: ":"Чат ограничен: ")+Plain(reason));
            var profile=ledger.Get(target);var entry=profile.Records.First(r=>r.Id==operationId);Publish(target,entry);Changed(target);if(online!=null)ApplyTrustGroup(UnturnedPlayer.FromPlayer(online.player));
            return "Записано: "+kind+"; id "+operationId+".";
        }
        public string RevokeRecord(UnturnedPlayer actor,ulong target,string recordId,string reason)
        {
            Authorize(actor,"revoke");var profile=ledger.Get(target);var record=profile==null?null:profile.Records.FirstOrDefault(r=>r.Id==recordId);if(record==null)throw new InvalidOperationException("Запись не найдена.");
            if(record.Kind=="ban"||record.Kind=="mute")Authorize(actor,record.Kind=="ban"?"unban":"unmute");
            if(String.IsNullOrWhiteSpace(reason))throw new ArgumentException("Укажите причину отмены.");
            if(record.Kind=="ban")RemoveOwnedNativeBan(target,new[]{record.Id});
            ledger.Revoke(target,recordId,actor.CSteamID.m_SteamID,reason);var changed=ledger.Get(target).Records.First(r=>r.Id==recordId);Publish(target,changed);Changed(target);return "Запись отменена; история сохранена.";
        }
        public string RevokePunishment(UnturnedPlayer actor,ulong target,string kind,string reason)
        {
            Authorize(actor,kind=="ban"?"unban":"unmute");if(kind!="ban"&&kind!="mute")throw new ArgumentException("Недопустимый вид наказания.");
            if(String.IsNullOrWhiteSpace(reason))throw new ArgumentException("Нужна причина отмены.");var profile=ledger.Get(target);if(profile==null)throw new InvalidOperationException("Профиль не найден.");
            var records=profile.Records.Where(r=>r.Kind==kind&&String.IsNullOrEmpty(r.RevokedBy)&&(r.Status=="active"||r.Status=="pending")).ToArray();if(records.Length==0)return "Активных записей этого модуля нет; внешние наказания не изменены.";
            if(kind=="ban")RemoveOwnedNativeBan(target,records.Select(r=>r.Id));foreach(var record in records){ledger.Revoke(target,record.Id,actor.CSteamID.m_SteamID,reason);Publish(target,ledger.Get(target).Records.First(r=>r.Id==record.Id));}Changed(target);return "Наказание MRE отменено. Чужие native-баны не изменяются.";
        }
        private static void RemoveOwnedNativeBan(ulong target,IEnumerable<string> ids)
        {
            SteamBlacklistID native;if(SteamBlacklist.checkBanned(new CSteamID(target),out native)&&ids.Any(id=>native.reason!=null&&native.reason.StartsWith("[MRE:"+id+"]",StringComparison.Ordinal))){SteamBlacklist.unban(new CSteamID(target));SteamBlacklist.save();}
        }
        private void CheckBan(SteamPlayerID player,uint remoteIp,ref bool banned,ref string reason,ref uint remaining)
        {
            try{var record=ledger.ActivePunishment(player.steamID.m_SteamID,"ban",DateTime.UtcNow);if(record!=null){banned=true;reason="MRE: "+record.Reason;remaining=record.ExpiresUtc.HasValue?(uint)Math.Max(1,Math.Ceiling((record.ExpiresUtc.Value-DateTime.UtcNow).TotalSeconds)):SteamBlacklist.PERMANENT;}}
            catch(Exception error){banned=true;reason="Ошибка учета ограничений MRE; обратитесь к владельцу.";remaining=60;Logger.LogException(error,"[MRE] Ошибка проверки сохраненного бана; вход закрыт до сверки.");}
        }
        private void Chatted(SteamPlayer player,EChatMode mode,ref Color color,ref bool rich,string text,ref bool visible)
        {
            if(player==null)return;ulong id=player.playerID.steamID.m_SteamID;
            try{if(!String.IsNullOrEmpty(text)&&text[0]!='/'&&text[0]!='@'&&ledger.ActivePunishment(id,"mute",DateTime.UtcNow)!=null)visible=false;
                if(Configuration.Instance.LogChat&&text!=null&&!text.StartsWith("/")&&!text.StartsWith("@"))ledger.Log(id,"chat",mode+(visible?"":"/blocked"),text,DateTime.UtcNow);
            }catch(Exception error){visible=false;Logger.LogException(error,"[MRE] Ошибка проверки mute/учета чата.");}
        }
        private void CommandAttempt(SteamPlayer player,string command,ref bool execute,ref bool list)
        {
            if(player==null||!Configuration.Instance.LogCommands)return;try{ledger.Log(player.playerID.steamID.m_SteamID,"command","attempt",command,DateTime.UtcNow);}catch(Exception error){Logger.LogException(error,"[MRE] Не удалось записать попытку команды.");}
        }
        private void CommandDispatch(IRocketPlayer actor,IRocketCommand command,ref bool cancel)
        {
            var player=actor as UnturnedPlayer;if(player==null||command==null||!Configuration.Instance.LogCommands)return;try{ledger.Log(player.CSteamID.m_SteamID,"command",cancel?"cancelled":"dispatch",command.Name,DateTime.UtcNow);}catch(Exception error){Logger.LogException(error,"[MRE] Не удалось записать dispatch команды.");}
        }
        private void ApplyTrustGroup(UnturnedPlayer player)
        {
            if(!Configuration.Instance.TrustGroupsEnabled||!Online(player))return;
            try {
                var profile=ledger.Get(player.CSteamID.m_SteamID);int score=Configuration.Instance.Trust.Calculate(profile);
                var rule=Configuration.Instance.TrustGroups.OrderByDescending(r=>r.MinimumScore).FirstOrDefault(r=>score>=r.MinimumScore);
                string desired=rule==null?null:rule.GroupId;
                var managed=Configuration.Instance.TrustGroups.Select(r=>r.GroupId).Concat(new[]{profile.ManagedTrustGroup}).Where(s=>!String.IsNullOrEmpty(s)).Distinct().ToArray();
                foreach(string id in managed){var group=R.Permissions.GetGroup(id);if(!StaffPolicy.SafeTrustGroup(id)||group==null||group.Permissions.Count!=0||(!String.IsNullOrEmpty(group.ParentGroup)&&group.ParentGroup!="default"))throw new InvalidOperationException("Trust-группа должна быть пустой, без административных/донатных прав и с parent default либо без parent.");}
                var current=R.Permissions.GetGroups(player,false).Select(g=>g.Id).ToArray();
                if(desired!=null&&!current.Contains(desired))R.Permissions.AddPlayerToGroup(desired,player);
                foreach(string old in managed.Where(s=>s!=desired&&current.Contains(s)))R.Permissions.RemovePlayerFromGroup(old,player);
                if(profile.ManagedTrustGroup!=desired)ledger.SetManagedGroup(profile.SteamId,desired);
            }catch(Exception error){Logger.LogException(error,"[MRE] Автогруппа trustscore не изменена: проверьте пустые безопасные группы.");}
        }
        private void Changed(ulong target) {var listeners=ProfileChanged;if(listeners!=null)foreach(Action<ulong> callback in listeners.GetInvocationList())try{callback(target);}catch(Exception error){Logger.LogException(error,"[MRE] Ошибка подписчика ProfileChanged.");}}
        private void Publish(ulong target,ModerationRecord record)
        {
            var profile=ledger.Get(target);var view=new ModerationEventView {PlayerId=target,Name=profile.Names.Count>0?profile.Names[0].Name:target.ToString(),Record=record.Copy()};
            Logger.Log("[MRE STAFF] target="+target+" actor="+record.ActorId+" kind="+record.Kind+" status="+record.Status+" id="+record.Id);
            if(Configuration.Instance.WebhookEnabled&&outbox!=null)try{outbox.Enqueue(record.Id+":"+record.Status+":"+(record.RevokedBy??""),"MRE | "+record.Kind+" / "+record.Status+" | target="+target+" | actor="+record.ActorId+" | id="+record.Id+" | "+record.Reason,DateTime.UtcNow);}catch(Exception error){Logger.LogException(error,"[MRE] Событие сохранено в профиле, но не добавлено в webhook.");}
            var listeners=StaffActionRecorded;if(listeners!=null)foreach(Action<ModerationEventView> callback in listeners.GetInvocationList())try{callback(view);}catch(Exception error){Logger.LogException(error,"[MRE] Ошибка подписчика StaffActionRecorded.");}
        }
        public static ulong ResolveTarget(string query)
        {
            ulong id;if(UInt64.TryParse(query,out id)&&id!=0)return id;var matches=Provider.clients.Where(p=>p!=null&&(p.playerID.characterName.Equals(query,StringComparison.OrdinalIgnoreCase)||p.playerID.playerName.Equals(query,StringComparison.OrdinalIgnoreCase))).ToArray();if(matches.Length!=1)throw new ArgumentException("Нужен SteamID64 или уникальное полное имя.");return matches[0].playerID.steamID.m_SteamID;
        }
        public static string Plain(string text) {return StaffPolicy.Clean(text,512).Replace('<','[').Replace('>',']');}
    }
}
