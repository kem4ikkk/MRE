using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rocket.API
{
    public interface IRocketPluginConfiguration { void LoadDefaults(); }
}

namespace SDG.Unturned
{
    public static class SteamAdminlist
    {
        public static ulong ownerID = 123456;
        public static bool checkAdmin(ulong playerID) { return playerID == ownerID; }
    }
}

namespace DiagnosticsFixtures
{
    public sealed class GameplayData { public int Max_Group_Members = 4; }
    public sealed class ModeData { public GameplayData Gameplay = new GameplayData(); }
    public static class Provider
    {
        public static string mode = "EASY";
        public static string map = "PEI";
        public static byte maxPlayers = 24;
        public static ModeData modeConfigData = new ModeData();
    }
    public static class BarricadeManager
    {
        private static void ServerSetBedOwnerInternal(int a, int b, int c, int d, int e, int f) { }
        public static void Unrelated() { }
    }
    public static class PatchedTargets
    {
        public static void First() { }
        public static void Second() { }
        public static void Third() { }
        public static void Other() { }
    }
}

namespace HarmonyLib
{
    // Только тестовый двойник API. Не включается в плагин.
    public sealed class FakePatchInfo
    {
        public string[] Owners { get; set; }
    }
    public static class Harmony
    {
        public static IEnumerable<MethodBase> GetAllPatchedMethods()
        { return typeof(DiagnosticsFixtures.PatchedTargets).GetMethods(BindingFlags.Public | BindingFlags.Static); }
        public static FakePatchInfo GetPatchInfo(MethodBase method)
        {
            return new FakePatchInfo { Owners = new[] { method.Name == "Other" ? "someone.else" : "com.restoremonarchy.morehomes.test" } };
        }
    }
}
