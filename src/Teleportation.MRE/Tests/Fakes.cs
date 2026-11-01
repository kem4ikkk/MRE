using RestoreMonarchy.Teleportation.Models;
using Rocket.Unturned.Player;
using Steamworks;

namespace Rocket.API
{
    public interface IRocketPluginConfiguration { void LoadDefaults(); }
    public interface IRocketPlayer { string Id { get; } }
    public enum AllowedCaller { Player }
    public interface IRocketCommand
    {
        void Execute(IRocketPlayer caller, string[] command);
        AllowedCaller AllowedCaller { get; }
        string Name { get; }
        string Help { get; }
        string Syntax { get; }
        List<string> Aliases { get; }
        List<string> Permissions { get; }
    }
    public sealed class RocketPlayer
    {
        public static readonly Dictionary<string, HashSet<string>> Permissions = new();
        private readonly string id;
        public RocketPlayer(string id) => this.id = id;
        public bool HasPermission(string permission) => Permissions.TryGetValue(id, out var tags) && tags.Contains(permission);
    }
}

namespace Steamworks
{
    public readonly record struct CSteamID(ulong m_SteamID);
}

namespace SDG.Unturned
{
    public sealed class Player
    {
        public UnityEngine.Transform transform { get; } = new();
        public int UnsafeTeleports;
        public int HomeTeleports;
        public bool HomeTeleportSucceeds = true;
        public bool ThrowOnHomeTeleport;
        public SteamChannel channel { get; } = new();
        public bool teleportToLocation(UnityEngine.Vector3 position, float rotation)
        {
            if (ThrowOnHomeTeleport) throw new InvalidOperationException("Simulated bed teleport failure");
            if (!HomeTeleportSucceeds) return false;
            HomeTeleports++;
            transform.position = position;
            return true;
        }
        public void teleportToLocationUnsafe(UnityEngine.Vector3 position, float rotation)
        {
            UnsafeTeleports++;
            transform.position = position;
        }
    }

    public static class PlayerTool
    {
        public static readonly Dictionary<CSteamID, Player> Players = new();
        public static Player getPlayer(CSteamID id) => Players.GetValueOrDefault(id);
    }
}

namespace UnityEngine
{
    public class MonoBehaviour
    {
        protected void InvokeRepeating(string method, float delay, float interval) { }
    }
    public sealed class Transform { public Vector3 position; }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static float Distance(Vector3 a, Vector3 b) =>
            MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
}

namespace Rocket.Unturned.Chat { public static class UnturnedChat { } }

namespace Rocket.Core.Utils
{
    public static class TaskDispatcher
    {
        public static readonly Queue<(Action action, float delay)> Pending = new();
        public static void QueueOnMainThread(Action action, float delay) => Pending.Enqueue((action, delay));
        public static void RunAll() { while (Pending.TryDequeue(out var queued)) queued.action(); }
    }
}

namespace Rocket.Unturned.Player
{
    public sealed class UnturnedPlayer : Rocket.API.IRocketPlayer
    {
        public static readonly Dictionary<CSteamID, UnturnedPlayer> Players = new();
        public CSteamID CSteamID { get; init; }
        public string Id => CSteamID.m_SteamID.ToString();
        public string DisplayName => Id;
        public string CharacterName => Id;
        public SDG.Unturned.Player Player { get; init; } = new();
        public bool Dead { get; set; }
        public bool IsInVehicle { get; set; }
        public SDG.Unturned.EPlayerStance Stance { get; set; }
        public UnityEngine.Vector3 Position => Player.transform.position;
        public float Rotation => 0;
        public int Teleports { get; private set; }
        public bool ThrowOnTeleport { get; set; }
        public static UnturnedPlayer FromCSteamID(CSteamID id) => Players.GetValueOrDefault(id);
        public static UnturnedPlayer FromName(string name) => Players.Values.FirstOrDefault(p =>
            string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        public bool HasPermission(string permission) => new Rocket.API.RocketPlayer(Id).HasPermission(permission);
        public void Teleport(UnturnedPlayer target)
        {
            if (ThrowOnTeleport) throw new InvalidOperationException("Simulated game teleport failure");
            Teleports++;
            Player.transform.position = target.Position;
        }
        public static UnturnedPlayer Create(ulong id)
        {
            var player = new UnturnedPlayer { CSteamID = new CSteamID(id) };
            Players[player.CSteamID] = player;
            SDG.Unturned.PlayerTool.Players[player.CSteamID] = player.Player;
            return player;
        }
    }
}

namespace RestoreMonarchy.Teleportation
{
    public sealed class ConfigurationHolder
    {
        public TeleportationConfiguration Instance { get; } = new();
        public ConfigurationHolder() => Instance.LoadDefaults();
    }
    public sealed class MovementDetector
    {
        public readonly Dictionary<SDG.Unturned.Player, Action> Players = new();
        public void AddPlayer(SDG.Unturned.Player player, Action moved) => Players[player] = moved;
        public void RemovePlayer(SDG.Unturned.Player player) => Players.Remove(player);
    }
    public sealed class TeleportationPlugin
    {
        public static TeleportationPlugin Instance { get; set; }
        public ConfigurationHolder Configuration { get; } = new();
        public readonly List<TPARequest> TPRequests = new();
        public readonly Dictionary<CSteamID, HomeTeleportRequest> HomeRequests = new();
        public readonly Dictionary<CSteamID, DateTime> Cooldowns = new();
        public readonly HashSet<CSteamID> Combat = new();
        public readonly HashSet<CSteamID> Raid = new();
        public readonly List<string> Messages = new();
        public readonly List<CSteamID> PendingNotifications = new();
        public readonly List<CSteamID> StateNotifications = new();
        public HomeTeleportRequest GetHomeUiRequest(CSteamID player) => HomeRequests.GetValueOrDefault(player);
        public void NotifyHomeRequestChanged(CSteamID player) => StateNotifications.Add(player);
        // The facade replaces only plugin wiring; the real HomeTeleportRequest owns validation and mutual exclusion.
        public bool TryStartHomeTeleport(UnturnedPlayer player, string name, double delay,
            Func<bool> validate, Func<bool> teleport, object owner) =>
            new HomeTeleportRequest(this, player, name, validate, teleport, owner).Execute(delay);
        public void NotifyPendingChanged(CSteamID target) => PendingNotifications.Add(target);
        public void NotifyRequestChanged(TPARequest request)
        {
            NotifyPendingChanged(request.Target);
            StateNotifications.Add(request.Sender);
            StateNotifications.Add(request.Target);
        }
        public MovementDetector MovementDetector { get; } = new();
        public bool IsPlayerInCombat(CSteamID id) => Combat.Contains(id);
        public bool IsPlayerInRaid(CSteamID id) => Raid.Contains(id);
        public bool IsPlayerInCave(UnturnedPlayer player) => false;
        public void SendMessageToPlayer(Rocket.API.IRocketPlayer player, string key, params object[] arguments) => Messages.Add(key);
    }
}
