using RestoreMonarchy.Teleportation.Models;
using Rocket.Unturned.Player;
using Steamworks;

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
        public void teleportToLocationUnsafe(object position, float rotation) => UnsafeTeleports++;
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
        public static float Distance(Vector3 a, Vector3 b) =>
            MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
}

namespace Rocket.Unturned.Chat { public static class UnturnedChat { } }

namespace Rocket.Core.Utils
{
    public static class TaskDispatcher
    {
        public static readonly Queue<Action> Pending = new();
        public static void QueueOnMainThread(Action action, float delay) => Pending.Enqueue(action);
        public static void RunAll() { while (Pending.TryDequeue(out var action)) action(); }
    }
}

namespace Rocket.Unturned.Player
{
    public sealed class UnturnedPlayer
    {
        public static readonly Dictionary<CSteamID, UnturnedPlayer> Players = new();
        public CSteamID CSteamID { get; init; }
        public string Id => CSteamID.m_SteamID.ToString();
        public string DisplayName => Id;
        public string CharacterName => Id;
        public SDG.Unturned.Player Player { get; init; } = new();
        public bool Dead { get; set; }
        public bool IsInVehicle { get; set; }
        public object Position => new();
        public float Rotation => 0;
        public int Teleports { get; private set; }
        public static UnturnedPlayer FromCSteamID(CSteamID id) => Players.GetValueOrDefault(id);
        public void Teleport(UnturnedPlayer target) => Teleports++;
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
    public sealed class ConfigurationValues
    {
        public double TPADuration = 90;
        public double TPACooldown = 300;
        public double TPADelay = 15;
        public bool CancelOnMove = true;
        public bool UseUnsafeTeleport;
        public float MoveMaxDistance = 0.1f;
    }
    public sealed class ConfigurationHolder { public ConfigurationValues Instance { get; } = new(); }
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
        public readonly Dictionary<CSteamID, DateTime> Cooldowns = new();
        public readonly HashSet<CSteamID> Combat = new();
        public readonly HashSet<CSteamID> Raid = new();
        public readonly List<string> Messages = new();
        public MovementDetector MovementDetector { get; } = new();
        public bool IsPlayerInCombat(CSteamID id) => Combat.Contains(id);
        public bool IsPlayerInRaid(CSteamID id) => Raid.Contains(id);
        public bool IsPlayerInCave(UnturnedPlayer player) => false;
        public void SendMessageToPlayer(UnturnedPlayer player, string key, params object[] arguments) => Messages.Add(key);
    }
}
