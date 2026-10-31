using MreModeration;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
    private static void Fails(Action action,string name) {bool failed=false;try{action();}catch{failed=true;}Check(failed,name);}
    private static string Id() {return Guid.NewGuid().ToString("N");}
    private static void Main()
    {
        string root=Path.Combine(Directory.GetCurrentDirectory(),"runtime","builds","MreModeration","tests","data",Id());Directory.CreateDirectory(root);
        string profiles=Path.Combine(root,"profiles");var ledger=new ProfileLedger(profiles);DateTime now=new DateTime(2026,10,30,12,0,0,DateTimeKind.Utc);
        ledger.Visit(111,"Первое имя",now);Check(ledger.Get(111).FirstSeenUtc==now,"first join persisted in UTC");
        ledger.Visit(111,"Первое имя",now.AddMinutes(1));Check(ledger.Get(111).Names.Count==1,"unchanged name does not duplicate history");
        ledger.Visit(111,"Новое имя",now.AddMinutes(2));Check(ledger.Get(111).Names.Select(n=>n.Name).SequenceEqual(new[]{"Новое имя","Первое имя"}),"name history records changes newest first");
        ledger.Visit(111,"Новое имя",now.AddMinutes(1));Check(ledger.Get(111).LastSeenUtc==now.AddMinutes(2),"backward wall time does not lose last seen");
        ledger.AddPlaytime(111,7200,now.AddHours(2));Check(new ProfileLedger(profiles).Get(111).PlaySeconds==7200,"playtime survives restart");
        var detached=ledger.Get(111);detached.Names[0].Name="injected";detached.Records.Clear();Check(ledger.Get(111).Names[0].Name=="Новое имя","external DTO mutation cannot alter saved profile");
        var trust=new TrustPolicy();Check(trust.Calculate(ledger.Get(111))==54,"trust uses completed hours and configured rate");
        string note=Id();ledger.Record(111,777,"note","заметка",note,now);Check(trust.Calculate(ledger.Get(111))==54,"notation does not reduce trust");
        string warning=Id();ledger.Record(111,777,"warn","нарушение",warning,now);Check(trust.Calculate(ledger.Get(111))==44,"warning lowers score");
        Check(!ledger.Record(111,777,"warn","нарушение",warning,now.AddSeconds(1))&&ledger.Get(111).Records.Count(r=>r.Kind=="warn")==1,"operation id repeat is idempotent across save");
        Fails(()=>ledger.Record(111,778,"warn","другая причина",warning,now),"operation id cannot authorize another actor or payload");
        ledger.Record(111,777,"commend","помощь новичку",Id(),now);Check(trust.Calculate(ledger.Get(111))==49,"commendation raises trust");
        string kick=Id();ledger.Record(111,777,"kick","проверка",kick,now);Check(trust.Calculate(ledger.Get(111))==49,"pending kick does not count as applied punishment");
        ledger.SetKickResult(111,kick,true);Check(trust.Calculate(ledger.Get(111))==34,"applied kick affects trust");
        ledger.Revoke(111,warning,777,"ошибочное предупреждение");Check(trust.Calculate(ledger.Get(111))==44&&ledger.Get(111).Records.Any(r=>r.Id==warning&&r.RevokedBy=="777"),"revoke preserves audit and recalculates trust");
        Fails(()=>ledger.Revoke(111,warning,777,"повтор"),"repeated revoke is rejected");
        string ban=Id();ledger.Record(111,777,"ban","временная блокировка",ban,now,60);Check(ledger.ActivePunishment(111,"ban",now)!=null,"durable pending ban fails closed before native result");ledger.SetBanApplied(111,ban);
        Check(new ProfileLedger(profiles).ActivePunishment(111,"ban",now.AddSeconds(59))!=null,"timed ban survives restart before boundary");
        Check(ledger.ActivePunishment(111,"ban",now.AddSeconds(60))==null,"timed ban expires exactly on UTC boundary");
        Check(ledger.Expire(111,now.AddSeconds(61)).Length==1&&new ProfileLedger(profiles).Get(111).Records.Single(r=>r.Id==ban).Status=="expired","expiration transition is persisted for history");
        Check(ledger.Expire(111,now.AddSeconds(62)).Length==0,"expiration is emitted once");
        string permanent=Id();ledger.Record(111,777,"ban","бессрочно",permanent,now);ledger.SetBanApplied(111,permanent);Check(ledger.ActivePunishment(111,"ban",now.AddYears(10))!=null,"permanent ban has no invented expiry");
        string mute=Id();ledger.Record(111,777,"mute","флуд",mute,now,120);Check(ledger.ActivePunishment(111,"mute",now.AddSeconds(119))!=null,"mute shares persistent timer semantics");ledger.Revoke(111,mute,777,"ограничение снято");Check(ledger.ActivePunishment(111,"mute",now.AddSeconds(10))==null,"unmute stops enforcement while keeping history");
        Check(StaffPolicy.DurationSeconds("30m")==1800&&StaffPolicy.DurationSeconds("2h")==7200&&StaffPolicy.DurationSeconds("perm")==0,"duration parser supports explicit units and permanent option");
        Fails(()=>StaffPolicy.DurationSeconds("-10m"),"negative duration rejected");Fails(()=>StaffPolicy.DurationSeconds("99999999999999999999d"),"duration overflow rejected");Fails(()=>StaffPolicy.DurationSeconds("tomorrow"),"nonnumeric duration rejected");
        Fails(()=>ledger.Visit(222,"x",DateTime.Now),"local time is not silently accepted as UTC");
        var permissions=new HashSet<string>{"mre.staff.ban"};Check(!StaffPolicy.Can("ban",permissions.Contains),"action node alone cannot open staff capability");permissions.Add("mre.staff");Check(StaffPolicy.Can("ban",permissions.Contains),"base and action permit intended operation");permissions.Remove("mre.staff.ban");Check(!StaffPolicy.Can("ban",permissions.Contains),"permission revocation affects next call");Check(!StaffPolicy.Can("execute",p=>true),"arbitrary console action denied even with all known rights");
        Check(StaffPolicy.SafeTrustGroup("mre_trust_regular")&&!StaffPolicy.SafeTrustGroup("mre_admin")&&!StaffPolicy.SafeTrustGroup("vip")&&!StaffPolicy.SafeTrustGroup("mre_trust_../admin"),"trust group names cannot target admin donor or path-like values");
        Fails(()=>ledger.SetManagedGroup(111,"mre_admin"),"ledger refuses an administrative managed group");
        ledger.Log(111,"command","attempt","/password secret-value",now);Check(ledger.Get(111).Messages[0].Text=="password [аргументы скрыты]","sensitive command arguments are never saved");
        for(int i=0;i<205;i++)ledger.Log(111,"chat","GLOBAL","message"+i,now.AddSeconds(i));Check(ledger.Get(111).Messages.Count==200&&ledger.Get(111).Messages[0].Text=="message204","message history is bounded newest first");
        ledger.Visit(333,"write failure",now);Directory.CreateDirectory(Path.Combine(profiles,"333.json.previous"));Fails(()=>ledger.Record(333,777,"warn","must not commit",Id(),now),"write failure is visible before history is committed");Check(ledger.Get(333).Records.Count==0,"failed atomic replace leaves previous profile unchanged");
        File.WriteAllText(Path.Combine(profiles,"444.json"),"broken");Fails(()=>ledger.Get(444),"corrupt profile fails closed and is not replaced");
        string queuePath=Path.Combine(root,"outbox.json");var outbox=new WebhookOutbox(queuePath);string eventId=Id();outbox.Enqueue(eventId,"event",now);outbox.Enqueue(eventId,"duplicate",now);Check(outbox.Count==1,"outbox event id deduplicates before delivery");
        int attempts=0;Check(outbox.Pump(now,s=>{attempts++;return false;})&&attempts==1&&outbox.Count==1,"failed delivery remains queued");Check(!outbox.Pump(now.AddSeconds(1),s=>true),"retry obeys backoff");outbox=new WebhookOutbox(queuePath);Check(outbox.Count==1&&outbox.Pump(now.AddSeconds(3),s=>true)&&outbox.Count==0,"pending delivery survives restart and successful acknowledgment removes it");
        Check(WebhookOutbox.SafeEndpoint("https://discord.com/api/webhooks/123/token")&&!WebhookOutbox.SafeEndpoint("http://discord.com/api/webhooks/123/token")&&!WebhookOutbox.SafeEndpoint("https://evil.example/api/webhooks/123/token")&&!WebhookOutbox.SafeEndpoint("https://discord.com.evil.example/api/webhooks/123/token")&&!WebhookOutbox.SafeEndpoint("https://user@discord.com/api/webhooks/123/token"),"webhook validation rejects alternate schemes hosts and credentials");
        Console.WriteLine("MreModeration: "+passed+" проверок PASS; "+root);
    }
}
