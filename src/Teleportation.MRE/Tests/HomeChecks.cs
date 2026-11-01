using RestoreMonarchy.MoreHomes;
using RestoreMonarchy.MoreHomes.Commands;
using RestoreMonarchy.MoreHomes.Helpers;
using RestoreMonarchy.MoreHomes.Models;
using RestoreMonarchy.Teleportation;
using RestoreMonarchy.Teleportation.Models;
using RestoreMonarchy.Teleportation.Utils;
using Rocket.API;
using Rocket.Core.Utils;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System.Xml.Serialization;
using UnityEngine;

internal static class HomeChecks
{
    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static string FindConfig()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string path=Path.Combine(dir.FullName,"config","plugins","MoreHomes","MoreHomes.configuration.xml");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Cannot find actual repository MoreHomes XML");
    }

    private static (TeleportationPlugin tpa, MoreHomesPlugin homes, UnturnedPlayer player, PlayerHome home) Setup()
    {
        TaskDispatcher.Pending.Clear();
        PlayerTool.Players.Clear(); UnturnedPlayer.Players.Clear(); RocketPlayer.Permissions.Clear();
        LevelGround.Height=0;
        var tpa=new TeleportationPlugin(); TeleportationPlugin.Instance=tpa;
        var homes=new MoreHomesPlugin(); MoreHomesPlugin.Instance=homes;
        using (var reader=File.OpenRead(FindConfig()))
            homes.Configuration.Instance=(MoreHomesConfiguration)new XmlSerializer(typeof(MoreHomesConfiguration)).Deserialize(reader);
        var player=UnturnedPlayer.Create(1);
        var bed=new InteractableBed { owner=player.CSteamID };
        bed.transform.position=new Vector3(10,1,20);
        var home=new PlayerHome("bed",bed);
        HomesHelper.GetOrCreatePlayer(player.CSteamID).Homes.Add(home);
        return (tpa,homes,player,home);
    }

    private static void Home(UnturnedPlayer player, params string[] args) => new HomeCommand().Execute(player,args);

    public static int Run()
    {
        var tests=new List<(string name, Action test)>
        {
            ("HOME actual XML has a positive home limit and eighty-second cooldown",()=>{
                var (_,h,_,_)=Setup(); var c=h.Configuration.Instance;
                Assert(c.DefaultMaxHomes>0 && c.DefaultHomeCooldown==80,"Existing limits or cooldown were reset");
                Assert(c.DefaultHomeDelay==5 && !c.CancelOnMove,"HOME XML does not match five seconds and movement");
            }),
            ("HOME real command schedules five seconds and permits movement to current bed",()=>{
                var (t,h,p,bed)=Setup(); Home(p);
                Assert(TaskDispatcher.Pending.Single().delay==5 && p.Player.HomeTeleports==0,"HOME did not queue exactly five seconds");
                Assert(t.GetHomeUiRequest(p.CSteamID).HomeName=="bed","HOME UI did not get selected home name");
                p.Player.transform.position=new Vector3(100,0,100); bed.InteractableBed.transform.position=new Vector3(12,2,34);
                Assert(t.MovementDetector.Players.Count==0,"HOME requires standing still");
                TaskDispatcher.RunAll();
                Assert(p.Position==new Vector3(12,2.5f,34) && p.Player.HomeTeleports==1,"HOME captured obsolete bed position or failed after movement");
                Assert(h.Messages.Contains("HomeSuccess") && t.HomeRequests.Count==0,"HOME did not finish with UI cleanup");
            }),
            ("HOME cooldown begins once and prevents duplicate requests",()=>{
                var (_,h,p,_)=Setup(); var before=DateTime.Now.AddSeconds(80); Home(p); var until=h.PlayerCooldowns[p.Id]; Home(p);
                Assert(until>=before && until<=DateTime.Now.AddSeconds(80),"Actual cooldown did not use eighty seconds");
                Assert(h.PlayerCooldowns[p.Id]==until && TaskDispatcher.Pending.Count==1 && h.Messages.Contains("HomeCooldown"),"Duplicate HOME rescheduled or changed cooldown");
            }),
            ("HOME command cancel stops transfer and preserves cooldown",()=>{
                var (t,h,p,_)=Setup(); Home(p); var cooldown=h.PlayerCooldowns[p.Id]; Home(p,"cancel"); TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0 && h.PlayerCooldowns[p.Id]==cooldown,"HOME cancel did not clean up or refunded cooldown");
                Assert(h.Messages.Contains("HomeCanceled"),"HOME cancel was not acknowledged");
            }),
            ("HOME missing guard provider fails closed",()=>{
                var (_,h,p,_)=Setup(); TeleportationPlugin.Instance=null; Home(p);
                Assert(TaskDispatcher.Pending.Count==0 && h.PlayerCooldowns.Count==0 && h.Messages.Contains("HomeUnavailable"),"HOME bypassed missing combat provider");
            }),
            ("HOME unknown bed does not charge cooldown",()=>{
                var (_,h,p,_)=Setup(); Home(p,"missing");
                Assert(TaskDispatcher.Pending.Count==0 && h.PlayerCooldowns.Count==0 && h.Messages.Contains("NoHome"),"Missing home created an operation");
            }),
            ("HOME combat before command blocks without cooldown",()=>{
                var (t,h,p,_)=Setup(); t.Combat.Add(p.CSteamID); Home(p);
                Assert(TaskDispatcher.Pending.Count==0 && h.PlayerCooldowns.Count==0,"HOME bypassed combat before scheduling");
            }),
            ("HOME raid before command blocks without cooldown",()=>{
                var (t,h,p,_)=Setup(); t.Raid.Add(p.CSteamID); Home(p);
                Assert(TaskDispatcher.Pending.Count==0 && h.PlayerCooldowns.Count==0,"HOME bypassed raid before scheduling");
            }),
            ("HOME combat during countdown cancels without cooldown refund",()=>{
                var (t,h,p,_)=Setup(); Home(p); t.Combat.Add(p.CSteamID); TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0 && h.PlayerCooldowns.ContainsKey(p.Id),"HOME bypassed combat during countdown");
            }),
            ("HOME raid during countdown cancels without cooldown refund",()=>{
                var (t,h,p,_)=Setup(); Home(p); t.Raid.Add(p.CSteamID); TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0 && h.PlayerCooldowns.ContainsKey(p.Id),"HOME bypassed raid during countdown");
            }),
            ("HOME death before command does not schedule",()=>{
                var (_,h,p,_)=Setup(); p.Dead=true; Home(p);
                Assert(TaskDispatcher.Pending.Count==0 && h.PlayerCooldowns.Count==0,"Dead player scheduled HOME");
            }),
            ("HOME death during countdown clears operation",()=>{
                var (t,_,p,_)=Setup(); Home(p); p.Dead=true; TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0,"Dead player was teleported home");
            }),
            ("HOME stale player after reconnect cannot transfer",()=>{
                var (t,_,p,_)=Setup(); Home(p); UnturnedPlayer.Create(1); TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0,"HOME survived player object replacement");
            }),
            ("HOME guard plugin replacement invalidates old callback",()=>{
                var (t,_,p,_)=Setup(); Home(p); TeleportationPlugin.Instance=new TeleportationPlugin(); TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0,"Old Teleportation callback transferred a player");
            }),
            ("HOME MoreHomes unload or replacement invalidates destination",()=>{
                var (t,_,p,_)=Setup(); Home(p); MoreHomesPlugin.Instance=new MoreHomesPlugin(); TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0,"Old MoreHomes owner callback transferred a player");
            }),
            ("HOME destroyed bed removes registration and blocks arrival",()=>{
                var (t,h,p,bed)=Setup(); Home(p); bed.InteractableBed=null; TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0 && !HomesHelper.GetOrCreatePlayer(p.CSteamID).Homes.Contains(bed),"Destroyed bed remained a destination");
                Assert(h.Messages.Contains("BedDestroyed"),"Destroyed bed failure did not reach player");
            }),
            ("HOME inactive bed blocks arrival",()=>{
                var (t,_,p,bed)=Setup(); Home(p); bed.InteractableBed.isActiveAndEnabled=false; TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0,"Inactive bed remained a destination");
            }),
            ("HOME bed ownership change blocks arrival",()=>{
                var (t,_,p,bed)=Setup(); Home(p); bed.InteractableBed.owner=new Steamworks.CSteamID(99); TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0,"Player arrived at another owner's bed");
            }),
            ("HOME deleted registration blocks arrival despite surviving bed",()=>{
                var (t,_,p,bed)=Setup(); Home(p); HomesHelper.RemoveHome(p.CSteamID,bed); TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0,"Deleted home remained a destination");
            }),
            ("HOME replacement with same name does not revive old destination",()=>{
                var (t,_,p,bed)=Setup(); Home(p); HomesHelper.RemoveHome(p.CSteamID,bed);
                HomesHelper.GetOrCreatePlayer(p.CSteamID).Homes.Add(new PlayerHome("bed",new InteractableBed { owner=p.CSteamID }));
                TaskDispatcher.RunAll(); Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0,"Same-name replacement revived old operation");
            }),
            ("HOME driving before and during countdown is blocked",()=>{
                foreach(bool before in new[]{true,false}) {
                    var (t,h,p,_)=Setup(); if(before)p.Stance=EPlayerStance.DRIVING; Home(p); p.Stance=EPlayerStance.DRIVING; TaskDispatcher.RunAll();
                    Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0 && h.Messages.Contains("WhileDriving"),"Driving restriction was bypassed");
                    Assert(h.PlayerCooldowns.ContainsKey(p.Id)!=before,"Unscheduled HOME charged or scheduled HOME refunded cooldown");
                }
            }),
            ("HOME underground bed before and during countdown is blocked",()=>{
                foreach(bool before in new[]{true,false}) {
                    var (t,h,p,_)=Setup(); if(before)LevelGround.Height=10; Home(p); LevelGround.Height=10; TaskDispatcher.RunAll();
                    Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0 && h.Messages.Contains("CantTeleportToBedUnderground"),"Underground bed restriction was bypassed");
                }
            }),
            ("HOME safe teleport rejection reports failure and cleans UI",()=>{
                var (t,h,p,_)=Setup(); Home(p); p.Player.HomeTeleportSucceeds=false; TaskDispatcher.RunAll();
                Assert(p.Player.HomeTeleports==0 && t.HomeRequests.Count==0 && h.Messages.Contains("HomeTeleportationFailed") && !h.Messages.Contains("HomeSuccess"),"Failed safe teleport was reported as success");
                Assert(h.PlayerCooldowns.ContainsKey(p.Id),"Failed safe teleport refunded cooldown");
            }),
            ("HOME teleport exception cleans operation and retains cooldown",()=>{
                var (t,h,p,_)=Setup(); Home(p); p.Player.ThrowOnHomeTeleport=true;
                try { TaskDispatcher.RunAll(); throw new Exception("Expected home API exception"); } catch(InvalidOperationException) { }
                Assert(t.HomeRequests.Count==0 && h.PlayerCooldowns.ContainsKey(p.Id),"Exception leaked HOME UI or refunded cooldown");
            }),
            ("HOME cannot start for either participant of executing TPA",()=>{
                foreach(bool recipient in new[]{true,false}) {
                    var (t,h,p,_)=Setup(); var other=UnturnedPlayer.Create(2);
                    t.SendTPARequest(recipient?other:p,recipient?p:other); t.AcceptTPARequest(recipient?p:other); Home(p);
                    Assert(t.HomeRequests.Count==0 && h.PlayerCooldowns.Count==0 && TaskDispatcher.Pending.Count==1,"HOME overlapped an executing TPA");
                }
            }),
            ("HOME blocks new TPA to and from player",()=>{
                var (t,_,p,_)=Setup(); Home(p); var other=UnturnedPlayer.Create(2); t.SendTPARequest(p,other); t.SendTPARequest(other,p);
                Assert(t.TPRequests.Count==0 && TaskDispatcher.Pending.Count==1,"TPA overlapped active HOME");
            }),
            ("HOME leaves pending TPA intact and prevents acceptance until canceled",()=>{
                var (t,_,p,_)=Setup(); var other=UnturnedPlayer.Create(2); t.SendTPARequest(other,p); Home(p); t.AcceptTPARequest(p);
                Assert(t.HomeRequests.Count==1 && TaskDispatcher.Pending.Count==1 && !t.TPRequests.Single().IsExecuting,"Pending TPA started during HOME");
                Home(p,"cancel"); t.AcceptTPARequest(p); TaskDispatcher.RunAll();
                Assert(other.Teleports==1 && p.Player.HomeTeleports==0,"HOME cancellation lost pending TPA or left a HOME callback active");
            }),
            ("HOME bed named cancel remains accessible without active operation",()=>{
                var (t,_,p,bed)=Setup(); bed.Name="cancel"; Home(p,"cancel");
                Assert(t.GetHomeUiRequest(p.CSteamID)?.HomeName=="cancel","Cancel subcommand broke an existing bed named cancel");
            }),
            ("HOME stale cancellation cannot remove newer request",()=>{
                var (t,_,p,_)=Setup(); int arrived=0; var owner=new object();
                var old=new HomeTeleportRequest(t,p,"old",()=>true,()=>{arrived++;return true;},owner); Assert(old.Execute(5),"Old request did not start");
                t.HomeRequests.Remove(p.CSteamID);
                var current=new HomeTeleportRequest(t,p,"new",()=>true,()=>true,owner); Assert(current.Execute(5),"Replacement request did not start");
                old.Cancel(); Assert(ReferenceEquals(t.GetHomeUiRequest(p.CSteamID),current),"Old cancellation removed newer HOME");
                TaskDispatcher.RunAll(); Assert(arrived==0,"Canceled old HOME callback still arrived");
            }),
            ("HOME repeated execute and cancel are idempotent",()=>{
                var (t,_,p,_)=Setup(); var request=new HomeTeleportRequest(t,p,"bed",()=>true,()=>true,new object());
                Assert(request.Execute(5) && !request.Execute(5) && TaskDispatcher.Pending.Count==1,"Repeated Execute scheduled a second HOME");
                request.Cancel(); int hooks=t.StateNotifications.Count; request.Cancel(); TaskDispatcher.RunAll();
                Assert(t.HomeRequests.Count==0 && request.RemainingSeconds==0 && t.StateNotifications.Count==hooks,"Repeated cancel sent duplicate state or resurrected countdown");
            }),
            ("HOME VIP delay cannot bypass common five-second countdown",()=>{
                var (_,h,p,_)=Setup(); h.Configuration.Instance.VIPDelays.Add(new VIPPermission("test.instant",0));
                RocketPlayer.Permissions[p.Id]=new HashSet<string>{"test.instant"}; Home(p);
                Assert(TaskDispatcher.Pending.Single().delay==5,"VIP delay bypassed common HOME UI timer");
            }),
            ("HOME deny hotkey works without TPA permission even after home permission is revoked",()=>{
                foreach(bool stillHasHomePermission in new[]{true,false}) {
                    var (t,_,p,_)=Setup(); var other=UnturnedPlayer.Create(2); t.SendTPARequest(other,p); Home(p);
                    RocketPlayer.Permissions[p.Id]=stillHasHomePermission?new HashSet<string>{"home"}:new HashSet<string>();
                    t.HandlePluginUiAction(p,true);
                    Assert(t.HomeRequests.Count==1 && !t.TPRequests.Single().IsExecuting && TaskDispatcher.Pending.Count==1,"Accept key changed active HOME or accepted hidden TPA");
                    t.HandlePluginUiAction(p,false); TaskDispatcher.RunAll();
                    Assert(t.HomeRequests.Count==0 && p.Player.HomeTeleports==0,"Home owner without TPA permission cannot cancel countdown");
                }
            }),
            ("TPA permission remains required for plugin UI accept and deny",()=>{
                var (t,_,p,_)=Setup(); var other=UnturnedPlayer.Create(2); t.SendTPARequest(other,p);
                RocketPlayer.Permissions[p.Id]=new HashSet<string>{"home"};
                t.HandlePluginUiAction(p,true); t.HandlePluginUiAction(p,false);
                Assert(t.TPRequests.Count==1 && !t.TPRequests.Single().IsExecuting && TaskDispatcher.Pending.Count==0,"HOME permission granted TPA hotkey actions");
                RocketPlayer.Permissions[p.Id].Add("tpa"); t.HandlePluginUiAction(p,true);
                Assert(t.TPRequests.Single().IsExecuting && TaskDispatcher.Pending.Count==1,"Authorized TPA hotkey did not accept request");
            })
        };
        foreach(var (name,test) in tests) {test();Console.WriteLine("PASS: "+name);}
        return tests.Count;
    }
}
