using System;
using System.Collections.Generic;
using MreHelp;
using Steamworks;

namespace Rocket.Core.Logging
{
    public static class Logger
    {
        public static readonly List<string> Errors = new List<string>();
        public static void LogException(Exception error, string message) { Errors.Add(message + ": " + error.Message); }
    }
}

namespace Rocket.Core
{
    public static class R
    {
        public static readonly CommandsFacade Commands = new CommandsFacade();
        public static readonly PermissionsFacade Permissions = new PermissionsFacade();
    }

    public sealed class CommandsFacade
    {
        public object GetCommand(string name) { return String.IsNullOrEmpty(name) ? null : new object(); }
    }

    public sealed class PermissionsFacade
    {
        public bool HasPermission(Rocket.Unturned.Player.UnturnedPlayer player, List<string> permissions) { return true; }
    }
}

namespace Steamworks
{
    public struct CSteamID
    {
        public ulong m_SteamID;
        public CSteamID(ulong id) { m_SteamID = id; }
    }
}

namespace SDG.Unturned
{
    public enum EPluginWidgetFlags { Modal }
    public enum EDeathCause { SUICIDE }
    public enum ELimb { SPINE }

    public sealed class PlayerId
    {
        public SteamId steamID;
        public string characterName = "Игрок";
    }

    public sealed class SteamId
    {
        public ulong m_SteamID;
    }

    public sealed class PlayerOwner
    {
        public PlayerId playerID;
    }

    public sealed class PlayerChannel
    {
        public PlayerOwner owner;
        public object GetOwnerTransportConnection() { return owner; }
    }

    public sealed class Player
    {
        private readonly HashSet<EPluginWidgetFlags> flags = new HashSet<EPluginWidgetFlags>();
        public PlayerChannel channel;
        public PlayerQuests quests = new PlayerQuests();
        public bool isPluginWidgetFlagActive(EPluginWidgetFlags flag) { return flags.Contains(flag); }
        public void enablePluginWidgetFlag(EPluginWidgetFlags flag) { flags.Add(flag); }
        public void disablePluginWidgetFlag(EPluginWidgetFlags flag) { flags.Remove(flag); }
    }

    public sealed class PlayerLife
    {
        public PlayerChannel channel;
        public static event Action<PlayerLife, EDeathCause, ELimb, CSteamID> onPlayerDied;
        public static void RaiseDeath(PlayerLife life) { if (onPlayerDied != null) onPlayerDied(life, EDeathCause.SUICIDE, ELimb.SPINE, new CSteamID()); }
    }

    public static class Provider
    {
        public static string serverName = "MRE #1 | PEI | Выживание";
        public static byte maxPlayers = 24;
        public static readonly List<SteamPlayer> clients = new List<SteamPlayer>();
        public static event Action<CSteamID> onServerDisconnected;
        public static void RaiseDisconnect(CSteamID id) { if (onServerDisconnected != null) onServerDisconnected(id); }
    }

    public sealed class PlayerQuests { public CSteamID groupID; }
    public sealed class SteamPlayer { public Player player; }
    public sealed class GroupInfo { public string name; }
    public static class GroupManager
    {
        public static readonly Dictionary<ulong, GroupInfo> Groups = new Dictionary<ulong, GroupInfo>();
        public static GroupInfo getGroupInfo(CSteamID id) { GroupInfo group; return Groups.TryGetValue(id.m_SteamID, out group) ? group : null; }
    }
    public sealed class LevelInfo { public string name = "PEI"; }
    public static class Level { public static LevelInfo info = new LevelInfo(); }

    public sealed class EffectAsset
    {
        public ushort id;
    }

    public static class Assets
    {
        public static bool Missing;
        public static EffectAsset find(Guid guid) { return Missing ? null : new EffectAsset { id = HelpUi.EffectId }; }
    }

    public static class EffectManager
    {
        public static event Action<Player, string> onEffectButtonClicked;
        public static readonly Dictionary<string, bool> Visibility = new Dictionary<string, bool>();
        public static readonly Dictionary<string, string> Text = new Dictionary<string, string>();
        public static readonly Dictionary<object, Dictionary<string, string>> TextByConnection = new Dictionary<object, Dictionary<string, string>>();

        public static void ResetRenderState() { Visibility.Clear(); Text.Clear(); }
        public static void RaiseButton(Player player, string name) { if (onEffectButtonClicked != null) onEffectButtonClicked(player, name); }
        public static int Sends;
        public static int Clears;
        public static bool FailRender;
        public static void SendUIEffect(EffectAsset asset, short key, object connection, bool reliable) { Sends++; }
        public static void sendUIEffectVisibility(short key, object connection, bool reliable, string name, bool visible) { if (FailRender) throw new InvalidOperationException("fixture render failure"); Visibility[name] = visible; }
        public static void sendUIEffectText(short key, object connection, bool reliable, string name, string text)
        {
            if (FailRender) throw new InvalidOperationException("fixture render failure");
            Text[name] = text;
            Dictionary<string, string> values;
            if (!TextByConnection.TryGetValue(connection, out values)) TextByConnection[connection] = values = new Dictionary<string, string>();
            values[name] = text;
        }
        public static void ClearEffectByGuid(Guid guid, object connection) { Clears++; }
    }
}

namespace Rocket.Unturned.Player
{
    public sealed class UnturnedPlayer
    {
        public SDG.Unturned.Player Player { get; private set; }
        public CSteamID CSteamID { get; private set; }

        public UnturnedPlayer(SDG.Unturned.Player player, CSteamID id) { Player = player; CSteamID = id; }
        public static UnturnedPlayer FromPlayer(SDG.Unturned.Player player)
        {
            return new UnturnedPlayer(player, new CSteamID(player.channel.owner.playerID.steamID.m_SteamID));
        }
    }
}
