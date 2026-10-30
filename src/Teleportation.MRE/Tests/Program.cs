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
Console.WriteLine($"Passed {cases.Count} behavioral tests. Unity/Steam runtime smoke is separate.");
