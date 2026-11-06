using System;
namespace SDG.Unturned
{
    public enum EBuild { FORTIFICATION, BARRICADE, BARRICADE_WALL, DOOR, GATE, LADDER, SHUTTER, GLASS, HATCH, GENERATOR, STORAGE, FARM, FREEFORM }
    public class UseableHousingPlanner { }
    public class CustomHousingPlanner : UseableHousingPlanner { }
    public class UseableTool { }
    public class ItemAsset { public Type useableType; }
    public class ItemStructureAsset : ItemAsset { }
    public class ItemBarricadeAsset : ItemAsset { public EBuild build; }
    public class EffectAsset { public ushort id; }
    public class Player { public Channel channel; public Equipment equipment; public UnityEngine.Transform transform = new UnityEngine.Transform(); }
    public class Equipment { public ItemAsset asset; }
    public class Channel { public object Connection = new object(); public object GetOwnerTransportConnection() => Connection; }
    public struct SteamId { public ulong m_SteamID; }
    public class PlayerId { public SteamId steamID; }
    public class SteamPlayer { public Player player; public PlayerId playerID; }
    public static class Provider { public static System.Collections.Generic.List<SteamPlayer> clients = new System.Collections.Generic.List<SteamPlayer>(); }
    public static class Assets { public static object Current; public static object find(Guid guid) => Current; }
    public static class EffectManager
    {
        public static string Last;
        public static int Creates;
        public static void SendUIEffect(EffectAsset asset, short key, object connection, bool reliable) { Last = "create"; Creates++; }
        public static void sendUIEffectText(short key, object connection, bool reliable, string name, string text) { Last = "text:" + name; }
        public static void sendUIEffectVisibility(short key, object connection, bool reliable, string name, bool visible) { Last = "visible:" + name; }
        public static void ClearEffectByGuid(Guid guid, object connection) { Last = "clear"; }
    }
}
namespace UnityEngine
{
    public struct Vector3 { public float x, z; }
    public class Transform { public Vector3 position; }
}
