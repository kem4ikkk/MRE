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
        public static event Action<CSteamID> onServerDisconnected;
        public static void RaiseDisconnect(CSteamID id) { if (onServerDisconnected != null) onServerDisconnected(id); }
    }

    public sealed class EffectAsset
    {
        public ushort id;
    }

    public static class Assets
    {
        public static EffectAsset find(Guid guid) { return new EffectAsset { id = HelpUi.EffectId }; }
    }

    public static class EffectManager
    {
        public static event Action<Player, string> onEffectButtonClicked;
        public static readonly Dictionary<string, bool> Visibility = new Dictionary<string, bool>();
        public static readonly Dictionary<string, string> Text = new Dictionary<string, string>();

        public static void ResetRenderState() { Visibility.Clear(); Text.Clear(); }
        public static void RaiseButton(Player player, string name) { if (onEffectButtonClicked != null) onEffectButtonClicked(player, name); }
        public static void SendUIEffect(EffectAsset asset, short key, object connection, bool reliable) { }
        public static void sendUIEffectVisibility(short key, object connection, bool reliable, string name, bool visible) { Visibility[name] = visible; }
        public static void sendUIEffectText(short key, object connection, bool reliable, string name, string text) { Text[name] = text; }
        public static void ClearEffectByGuid(Guid guid, object connection) { }
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
