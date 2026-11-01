using RestoreMonarchy.MoreHomes.Models;
using Rocket.API;
using Steamworks;

namespace Newtonsoft.Json
{
    public sealed class JsonIgnoreAttribute : Attribute { }
}

namespace SDG.Unturned
{
    public enum EPlayerStance { STAND, DRIVING }
    public sealed class SteamChannel { public SteamPlayer owner { get; } = new(); }
    public sealed class SteamPlayer { public SteamPlayerID playerID { get; } = new(); }
    public sealed class SteamPlayerID { public CSteamID steamID; }
    public sealed class InteractableBed
    {
        public UnityEngine.Transform transform { get; } = new();
        public bool isActiveAndEnabled = true;
        public CSteamID owner;
    }
    public static class LevelGround
    {
        public static float Height;
        public static float getHeight(UnityEngine.Vector3 position) => Height;
    }
    public sealed class BarricadeRegion { }
    public sealed class BarricadeDrop { }
    public static class BarricadeManager
    {
        public static bool tryGetRegion(UnityEngine.Transform transform, out byte x, out byte y,
            out ushort plant, out BarricadeRegion region)
        { x=0; y=0; plant=0; region=null; return false; }
        public static void ServerUnclaimBed(InteractableBed bed) { }
        public static BarricadeDrop FindBarricadeByRootTransform(UnityEngine.Transform transform) => null;
        public static bool tryGetRegion(UnityEngine.Transform transform, out byte x, out byte y, out ushort plant)
        { x=0; y=0; plant=0; return false; }
        public static void destroyBarricade(BarricadeDrop drop, byte x, byte y, ushort plant) { }
    }
}

namespace RestoreMonarchy.MoreHomes.Helpers
{
    internal static class ReflectionHelper
    {
        internal static void ServerSetBedOwnerInternal(SDG.Unturned.InteractableBed bed,
            byte x, byte y, ushort plant, SDG.Unturned.BarricadeRegion region, CSteamID owner) { }
    }
}

namespace RestoreMonarchy.MoreHomes
{
    public sealed class TestConfigurationHolder
    {
        public MoreHomesConfiguration Instance { get; set; } = new();
        public TestConfigurationHolder() => Instance.LoadDefaults();
    }
    public sealed class TestDataService
    {
        public List<PlayerData> PlayersData { get; } = new();
    }
    public sealed class MoreHomesPlugin
    {
        public static MoreHomesPlugin Instance { get; set; }
        public TestConfigurationHolder Configuration { get; } = new();
        public TestDataService DataService { get; } = new();
        public Dictionary<string,DateTime> PlayerCooldowns { get; } = new();
        public List<string> Messages { get; } = new();
        public void SendMessageToPlayer(IRocketPlayer player, string key, params object[] args) => Messages.Add(key);
    }
}
