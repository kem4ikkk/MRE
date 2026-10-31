// Flight-only derivative of ShimmysAdminTools, retained under GPL-3.0-only.
// See UPSTREAM.md for exact upstream revision and changed behavior.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger=Rocket.Core.Logging.Logger;

namespace MreFlight
{
    public sealed class MreFlightPlugin : RocketPlugin
    {
        private sealed class Session {public UnturnedPlayer Actor;public Player Player;public FlightState State;public MovementValues Applied;}
        public static MreFlightPlugin Instance {get;private set;}
        private readonly Dictionary<ulong,Session> sessions=new Dictionary<ulong,Session>();
        private int mainThread;
        protected override void Load()
        {
            mainThread=Thread.CurrentThread.ManagedThreadId;
            Instance=this;
            Provider.onServerDisconnected+=Disconnected;
            PlayerLife.OnPreDeath+=PreDeath;
            Logger.Log("[MRE] MreFlight загружен: /afly on|off, физический полет; GPL-3.0, upstream ShimmysAdminTools.");
        }
        protected override void Unload()
        {
            Provider.onServerDisconnected-=Disconnected;
            PlayerLife.OnPreDeath-=PreDeath;
            foreach(ulong id in sessions.Keys.ToArray())Stop(id);
            Instance=null;
        }
        private void RequireThread() {if(Thread.CurrentThread.ManagedThreadId!=mainThread)throw new InvalidOperationException("MRE Flight требует главный игровой поток.");}
        private static bool Online(UnturnedPlayer actor) {return actor!=null&&actor.Player!=null&&Provider.clients.Any(p=>p!=null&&p.player==actor.Player&&p.playerID.steamID==actor.CSteamID);}
        private static bool Allowed(UnturnedPlayer actor) {return Online(actor)&&(actor.IsAdmin||(actor.HasPermission("mre.admin")&&actor.HasPermission("mre.admin.fly")));}
        private static bool Usable(UnturnedPlayer actor) {return Online(actor)&&!actor.Player.life.isDead&&actor.Player.movement.getVehicle()==null;}
        public bool IsFlying(UnturnedPlayer actor)
        {
            RequireThread();Session session;return Online(actor)&&sessions.TryGetValue(actor.CSteamID.m_SteamID,out session)&&session.Player==actor.Player&&session.State.Active;
        }
        public string SetFlight(UnturnedPlayer actor,bool enabled)
        {
            RequireThread();if(!Online(actor))return "Игрок не подключен.";
            ulong id=actor.CSteamID.m_SteamID;
            // Exiting one's own flight always remains possible after permission revocation.
            if(!enabled){Stop(id);return "Физический полет выключен.";}
            if(!Allowed(actor))return "Нет прав на полет.";
            if(!Usable(actor))return "Полет доступен живому игроку вне транспорта.";
            if(IsFlying(actor))return "Физический полет уже включен.";
            if(sessions.ContainsKey(id))Stop(id);
            var movement=actor.Player.movement;
            var state=new FlightState(new MovementValues {Gravity=movement.pluginGravityMultiplier,Speed=movement.pluginSpeedMultiplier,Jump=movement.pluginJumpMultiplier});
            var session=new Session {Actor=actor,Player=actor.Player,State=state};
            sessions[id]=session;
            try{Apply(session,state.Tick(false,false,false,true,true),true);}
            catch{Stop(id);throw;}
            Logger.Log("[MRE FLIGHT] enabled actor="+id);
            return "Физический полет включен: WASD — движение, прыжок — вверх, спринт при взгляде вниз — вниз. /afly off — выключить.";
        }
        public string SetSpeed(UnturnedPlayer actor,float horizontal,float vertical)
        {
            RequireThread();if(!Allowed(actor))return "Нет прав на скорость полета.";
            FlightState.Validate(horizontal,vertical);Session session;
            if(!sessions.TryGetValue(actor.CSteamID.m_SteamID,out session)||session.Player!=actor.Player||!session.State.Active)return "Сначала включите /afly on.";
            session.State.SetSpeeds(horizontal,vertical);session.Applied=null;
            return "Скорость полета изменена: "+horizontal.ToString(System.Globalization.CultureInfo.InvariantCulture)+" / "+vertical.ToString(System.Globalization.CultureInfo.InvariantCulture)+".";
        }
        public float[] GetSpeed(UnturnedPlayer actor)
        {
            RequireThread();Session session;
            if(!Allowed(actor)||!sessions.TryGetValue(actor.CSteamID.m_SteamID,out session)||session.Player!=actor.Player)return new[]{1f,1f};
            return new[]{session.State.HorizontalSpeed,session.State.VerticalSpeed};
        }
        private static void Apply(Session session,MovementValues values,bool force=false)
        {
            var movement=session.Player.movement;MovementValues last=session.Applied;
            if(force||last==null||last.Gravity!=values.Gravity)movement.sendPluginGravityMultiplier(values.Gravity);
            if(force||last==null||last.Speed!=values.Speed)movement.sendPluginSpeedMultiplier(values.Speed);
            if(force||last==null||last.Jump!=values.Jump)movement.sendPluginJumpMultiplier(values.Jump);
            session.Applied=values.Copy();
        }
        private void FixedUpdate()
        {
            foreach(var pair in sessions.ToArray()) {
                Session session=pair.Value;
                bool valid=Usable(session.Actor)&&session.Actor.Player==session.Player;
                if(!valid||!Allowed(session.Actor)){Stop(pair.Key);continue;}
                try {
                    bool[] keys=session.Player.input.keys;
                    bool jump=keys!=null&&keys.Length>0&&keys[0];
                    bool sprint=keys!=null&&keys.Length>5&&keys[5];
                    MovementValues values=session.State.Tick(jump,sprint,session.Player.look.pitch>160,true,true);
                    Apply(session,values);
                }catch(Exception error){Logger.LogException(error,"[MRE] Полет отключен после ошибки.");Stop(pair.Key);}
            }
        }
        private void Stop(ulong id)
        {
            Session session;if(!sessions.TryGetValue(id,out session))return;sessions.Remove(id);
            MovementValues previous=session.State.Stop();
            try{if(session.Player!=null&&session.Player.movement!=null)Apply(session,previous,true);}
            catch(Exception error){Logger.LogException(error,"[MRE] Ошибка восстановления параметров движения после полета.");}
            Logger.Log("[MRE FLIGHT] disabled actor="+id);
        }
        private void Disconnected(CSteamID id) {Stop(id.m_SteamID);}
        private void PreDeath(PlayerLife life) {if(life!=null&&life.channel!=null&&life.channel.owner!=null)Stop(life.channel.owner.playerID.steamID.m_SteamID);}
    }
}
