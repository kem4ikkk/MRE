using RestoreMonarchy.Teleportation;
using RestoreMonarchy.Teleportation.Utils;
using Rocket.Core.Utils;
using Rocket.Unturned.Player;
using SDG.Unturned;
using RestoreMonarchy.Teleportation.Components;
using System.Reflection;

static void Assert(bool result, string message)
{
    if (!result) throw new Exception(message);
}

static (TeleportationPlugin plugin, UnturnedPlayer sender, UnturnedPlayer target) Setup()
{
    TaskDispatcher.Pending.Clear();
    PlayerTool.Players.Clear();
    UnturnedPlayer.Players.Clear();
    var plugin = new TeleportationPlugin();
    TeleportationPlugin.Instance = plugin;
    return (plugin, UnturnedPlayer.Create(1), UnturnedPlayer.Create(2));
}

var cases = new List<(string name, Action test)>
{
    ("actual TPA command accepts a alias case-insensitively exactly once", () => {
        foreach(string alias in new[]{"a","A"}) {
            var (p,s,t)=Setup(); p.SendTPARequest(s,t); var command=new RestoreMonarchy.Teleportation.Commands.TPACommand();
            command.Execute(t,new[]{alias}); command.Execute(t,new[]{alias});
            Assert(TaskDispatcher.Pending.Count==1 && p.TPRequests.Single().IsExecuting,"TPA a alias did not accept exactly one request");
            TaskDispatcher.RunAll(); Assert(s.Teleports==1,"Accepted alias request did not transfer sender");
        }
    }),
    ("actual TPA command d alias denies pending and executing requests", () => {
        foreach(bool accepted in new[]{true,false}) {
            var (p,s,t)=Setup(); p.SendTPARequest(s,t); if(accepted)p.AcceptTPARequest(t);
            new RestoreMonarchy.Teleportation.Commands.TPACommand().Execute(t,new[]{"d"}); TaskDispatcher.RunAll();
            Assert(p.TPRequests.Count==0 && s.Teleports==0,"TPA d alias did not stop the request");
        }
    }),
    ("actual TPA command d cancels sender pending outgoing request", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t);
        new RestoreMonarchy.Teleportation.Commands.TPACommand().Execute(s,new[]{"d"});
        Assert(p.TPRequests.Count==0 && TaskDispatcher.Pending.Count==0,"Sender /tpa d did not cancel outgoing pending request");
    }),
    ("actual TPA command d cancels sender accepted outgoing request", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t);
        new RestoreMonarchy.Teleportation.Commands.TPACommand().Execute(s,new[]{"d"}); TaskDispatcher.RunAll();
        Assert(p.TPRequests.Count==0 && s.Teleports==0,"Sender /tpa d did not cancel accepted outgoing countdown");
    }),
    ("actual TPA command c alias cancels own executing request", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t);
        new RestoreMonarchy.Teleportation.Commands.TPACommand().Execute(s,new[]{"c"}); TaskDispatcher.RunAll();
        Assert(p.TPRequests.Count==0 && s.Teleports==0,"TPA c alias did not cancel own countdown");
    }),
    ("selected TPA XML enforces hundred-second cooldown", () => {
        string path=Path.Combine(Environment.CurrentDirectory,"config","plugins","Teleportation","Teleportation.configuration.xml");
        using var stream=File.OpenRead(path);
        var selected=(TeleportationConfiguration)new System.Xml.Serialization.XmlSerializer(typeof(TeleportationConfiguration)).Deserialize(stream);
        Assert(selected.TPACooldown==100 && selected.TPADelay==5 && !selected.CancelOnMove,"Selected TPA timings do not match request");
        var (p,s,t)=Setup(); p.Configuration.Instance.TPACooldown=selected.TPACooldown;
        p.Cooldowns[s.CSteamID]=DateTime.Now.AddSeconds(-99); p.SendTPARequest(s,t);
        Assert(p.TPRequests.Count==0,"TPA cooldown ended before hundred seconds");
        p.Cooldowns[s.CSteamID]=DateTime.Now.AddSeconds(-101); p.SendTPARequest(s,t);
        Assert(p.TPRequests.Count==1,"TPA cooldown did not end after hundred seconds");
    }),
    ("actual defaults use five seconds and free movement", () => {
        var (p,_,_)=Setup(); var config=p.Configuration.Instance;
        Assert(config.TPADelay==5 && !config.CancelOnMove,"Actual defaults do not allow free movement during five-second TPA");
        Assert(config.EnablePluginHotkeys && config.AcceptPluginKey==0 && config.DenyPluginKey==1,"Default plugin keys do not match comma/period");
    }),
    ("accepted request schedules five seconds without instant teleport", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); var request=p.TPRequests.Single();
        Assert(request.ExecuteAtUtc==null && request.RemainingSeconds==0,"Unaccepted request already has a countdown");
        var earliest=DateTime.UtcNow.AddSeconds(5); p.AcceptTPARequest(t); var latest=DateTime.UtcNow.AddSeconds(5);
        Assert(TaskDispatcher.Pending.Single().delay==5 && s.Teleports==0,"Accepted request did not queue exactly five seconds");
        Assert(request.ExecuteAtUtc>=earliest && request.ExecuteAtUtc<=latest,"Countdown deadline disagrees with acceptance time");
        Assert(request.RemainingSeconds>0 && request.RemainingSeconds<=5,"Countdown is outside the accepted delay");
    }),
    ("both players move and arrival uses recipient current position", () => {
        var (p,s,t)=Setup(); t.Player.transform.position=new UnityEngine.Vector3(1,0,1);
        p.SendTPARequest(s,t); p.AcceptTPARequest(t);
        s.Player.transform.position=new UnityEngine.Vector3(10,0,10);
        t.Player.transform.position=new UnityEngine.Vector3(20,0,30);
        Assert(p.MovementDetector.Players.Count==0,"Free-movement TPA registered a movement cancellation");
        TaskDispatcher.RunAll();
        Assert(s.Teleports==1 && s.Position==t.Position,"Movement cancelled TPA or used the old recipient position");
    }),
    ("unsafe option also uses recipient current position", () => {
        var (p,s,t)=Setup(); p.Configuration.Instance.UseUnsafeTeleport=true;
        p.SendTPARequest(s,t); p.AcceptTPARequest(t); t.Player.transform.position=new UnityEngine.Vector3(30,2,40);
        TaskDispatcher.RunAll();
        Assert(s.Player.UnsafeTeleports==1 && s.Position==t.Position,"Unsafe path captured an obsolete target position");
    }),
    ("request state notifications cover both players through completion", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t); TaskDispatcher.RunAll();
        Assert(p.StateNotifications.Count(x=>x==s.CSteamID)==3 && p.StateNotifications.Count(x=>x==t.CSteamID)==3,"Both UI owners were not notified on create accept and cleanup");
        Assert(p.GetUiRequest(s.CSteamID)==null && p.GetUiRequest(t.CSteamID)==null,"Completed request is still visible");
    }),
    ("accept hotkey cannot accept own outgoing request", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.HandleTPAUiAction(s,true);
        Assert(TaskDispatcher.Pending.Count==0,"Sender accepted their own outgoing request");
        p.HandleTPAUiAction(t,true); p.HandleTPAUiAction(t,true);
        Assert(TaskDispatcher.Pending.Count==1,"Recipient accept was ignored or duplicated");
    }),
    ("deny hotkey cancels displayed accepted request for either participant", () => {
        foreach(var cancelSender in new[]{true,false}) {
            var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.HandleTPAUiAction(t,true); var request=p.TPRequests.Single();
            p.HandleTPAUiAction(cancelSender?s:t,false); var notifications=p.StateNotifications.Count;
            request.Cancel(); TaskDispatcher.RunAll();
            Assert(s.Teleports==0 && p.TPRequests.Count==0 && request.RemainingSeconds==0,"Hotkey cancellation did not stop both countdowns");
            Assert(notifications==p.StateNotifications.Count,"Repeated cancellation sent duplicate cleanup");
        }
    }),
    ("incoming request has priority over own pending outgoing request", () => {
        var (p,s,t)=Setup(); var third=UnturnedPlayer.Create(3); p.SendTPARequest(t,third); p.SendTPARequest(s,t);
        var displayed=p.GetUiRequest(t.CSteamID); Assert(displayed.Sender==s.CSteamID,"UI showed outgoing request ahead of incoming request");
        p.HandleTPAUiAction(t,false);
        Assert(p.TPRequests.Single().Sender==t.CSteamID,"Deny hotkey cancelled a different request than UI displayed");
    }),
    ("executing request has priority and prevents second acceptance", () => {
        var (p,s,t)=Setup(); var third=UnturnedPlayer.Create(3); p.SendTPARequest(s,t); p.SendTPARequest(t,third);
        p.AcceptTPARequest(third); var displayed=p.GetUiRequest(t.CSteamID);
        Assert(displayed.IsExecuting && displayed.Sender==t.CSteamID,"Pending incoming request hid active outgoing countdown");
        p.AcceptTPARequest(t);
        Assert(TaskDispatcher.Pending.Count==1,"One player entered two executing teleports");
        p.HandleTPAUiAction(t,false);
        Assert(p.TPRequests.Single().Sender==s.CSteamID,"Cancel hotkey did not cancel the active displayed countdown");
    }),
    ("multiple inbound requests are accepted in FIFO order", () => {
        var (p,s,t)=Setup(); var third=UnturnedPlayer.Create(3); p.SendTPARequest(s,t); p.SendTPARequest(third,t);
        p.HandleTPAUiAction(t,true); TaskDispatcher.RunAll();
        Assert(s.Teleports==1 && third.Teleports==0 && p.GetUiRequest(t.CSteamID).Sender==third.CSteamID,"FIFO or next request UI was lost");
    }),
    ("new requests cannot involve an executing participant", () => {
        var (p,s,t)=Setup(); var third=UnturnedPlayer.Create(3); p.SendTPARequest(s,t); p.AcceptTPARequest(t);
        p.SendTPARequest(third,t); p.SendTPARequest(t,third);
        Assert(p.TPRequests.Count==1 && TaskDispatcher.Pending.Count==1,"Busy participant acquired a parallel request");
    }),
    ("expired requests cannot be acted on by hotkey", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.TPRequests.Single().ExpireDate=DateTime.Now.AddSeconds(-1);
        p.HandleTPAUiAction(t,true);
        Assert(p.GetUiRequest(t.CSteamID)==null && TaskDispatcher.Pending.Count==0,"Hotkey accepted an expired request");
    }),
    ("deny hotkey skips expired first inbound and cancels displayed request", () => {
        var (p,s,t)=Setup(); var third=UnturnedPlayer.Create(3); p.SendTPARequest(s,t); p.SendTPARequest(third,t);
        var expired=p.TPRequests[0]; expired.ExpireDate=DateTime.Now.AddSeconds(-1);
        Assert(p.GetUiRequest(t.CSteamID).Sender==third.CSteamID,"Expired first request hid the next request");
        p.HandleTPAUiAction(t,false);
        Assert(p.TPRequests.Single()==expired && p.GetUiRequest(t.CSteamID)==null,"Deny cancelled a different request from the displayed card");
    }),
    ("teleport exception still clears both UI owners and preserves cooldown", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t); s.ThrowOnTeleport=true;
        try { TaskDispatcher.RunAll(); throw new Exception("Expected simulated teleport failure"); }
        catch(InvalidOperationException) { }
        Assert(p.TPRequests.Count==0 && p.MovementDetector.Players.Count==0,"Failed teleport leaked request or detector");
        Assert(p.Cooldowns.ContainsKey(s.CSteamID) && p.StateNotifications.Count==6,"Failure refunded cooldown or omitted UI cleanup");
    }),
    ("plugin key fires only on rising edge", () => {
        var keys = new PluginKeyEdges();
        Assert(keys.IsRising(1,0,true),"First press ignored");
        Assert(!keys.IsRising(1,0,true),"Held key repeated action");
        Assert(!keys.IsRising(1,0,false),"Release fired action");
        Assert(keys.IsRising(1,0,true),"Second press ignored");
    }),
    ("plugin keys and players have independent states", () => {
        var keys = new PluginKeyEdges();
        Assert(keys.IsRising(1,0,true) && keys.IsRising(1,1,true) && keys.IsRising(2,0,true),"State leaked between keys or players");
        Assert(!keys.IsRising(1,5,true),"Invalid plugin key accepted");
        keys.Remove(1); Assert(keys.IsRising(1,0,true),"Disconnect did not clear state");
    }),
    ("pending hook reports create accept and finish", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t);
        Assert(p.PendingNotifications.Count==1,"Creation not reported");
        p.AcceptTPARequest(t); Assert(p.PendingNotifications.Count==2,"Accept not reported");
        TaskDispatcher.RunAll(); Assert(p.PendingNotifications.Count==3,"Completion not reported");
    }),
    ("recipient in combat before send", () => {
        var (p,s,t)=Setup(); p.Combat.Add(t.CSteamID); p.SendTPARequest(s,t);
        Assert(p.TPRequests.Count==0 && TaskDispatcher.Pending.Count==0,"Blocked recipient accepted a new request");
    }),
    ("sender in combat before send", () => {
        var (p,s,t)=Setup(); p.Combat.Add(s.CSteamID); p.SendTPARequest(s,t);
        Assert(p.TPRequests.Count==0,"Blocked sender created a request");
    }),
    ("recipient begins raid before accept", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.Raid.Add(t.CSteamID); p.AcceptTPARequest(t);
        Assert(p.TPRequests.Count==0 && TaskDispatcher.Pending.Count==0,"Blocked recipient accepted a queued request");
    }),
    ("recipient begins combat during delay", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t); p.Combat.Add(t.CSteamID); TaskDispatcher.RunAll();
        Assert(s.Teleports==0 && p.TPRequests.Count==0,"Combat started during delay but teleport happened");
        Assert(p.Cooldowns.ContainsKey(s.CSteamID),"Rejected teleport refunded the anti-spam cooldown");
    }),
    ("recipient begins raid during delay", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t); p.Raid.Add(t.CSteamID); TaskDispatcher.RunAll();
        Assert(s.Teleports==0,"Raid started during delay but teleport happened");
    }),
    ("duplicate accept and parallel outgoing request", () => {
        var (p,s,t)=Setup(); var third=UnturnedPlayer.Create(3); p.SendTPARequest(s,t); p.AcceptTPARequest(t); p.AcceptTPARequest(t);
        p.Cooldowns.Clear(); p.SendTPARequest(s,third);
        Assert(TaskDispatcher.Pending.Count==1 && p.TPRequests.Count==1,"A duplicate request scheduled another teleport");
        TaskDispatcher.RunAll(); Assert(s.Teleports==1 && p.TPRequests.Count==0,"Successful request did not complete exactly once");
    }),
    ("cancel accepted teleport", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t); p.CancelTPARequest(s); TaskDispatcher.RunAll();
        Assert(s.Teleports==0 && p.MovementDetector.Players.Count==0,"Cancel failed during delay");
    }),
    ("deny accepted teleport", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t); p.DenyTPARequest(t); TaskDispatcher.RunAll();
        Assert(s.Teleports==0,"Deny failed during delay");
    }),
    ("disconnect then reconnect before completion", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t); p.ClearPlayerRequests(t.CSteamID); UnturnedPlayer.Create(2); TaskDispatcher.RunAll();
        Assert(s.Teleports==0 && p.TPRequests.Count==0,"Old accepted request survived reconnect");
    }),
    ("changed player object without disconnect callback", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t); UnturnedPlayer.Create(2); TaskDispatcher.RunAll();
        Assert(s.Teleports==0,"Stale target object was still used");
    }),
    ("recipient dies during delay", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.AcceptTPARequest(t); t.Dead=true; TaskDispatcher.RunAll();
        Assert(s.Teleports==0,"Dead recipient received teleport");
    }),
    ("expired request can be replaced", () => {
        var (p,s,t)=Setup(); p.SendTPARequest(s,t); p.TPRequests[0].ExpireDate=DateTime.Now.AddSeconds(-1); p.Cooldowns.Clear();
        p.SendTPARequest(s,t); p.AcceptTPARequest(t); TaskDispatcher.RunAll();
        Assert(s.Teleports==1 && p.TPRequests.Count==0,"Expired request permanently blocked the sender");
    }),
    ("unrelated solo player is not a base group member", () => {
        Assert(!RaidOwnershipRules.IsOwnerOrGroupMember(1,0,2,0),"Zero groups bypassed solo-base raid protection");
    }),
    ("solo owner is recognized by SteamID", () => {
        Assert(RaidOwnershipRules.IsOwnerOrGroupMember(1,0,1,0),"Actual solo owner was not recognized");
    }),
    ("nonzero group member is recognized", () => {
        Assert(RaidOwnershipRules.IsOwnerOrGroupMember(1,99,2,99),"An actual group member was treated as unrelated");
    }),
    ("different nonzero group is unrelated", () => {
        Assert(!RaidOwnershipRules.IsOwnerOrGroupMember(1,99,2,88),"Unrelated group was allowed as owner");
    }),
    ("unrelated solo online does not make owner online", () => {
        var online = new[] { (id:2UL, group:0UL), (id:3UL, group:0UL) };
        Assert(!online.Any(p => RaidOwnershipRules.IsOwnerOrGroupMember(1,0,p.id,p.group)),"Unrelated solos falsely made owner online");
    }),
    ("online owner is recognized despite current group change", () => {
        Assert(RaidOwnershipRules.IsOwnerOrGroupMember(1,99,1,88),"Owner's current group hid their online presence");
    }),
    ("movement detector skips removed player", () => {
        var (_,s,_)=Setup();
        var detector = new MovementDetectorComponent();
        typeof(MovementDetectorComponent).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(detector,null);
        bool moved = false;
        detector.AddPlayer(s.Player,() => moved=true);
        var entries=(System.Collections.IEnumerable)typeof(MovementDetectorComponent).GetProperty("Players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(detector)!;
        var entry=entries.Cast<object>().Single();
        entry.GetType().GetProperty("Player")!.SetValue(entry,null);
        typeof(MovementDetectorComponent).GetMethod("CheckMovement",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(detector,null);
        Assert(!moved,"Removed player unexpectedly invoked movement callback");
    }),
    ("movement detector still cancels actual movement", () => {
        var (_,s,_)=Setup();
        var detector = new MovementDetectorComponent();
        typeof(MovementDetectorComponent).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(detector,null);
        bool moved=false;
        detector.AddPlayer(s.Player,() => moved=true);
        s.Player.transform.position=new UnityEngine.Vector3(1,0,0);
        typeof(MovementDetectorComponent).GetMethod("CheckMovement",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(detector,null);
        Assert(moved,"Actual movement no longer cancels teleport");
    })
};

foreach (var (name,test) in cases) { test(); Console.WriteLine("PASS: "+name); }
int homeCount=HomeChecks.Run();
Console.WriteLine($"Passed {cases.Count+homeCount} behavioral tests ({cases.Count} TPA, {homeCount} HOME). Unity/Steam runtime smoke is separate.");
