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
    public class Player { public Channel channel; public Equipment equipment; public PlayerLook look = new PlayerLook(); public UnityEngine.Transform transform = new UnityEngine.Transform(); }
    public class PlayerLook { public UnityEngine.Transform aim = new UnityEngine.Transform { forward = new UnityEngine.Vector3 { z = 1 } }; }
    public static class RayMasks { public const int PLAYER=1, VIEWMODEL=2, IGNORE_RAYCAST=4, UI=8; }
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
    public struct Vector3
    {
        public float x, y, z;
        public float sqrMagnitude => x*x+y*y+z*z;
    }
    public class Transform { public Vector3 position, forward; }
    public struct RaycastHit { public Vector3 point; public Transform transform; }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public static class Physics
    {
        public static bool HitAvailable = true;
        public static RaycastHit NextHit = new RaycastHit { point = new Vector3 { x=12, z=34 }, transform = new Transform() };
        public static Vector3 LastOrigin, LastDirection;
        public static float LastDistance;
        public static int LastMask;
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, QueryTriggerInteraction triggers)
        {
            LastOrigin=origin; LastDirection=direction; LastDistance=maxDistance; LastMask=layerMask;
            hit=NextHit;
            return HitAvailable;
        }
    }
}
