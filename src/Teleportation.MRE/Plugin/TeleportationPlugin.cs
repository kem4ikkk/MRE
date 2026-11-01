using RestoreMonarchy.Teleportation.Components;
using RestoreMonarchy.Teleportation.Models;
using RestoreMonarchy.Teleportation.Utils;
using Rocket.API;
using Rocket.API.Collections;
using Rocket.Core.Plugins;
using Rocket.Core;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace RestoreMonarchy.Teleportation
{
    public class TeleportationPlugin : RocketPlugin<TeleportationConfiguration>
    {
        public static TeleportationPlugin Instance { get; private set; }
        public List<TPARequest> TPRequests { get; set; }
        public Dictionary<CSteamID, HomeTeleportRequest> HomeRequests { get; private set; }
        public Dictionary<CSteamID, Timer> CombatPlayers { get; set; }
        public Dictionary<CSteamID, Timer> RaidPlayers { get; set; }
        public Dictionary<CSteamID, DateTime> Cooldowns { get; set; }
        public Color MessageColor { get; set; }

        public MovementDetectorComponent MovementDetector { get; set; }
        private readonly PluginKeyEdges hotkeyEdges = new PluginKeyEdges();
        private DateTime nextRequestCleanupUtc;

        // Совместимость с прежними подписчиками входящих запросов.
        public event Action<CSteamID> PendingRequestsChanged;
        // Единый HUD обновляет состояние обеих сторон, а отсчёт читает из TPARequest.
        public event Action<CSteamID> RequestStateChanged;

        public void NotifyPendingChanged(CSteamID target)
        {
            var handlers = PendingRequestsChanged;
            if (handlers == null) return;
            foreach (Action<CSteamID> handler in handlers.GetInvocationList())
            {
                try { handler(target); }
                catch (Exception error) { Logger.LogException(error, "MRE TPA: ошибка подписчика PendingRequestsChanged"); }
            }
        }

        public void NotifyRequestChanged(TPARequest request)
        {
            NotifyPendingChanged(request.Target);
            NotifyRequestStateChanged(request.Sender);
            if (request.Target != request.Sender) NotifyRequestStateChanged(request.Target);
        }

        public void NotifyHomeRequestChanged(CSteamID player) => NotifyRequestStateChanged(player);

        public HomeTeleportRequest GetHomeUiRequest(CSteamID player)
        {
            HomeTeleportRequest request;
            return HomeRequests != null && HomeRequests.TryGetValue(player, out request) && !request.IsCanceled
                ? request : null;
        }

        public bool TryStartHomeTeleport(UnturnedPlayer player, string homeName, double delay,
            Func<bool> validateDestination, Func<bool> performTeleport, object owner)
        {
            if (HomeRequests == null || TPRequests == null || player == null || player.Player == null) return false;
            if (GetHomeUiRequest(player.CSteamID) != null || TPRequests.Any(x => !x.IsCanceled && x.IsExecuting &&
                (x.Sender == player.CSteamID || x.Target == player.CSteamID)))
            {
                SendMessageToPlayer(player, "HomeBusy");
                return false;
            }
            return new HomeTeleportRequest(this, player, homeName, validateDestination, performTeleport, owner).Execute(delay);
        }

        public void CancelHomeRequestsForOwner(object owner)
        {
            if (HomeRequests == null) return;
            foreach (var request in HomeRequests.Values.Where(x => ReferenceEquals(x.Owner, owner)).ToList()) request.Cancel();
        }

        private void NotifyRequestStateChanged(CSteamID player)
        {
            var handlers = RequestStateChanged;
            if (handlers == null) return;
            foreach (Action<CSteamID> handler in handlers.GetInvocationList())
            {
                try { handler(player); }
                catch (Exception error) { Logger.LogException(error, "MRE TPA: ошибка подписчика RequestStateChanged"); }
            }
        }

        protected override void Load()
        {
            Instance = this;
            MessageColor = UnturnedChat.GetColorFromName(Configuration.Instance.MessageColor, Color.green);

            TPRequests = new List<TPARequest>();
            HomeRequests = new Dictionary<CSteamID, HomeTeleportRequest>();
            CombatPlayers = new Dictionary<CSteamID, Timer>();
            RaidPlayers = new Dictionary<CSteamID, Timer>();
            Cooldowns = new Dictionary<CSteamID, DateTime>();

            MovementDetector = gameObject.AddComponent<MovementDetectorComponent>();

            U.Events.OnPlayerDisconnected += OnPlayerDisconnected;
            DamageTool.damagePlayerRequested += DamagePlayerRequested;
            UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDeath;
            BarricadeManager.onDamageBarricadeRequested += OnBarricadeDamaged;
            StructureManager.onDamageStructureRequested += OnStructureDamaged;
            PlayerInput.onPluginKeyTick += OnPluginKeyTick;
            if (Configuration.Instance.AcceptPluginKey >= ControlsSettings.NUM_PLUGIN_KEYS ||
                Configuration.Instance.DenyPluginKey >= ControlsSettings.NUM_PLUGIN_KEYS ||
                Configuration.Instance.AcceptPluginKey == Configuration.Instance.DenyPluginKey)
            {
                Configuration.Instance.EnablePluginHotkeys = false;
                Logger.LogWarning("MRE TPA: неверные Plugin Key. Горячие клавиши отключены; /tpa доступен.");
            }

            Logger.Log($"{Name} {Assembly.GetName().Version} has been loaded!", ConsoleColor.Yellow);
            Logger.Log("Check out more Unturned plugins at restoremonarchy.com");
        }

        protected override void Unload()
        {
            foreach (var request in HomeRequests.Values.ToList()) request.Cancel();
            foreach (var request in TPRequests.ToList())
                request.Cancel();

            foreach (var combatPlayer in CombatPlayers)
                combatPlayer.Value.Dispose();
            foreach (var raidPlayer in RaidPlayers)
                raidPlayer.Value.Dispose();

            TPRequests = null;
            HomeRequests = null;
            CombatPlayers = null;
            RaidPlayers = null;
            Cooldowns = null;

            Destroy(MovementDetector);

            U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            DamageTool.damagePlayerRequested -= DamagePlayerRequested;
            UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDeath;
            BarricadeManager.onDamageBarricadeRequested -= OnBarricadeDamaged;
            StructureManager.onDamageStructureRequested -= OnStructureDamaged;
            PlayerInput.onPluginKeyTick -= OnPluginKeyTick;
            hotkeyEdges.Clear();
            PendingRequestsChanged = null;
            RequestStateChanged = null;
            Instance = null;

            Logger.Log($"{Name} has been unloaded!", ConsoleColor.Yellow);
        }

        private void OnPlayerDisconnected(UnturnedPlayer player)
        {
            GetHomeUiRequest(player.CSteamID)?.Cancel();
            hotkeyEdges.Remove(player.CSteamID.m_SteamID);
            this.ClearPlayerRequests(player.CSteamID);
            this.StopPlayerCombat(player.CSteamID);
            this.StopPlayerRaid(player.CSteamID);
        }

        private void Update()
        {
            if (TPRequests == null || DateTime.UtcNow < nextRequestCleanupUtc) return;
            nextRequestCleanupUtc = DateTime.UtcNow.AddSeconds(1);
            foreach (var request in TPRequests.Where(x => x.IsExpired).ToList()) request.Cancel();
        }

        private void OnPluginKeyTick(Player player, uint simulation, byte key, bool state)
        {
            if (player == null || player.channel == null || player.channel.owner == null) return;
            CSteamID steamId = player.channel.owner.playerID.steamID;
            bool rising = hotkeyEdges.IsRising(steamId.m_SteamID, key, state);
            if (!rising || !Configuration.Instance.EnablePluginHotkeys) return;
            if (key != Configuration.Instance.AcceptPluginKey && key != Configuration.Instance.DenyPluginKey) return;
            var caller = UnturnedPlayer.FromCSteamID(steamId);
            if (caller == null || caller.Player != player || caller.Dead) return;
            // Выбор совпадает с HUD; Ю отменяет также уже принятый перенос у обеих сторон.
            this.HandlePluginUiAction(caller, key == Configuration.Instance.AcceptPluginKey);
        }

        private void DamagePlayerRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow)
        {
            var killerPlayer = PlayerTool.getPlayer(parameters.killer);
            if (!parameters.player.life.isDead && killerPlayer != null && killerPlayer != parameters.player && !Configuration.Instance.AllowCombat)
            {
                this.StartPlayerCombat(parameters.killer);
                this.StartPlayerCombat(parameters.player.channel.owner.playerID.steamID);
            }
        }

        private void OnStructureDamaged(CSteamID instigatorSteamID, Transform structureTransform, ref ushort pendingTotalDamage, ref bool shouldAllow, EDamageOrigin damageOrigin)
        {
            SteamPlayer steamPlayer;
            if (!Configuration.Instance.AllowRaid && (steamPlayer = PlayerTool.getSteamPlayer(instigatorSteamID)) != null)
            {
                StructureDrop drop = StructureManager.FindStructureByRootTransform(structureTransform);
                StructureData data = drop.GetServersideData();
                // return if structure owner is instigator
                if (RaidOwnershipRules.IsOwnerOrGroupMember(data.owner, data.group,
                    instigatorSteamID.m_SteamID, steamPlayer.player.quests.groupID.m_SteamID))
                {
                    return;
                }

                // return if structure owner is offline
                if (!Provider.clients.Exists(x => RaidOwnershipRules.IsOwnerOrGroupMember(data.owner, data.group,
                    x.playerID.steamID.m_SteamID, x.player.quests.groupID.m_SteamID)))
                {
                    return;
                }

                this.StartPlayerRaid(instigatorSteamID);
            }
        }

        private void OnBarricadeDamaged(CSteamID instigatorSteamID, Transform barricadeTransform, ref ushort pendingTotalDamage, ref bool shouldAllow, EDamageOrigin damageOrigin)
        {
            SteamPlayer steamPlayer;
            if (!Configuration.Instance.AllowRaid && (steamPlayer = PlayerTool.getSteamPlayer(instigatorSteamID)) != null)
            {
                BarricadeDrop drop = BarricadeManager.FindBarricadeByRootTransform(barricadeTransform);
                BarricadeData data = drop.GetServersideData();
                // return if barricade owner is instigator
                if (RaidOwnershipRules.IsOwnerOrGroupMember(data.owner, data.group,
                    instigatorSteamID.m_SteamID, steamPlayer.player.quests.groupID.m_SteamID))
                {
                    return;
                }

                // return if barricade owner is offline
                if (!Provider.clients.Exists(x => RaidOwnershipRules.IsOwnerOrGroupMember(data.owner, data.group,
                    x.playerID.steamID.m_SteamID, x.player.quests.groupID.m_SteamID)))
                {
                    return;
                }

                this.StartPlayerRaid(instigatorSteamID);
            }
        }

        private void OnPlayerDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer)
        {
            GetHomeUiRequest(player.CSteamID)?.Cancel();
            this.ClearPlayerRequests(player.CSteamID);
            this.StopPlayerCombat(player.CSteamID);
        }

        internal void SendMessageToPlayer(CSteamID steamID, string translationKey, params object[] placeholder)
        {
            UnturnedPlayer player = UnturnedPlayer.FromCSteamID(steamID);
            if (player == null || player.Player == null)
            {
                return;
            }

            SendMessageToPlayer(player, translationKey, placeholder);
        }

        internal void SendMessageToPlayer(IRocketPlayer player, string translationKey, params object[] placeholder)
        {
            if (player == null)
            {
                return;
            }

            string msg = Translate(translationKey, placeholder);
            msg = msg.Replace("[[", "<").Replace("]]", ">");
            if (translationKey != "TPAHelp")
            {
                string category = translationKey.StartsWith("Home") ? "HOME" : "TPA";
                msg = "<color=#B8D66B><b>MRE · " + category + "</b></color>  " + msg;
            }
            if (player is ConsolePlayer)
            {
                Logger.Log(msg);
                return;
            }

            UnturnedPlayer unturnedPlayer = (UnturnedPlayer)player;
            if (unturnedPlayer != null)
            {
                ChatManager.serverSendMessage(msg, MessageColor, null, unturnedPlayer.SteamPlayer(), EChatMode.SAY, Configuration.Instance.MessageIconUrl, true);
            }
        }

        public override TranslationList DefaultTranslations => new TranslationList()
        {
            // Basic notifications
            { "TargetNotFound", "Target not found" },
            { "CombatStart", "Combat mode activated" },
            { "CombatExpire", "Combat mode ended" },
            { "RaidStart", "Raid mode activated" },
            { "RaidExpire", "Raid mode ended" },

            // TPA Command help
            { "TPAHelp", "[[color=#B8D66B]][[b]]MRE · ТЕЛЕПОРТАЦИЯ[[/b]][[/color]]\n[[b]]/tpa имя[[/b]] — отправить запрос\n[[b]]/tpa a[[/b]] или [[b]]/tpa accept[[/b]] — принять\n[[b]]/tpa d[[/b]] или [[b]]/tpa deny[[/b]] — отклонить\n[[b]]/tpa cancel[[/b]] — отменить исходящий запрос" },

            // TPA Request messages
            { "TPACooldown", "Please wait [[b]]{0}[[/b]] seconds before sending another request" },
            { "TPADuplicate", "You already have a pending request to this player" },
            { "TPABusy", "A teleport is already in progress for one of the players" },
            { "HomeBusy", "A home or player teleport is already in progress" },
            { "HomeCanceled", "Home teleportation canceled" },
            { "TPASent", "TPA request sent to [[b]]{0}[[/b]]" },
            { "TPAReceive", "[[b]]{0}[[/b]] wants to teleport to you\nType [[b]]/tpa accept[[/b]] to allow" },

            // TPA Status messages
            { "TPANoRequest", "No pending TPA requests" },
            { "TPAAccepted", "Accepted [[b]]{0}'s[[/b]] TPA request" },
            { "TPADelay", "Teleporting to [[b]]{0}[[/b]] in [[b]]{1}[[/b]] seconds..." },

            // Combat/Raid related
            { "TPAWhileCombat", "Teleport failed - [[b]]{0}[[/b]] is in combat" },
            { "TPAWhileCombatYou", "Teleport failed - You are in combat" },
            { "TPAWhileRaid", "Teleport failed - [[b]]{0}[[/b]] is in raid mode" },
            { "TPAWhileRaidYou", "Teleport failed - You are in raid mode" },

            // Other restrictions
            { "TPADead", "Teleport failed - Player death detected" },
            { "TPACave", "Teleport failed - [[b]]{0}[[/b]] is in a cave" },
            { "TPACaveYou", "Teleport failed - You are in a cave" },
            { "TPAVehicle", "Teleport failed - [[b]]{0}[[/b]] is in a vehicle" },
            { "TPAVehicleYou", "Teleport failed - You are in a vehicle" },

            // Request management
            { "TPANoSentRequest", "You have no pending outgoing requests" },
            { "TPACanceled", "TPA request to [[b]]{0}[[/b]] canceled" },
            { "TPADenied", "Denied TPA request from [[b]]{0}[[/b]]" },

            // Movement related
            { "TPACanceledSenderMoved", "Teleport canceled - [[b]]{0}[[/b]] moved" },
            { "TPACanceledYouMoved", "Teleport canceled - You moved" },

            // Success message
            { "TPASuccess", "Successfully teleported to [[b]]{0}[[/b]]" },

            // Invalid requests
            { "TPAYourself", "You cannot send a TPA request to yourself" }
        };
    }
}
