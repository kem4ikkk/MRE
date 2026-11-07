using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MreAdminTools;
using MreInterface;
using MreFlight;
using MreModeration;
using MreInventoryAudit;
using MreAirdrop;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreAdminMenu
{
    public sealed class MreAdminMenuPlugin : RocketPlugin
    {
        private const short EffectKey = 14826;
        private const ushort EffectId = 48626;
        private static readonly Guid EffectGuid = new Guid("bc15863fb9c24a69a36bb733456cc3b2");
        private sealed class Session
        {
            internal Player Actor;
            internal ulong TargetId;
            internal ushort ItemId = 1;
            internal byte Amount = 1;
            internal ushort VehicleId = 1;
            internal string AnnouncementText = String.Empty;
            internal string RestartMinutes = "10";
            internal string HorizontalSpeed = "1";
            internal string VerticalSpeed = "1";
            internal string StaffQuery = String.Empty;
            internal string StaffReason = String.Empty;
            internal string StaffDuration = "perm";
            internal string StaffRecordId = String.Empty;
            internal string StaffView;
            internal string SnapshotId = String.Empty;
            internal string PendingOperationKey;
            internal string PendingOperationId;
            internal string PendingStaffButton;
            internal string PendingStaffOperationId;
            internal string PendingDescription;
            internal string RestoreToken;
            internal string LastPanelResult = String.Empty;
            internal InventoryView[] Deaths = new InventoryView[0];
            internal InventoryItemView[] InventoryItems = new InventoryItemView[0];
            internal InventoryView SnapshotDetails;
            internal ulong InventoryTargetId;
            internal string InventoryMode;
            internal int Page;
            internal int Tab;
            internal int StaffOffset;
            internal int StaffTotal;
            internal int InventoryOffset;
            internal int DeathsOffset;
            internal string PendingToken;
            internal string PendingAction;
            internal DateTime PendingUntil;
            internal ulong PendingTargetId;
            internal Player PendingTargetPlayer;
            internal bool OwnsModal;
            internal readonly ulong[] ListedPlayers = new ulong[6];
        }

        private readonly Dictionary<ulong, Session> sessions = new Dictionary<ulong, Session>();
        private readonly Dictionary<ulong, Player> launchers = new Dictionary<ulong, Player>();
        private MreInterfacePlugin api;
        private EffectAsset effect;
        private int gameThread;
        private DateTime nextStartAttemptUtc;
        private DateTime nextPermissionAuditUtc;
        private bool attached;

        protected override void Load()
        {
            gameThread = Thread.CurrentThread.ManagedThreadId;
            nextStartAttemptUtc = DateTime.MinValue;
        }

        private void Update()
        {
            var currentApi = MreInterfacePlugin.Instance;
            if (attached && !ReferenceEquals(api, currentApi)) Detach();
            if (attached)
            {
                if (DateTime.UtcNow >= nextPermissionAuditUtc)
                {
                    nextPermissionAuditUtc = DateTime.UtcNow.AddSeconds(1);
                    AuditOnlinePermissions();
                }
                AuditPendingConfirmations();
                return;
            }
            if (DateTime.UtcNow < nextStartAttemptUtc || !Level.isLoaded) return;
            nextStartAttemptUtc = DateTime.UtcNow.AddSeconds(10);
            try
            {
                var candidateApi = MreInterfacePlugin.Instance;
                if (candidateApi == null) return;
                var candidateEffect = Assets.find(EffectGuid) as EffectAsset;
                if (candidateEffect == null || candidateEffect.id != EffectId) throw new InvalidOperationException("EffectAsset админ-панели не найден по GUID и ID.");
                api = candidateApi; effect = candidateEffect;
                api.AdminMenuRequested += OpenRequested;
                EffectManager.onEffectButtonClicked += Button;
                EffectManager.onEffectTextCommitted += TextCommitted;
                Provider.onServerDisconnected += Disconnected;
                Provider.onServerConnected += Connected;
                foreach (var client in Provider.clients) if (client != null && client.player != null) ShowLauncher(UnturnedPlayer.FromPlayer(client.player));
                Logger.Log("[MRE ADMIN UI] Панель готова; кнопка показывается администраторам, /admin доступен как резерв.");
                attached = true;
            }
            catch (Exception error)
            {
                effect = null;
                Logger.LogException(error, "[MRE ADMIN UI] Панель не включена.");
            }
        }

        protected override void Unload()
        {
            Detach();
        }

        private void Detach()
        {
            if (api != null) api.AdminMenuRequested -= OpenRequested;
            EffectManager.onEffectButtonClicked -= Button;
            EffectManager.onEffectTextCommitted -= TextCommitted;
            Provider.onServerDisconnected -= Disconnected;
            Provider.onServerConnected -= Connected;
            foreach (var pair in sessions.ToArray())
            {
                Session session = pair.Value;
                try { if (session.Actor != null && session.Actor.channel != null) EffectManager.ClearEffectByGuid(EffectGuid, session.Actor.channel.GetOwnerTransportConnection()); }
                catch (Exception error) { Logger.LogException(error, "[MRE ADMIN UI] Не удалось убрать панель при смене MreInterface."); }
                try { if (session.OwnsModal && session.Actor != null) session.Actor.disablePluginWidgetFlag(EPluginWidgetFlags.Modal); }
                catch (Exception error) { Logger.LogException(error, "[MRE ADMIN UI] Не удалось освободить Modal при смене MreInterface."); }
            }
            foreach (var pair in launchers.ToArray()) HideEffect(pair.Value);
            sessions.Clear(); launchers.Clear(); api = null; effect = null; attached = false;
        }

        private bool OpenRequested(UnturnedPlayer actor)
        {
            if (Thread.CurrentThread.ManagedThreadId != gameThread || actor == null || actor.Player == null || api == null || effect == null) return false;
            if (!CanOpenPanel(actor)) return false;
            ulong id = actor.CSteamID.m_SteamID;
            launchers.Remove(id);
            Session prior;
            if (sessions.TryGetValue(id, out prior)) { Render(actor.Player, prior); return true; }
            if (actor.Player.isPluginWidgetFlagActive(EPluginWidgetFlags.Modal)) return false;
            var session = new Session { Actor = actor.Player, OwnsModal = true };
            sessions.Add(id, session);
            try
            {
                actor.Player.enablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                EffectManager.SendUIEffect(effect, EffectKey, actor.Player.channel.GetOwnerTransportConnection(), true);
                Render(actor.Player, session);
                return true;
            }
            catch (Exception error) { Logger.LogException(error, "[MRE ADMIN UI] Не удалось открыть панель."); Close(id); return false; }
        }

        private void Disconnected(CSteamID steamId)
        {
            sessions.Remove(steamId.m_SteamID);
            launchers.Remove(steamId.m_SteamID);
        }
        private void Connected(CSteamID steamId)
        {
            var client = Provider.clients.FirstOrDefault(c => c != null && c.player != null && c.playerID.steamID == steamId);
            if (client != null) ShowLauncher(UnturnedPlayer.FromPlayer(client.player));
        }

        private void ShowLauncher(UnturnedPlayer actor)
        {
            if (actor == null || actor.Player == null || api == null || effect == null || !CanOpenPanel(actor)) return;
            ulong id = actor.CSteamID.m_SteamID;
            if (launchers.ContainsKey(id)) return;
            try
            {
                launchers[id] = actor.Player;
                EffectManager.SendUIEffect(effect, EffectKey, actor.Player.channel.GetOwnerTransportConnection(), true);
                EffectManager.sendUIEffectVisibility(EffectKey, actor.Player.channel.GetOwnerTransportConnection(), true, "AdminContent", false);
                EffectManager.sendUIEffectVisibility(EffectKey, actor.Player.channel.GetOwnerTransportConnection(), true, "AdminLauncher", true);
            }
            catch (Exception error) { launchers.Remove(id); Logger.LogException(error, "[MRE ADMIN UI] Не удалось показать кнопку панели."); }
        }

        private void AuditOnlinePermissions()
        {
            foreach (var pair in sessions.ToArray())
            {
                var client = Provider.clients.FirstOrDefault(c => c != null && c.player != null && ReferenceEquals(c.player, pair.Value.Actor) && c.playerID.steamID.m_SteamID == pair.Key);
                if (client == null || !CanOpenPanel(UnturnedPlayer.FromPlayer(client.player))) Close(pair.Key);
            }
            foreach (var pair in launchers.ToArray())
            {
                var client = Provider.clients.FirstOrDefault(c => c != null && c.player != null && ReferenceEquals(c.player, pair.Value) && c.playerID.steamID.m_SteamID == pair.Key);
                if (client == null || !CanOpenPanel(UnturnedPlayer.FromPlayer(client.player)))
                {
                    HideEffect(pair.Value);
                    launchers.Remove(pair.Key);
                }
            }
            foreach (var client in Provider.clients)
            {
                if (client == null || client.player == null) continue;
                ulong id = client.playerID.steamID.m_SteamID;
                if (!sessions.ContainsKey(id) && !launchers.ContainsKey(id)) ShowLauncher(UnturnedPlayer.FromPlayer(client.player));
            }
        }

        private void AuditPendingConfirmations()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var pair in sessions.ToArray())
            {
                Session session = pair.Value;
                if (String.IsNullOrEmpty(session.PendingAction) || now <= session.PendingUntil) continue;
                ClearPending(session);
                if (session.Actor != null && session.Actor.channel != null)
                {
                    SetStatus(session.Actor, "Подтверждение истекло. Запросите его заново.");
                    Render(session.Actor, session);
                }
            }
        }

        private void Close(ulong id)
        {
            Session s; if (!sessions.TryGetValue(id, out s)) return;
            sessions.Remove(id);
            try { if (s.Actor != null && s.Actor.channel != null) EffectManager.ClearEffectByGuid(EffectGuid, s.Actor.channel.GetOwnerTransportConnection()); }
            catch (Exception error) { Logger.LogException(error, "[MRE ADMIN UI] Не удалось закрыть эффект."); }
            try { if (s.OwnsModal && s.Actor != null) s.Actor.disablePluginWidgetFlag(EPluginWidgetFlags.Modal); }
            catch (Exception error) { Logger.LogException(error, "[MRE ADMIN UI] Не удалось освободить modal."); }
            if (s.Actor != null && s.Actor.channel != null) ShowLauncher(UnturnedPlayer.FromPlayer(s.Actor));
        }

        private UnturnedPlayer ResolveActor(Player callbackPlayer, out Session session)
        {
            session = null;
            if (Thread.CurrentThread.ManagedThreadId != gameThread || callbackPlayer == null || callbackPlayer.channel == null || callbackPlayer.channel.owner == null) return null;
            ulong id = callbackPlayer.channel.owner.playerID.steamID.m_SteamID;
            if (!sessions.TryGetValue(id, out session) || !ReferenceEquals(session.Actor, callbackPlayer)) return null;
            var online = Provider.clients.FirstOrDefault(c => c != null && ReferenceEquals(c.player, callbackPlayer) && c.playerID.steamID.m_SteamID == id);
            if (online == null) return null;
            return UnturnedPlayer.FromPlayer(callbackPlayer);
        }

        private bool Allowed(UnturnedPlayer actor, string action)
        {
            return api != null && api.CanExecuteAdmin(actor, action);
        }

        private bool ToolAllowed(UnturnedPlayer actor, string permission)
        {
            return api != null && api.HasAdminPermission(actor, "mre.admin") && api.HasAdminPermission(actor, "mre.admin." + permission);
        }

        private bool CanOpenPanel(UnturnedPlayer actor)
        {
            return api != null && (api.HasAdminPermission(actor,"mre.admin") || api.HasAdminPermission(actor,"mre.staff"));
        }

        private void Button(Player player, string name)
        {
            if (name == "AdminLauncher")
            {
                if (player == null || player.channel == null || player.channel.owner == null) return;
                var online = Provider.clients.FirstOrDefault(c => c != null && ReferenceEquals(c.player, player));
                if (online == null) return;
                var launcherActor = UnturnedPlayer.FromPlayer(player);
                if (!CanOpenPanel(launcherActor)) { HideEffect(player); launchers.Remove(online.playerID.steamID.m_SteamID); return; }
                launchers.Remove(online.playerID.steamID.m_SteamID);
                OpenRequested(launcherActor); return;
            }
            Session s; var actor = ResolveActor(player, out s); if (actor == null || String.IsNullOrEmpty(name)) return;
            try
            {
                if (name == "AdminClose") { Close(actor.CSteamID.m_SteamID); return; }
                if (name.StartsWith("AdminTab", StringComparison.Ordinal))
                {
                    int tab; if (Int32.TryParse(name.Substring(8), out tab) && tab >= 0 && tab <= 5) { s.Tab = tab; ClearPending(s); Render(player, s); }
                    return;
                }
                if (name == "AdminPlayerPrev" || name == "AdminPlayerNext")
                {
                    int pages = Math.Max(1, (OnlinePlayers().Length + 5) / 6);
                    s.Page = name == "AdminPlayerPrev" ? (s.Page + pages - 1) % pages : (s.Page + 1) % pages;
                    Render(player, s); return;
                }
                if (name == "AdminStaffPrev" || name == "AdminStaffNext")
                {
                    const int pageSize = 3;
                    int last = s.StaffTotal == 0 ? 0 : ((s.StaffTotal - 1) / pageSize) * pageSize;
                    s.StaffOffset = name == "AdminStaffPrev" ? Math.Max(0, s.StaffOffset - pageSize) : Math.Min(last, s.StaffOffset + pageSize);
                    if (!String.IsNullOrEmpty(s.StaffView)) ExecuteStaff(actor,s,s.StaffView);
                    Render(player, s); return;
                }
                if (name == "AdminInventoryPrev" || name == "AdminInventoryNext")
                {
                    if (String.IsNullOrEmpty(s.InventoryMode)) { SetStatus(player, "Сначала запросите инвентарь или список снимков."); Render(player,s); return; }
                    const int pageSize = 4;
                    int count = s.InventoryMode == "inspect" ? (s.InventoryItems == null ? 0 : s.InventoryItems.Length) :
                        s.InventoryMode == "details" ? (s.SnapshotDetails == null || s.SnapshotDetails.Items == null ? 0 : s.SnapshotDetails.Items.Length) :
                        (s.Deaths == null ? 0 : s.Deaths.Length);
                    int last = count == 0 ? 0 : ((count - 1) / pageSize) * pageSize;
                    if (s.InventoryMode == "inspect" || s.InventoryMode == "details") s.InventoryOffset = name == "AdminInventoryPrev" ? Math.Max(0, s.InventoryOffset - pageSize) : Math.Min(last, s.InventoryOffset + pageSize);
                    else s.DeathsOffset = name == "AdminInventoryPrev" ? Math.Max(0, s.DeathsOffset - pageSize) : Math.Min(last, s.DeathsOffset + pageSize);
                    ExecuteInventory(actor, s, s.InventoryMode == "inspect" ? "AdminInventoryInspect" : s.InventoryMode == "details" ? "AdminInventoryDetails" : "AdminInventoryDeaths");
                    Render(player, s); return;
                }
                if (name.StartsWith("AdminPlayer", StringComparison.Ordinal))
                {
                    int slot; if (Int32.TryParse(name.Substring(11), out slot) && slot >= 0 && slot < 6)
                    {
                        ulong selected = s.ListedPlayers[slot];
                        if (selected != 0 && Provider.clients.Any(c => c != null && c.player != null && c.playerID.steamID.m_SteamID == selected))
                        { s.TargetId = selected; s.StaffQuery = String.Empty; InvalidateInventory(s); ClearPending(s); }
                    }
                    Render(player, s); return;
                }
                if (HandleFields(player, s, name)) { Render(player, s); return; }
                if (name == "AdminConfirm") { Confirm(actor, s); Render(player, s); return; }
                if (name == "AdminCancelConfirm") { ClearPending(s); Render(player, s); return; }
                Execute(actor, s, name);
                Render(player, s);
            }
            catch (Exception error) { Logger.LogException(error, "[MRE ADMIN UI] Ошибка действия панели."); s.PendingToken = null; SetStatus(player, "Ошибка операции. Проверьте серверный журнал."); }
        }

        private void TextCommitted(Player player, string fieldName, string text)
        {
            Session session;
            var actor = ResolveActor(player, out session);
            if (actor == null || String.IsNullOrEmpty(fieldName)) return;
            string value = text ?? String.Empty;
            if (fieldName == "AdminAnnouncementText") session.AnnouncementText = value;
            else if (fieldName == "AdminRestartMinutes") session.RestartMinutes = value;
            else if (fieldName == "AdminHorizontalSpeed") session.HorizontalSpeed = value;
            else if (fieldName == "AdminVerticalSpeed") session.VerticalSpeed = value;
            else if (fieldName == "AdminStaffQuery")
            {
                string query = value.Trim();
                if (session.StaffQuery != query) { session.StaffQuery = query; session.StaffOffset = 0; session.StaffTotal = 0; InvalidateInventory(session); ClearPending(session); }
            }
            else if (fieldName == "AdminStaffReason") { if (session.StaffReason != value) ClearPending(session); session.StaffReason = value; }
            else if (fieldName == "AdminStaffDuration") { if (session.StaffDuration != value.Trim()) ClearPending(session); session.StaffDuration = value.Trim(); }
            else if (fieldName == "AdminStaffRecordId") { if (session.StaffRecordId != value.Trim()) ClearPending(session); session.StaffRecordId = value.Trim(); }
            else if (fieldName == "AdminSnapshotId")
            {
                string snapshotId = value.Trim();
                if (session.SnapshotId != snapshotId) { session.SnapshotId = snapshotId; session.SnapshotDetails = null; session.InventoryOffset = 0; ClearPending(session); }
            }
            else return;
            SetStatus(player, "Ввод принят. Для выполнения нажмите соответствующую кнопку.");
            Render(player, session);
        }

        private static bool HandleFields(Player player, Session s, string name)
        {
            int delta = name.EndsWith("Down10", StringComparison.Ordinal) ? -10 : name.EndsWith("Up10", StringComparison.Ordinal) ? 10 : name.EndsWith("Down", StringComparison.Ordinal) ? -1 : name.EndsWith("Up", StringComparison.Ordinal) ? 1 : 0;
            if (delta == 0) return false;
            if (name.StartsWith("AdminItem", StringComparison.Ordinal)) s.ItemId = (ushort)Math.Max(1, Math.Min(UInt16.MaxValue, s.ItemId + delta));
            else if (name.StartsWith("AdminAmount", StringComparison.Ordinal)) s.Amount = (byte)Math.Max(1, Math.Min(255, s.Amount + delta));
            else if (name.StartsWith("AdminVehicle", StringComparison.Ordinal)) s.VehicleId = (ushort)Math.Max(1, Math.Min(UInt16.MaxValue, s.VehicleId + delta));
            else return false;
            return true;
        }

        private static void InvalidateInventory(Session s)
        {
            s.InventoryTargetId = 0; s.InventoryMode = null; s.InventoryItems = new InventoryItemView[0];
            s.Deaths = new InventoryView[0]; s.SnapshotDetails = null; s.InventoryOffset = 0; s.DeathsOffset = 0;
            s.SnapshotId = String.Empty; s.PendingStaffButton = null; s.PendingStaffOperationId = null;
            ClearPending(s);
        }

        private void Execute(UnturnedPlayer actor, Session s, string action)
        {
            string result;
            ulong target = s.TargetId;
            ClearPending(s);
            if (action == "AdminAnnounceEvent")
            {
                SetStatus(actor.Player, api.PublishAdminAnnouncement(actor, "event", s.AnnouncementText)); return;
            }
            if (action == "AdminAnnounceRestart")
            {
                SetStatus(actor.Player, api.PublishAdminAnnouncement(actor, "restart", s.RestartMinutes)); return;
            }
            if (action == "AdminFlightSpeed")
            {
                if (!Allowed(actor, "fly")) { SetStatus(actor.Player, "Нет права mre.admin.fly."); return; }
                float horizontal, vertical;
                if (!Single.TryParse(s.HorizontalSpeed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out horizontal) ||
                    !Single.TryParse(s.VerticalSpeed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vertical))
                { SetStatus(actor.Player, "Введите числовую скорость с точкой: горизонтальная 0.1–10, вертикальная 0.1–5."); return; }
                var flight = MreFlightPlugin.Instance;
                if (flight == null) { SetStatus(actor.Player, "MreFlight не загружен."); return; }
                try { SetStatus(actor.Player, flight.SetSpeed(actor, horizontal, vertical)); }
                catch (ArgumentOutOfRangeException error) { SetStatus(actor.Player, error.Message); }
                return;
            }
            if (action == "AdminHealSelf")
            {
                SetStatus(actor.Player, Allowed(actor, "heal") ? api.ExecuteAdmin(actor, "heal", 0) : "Нет права на лечение."); return;
            }
            if (action == "AdminGive")
            {
                if (!ToolAllowed(actor, "give")) { SetStatus(actor.Player, "Нет права на выдачу предметов."); return; }
                if (!ValidTarget(target, actor)) { SetStatus(actor.Player, "Сначала выберите другого подключённого игрока."); return; }
                var tools = MreAdminToolsPlugin.Instance;
                SetStatus(actor.Player, tools == null ? "Сервис выдачи предметов не загружен." : tools.GiveItem(actor, target, s.ItemId, s.Amount)); return;
            }
            if (action == "AdminGiveSelf")
            {
                if (!ToolAllowed(actor, "give")) { SetStatus(actor.Player, "Нет права на выдачу предметов."); return; }
                var tools = MreAdminToolsPlugin.Instance;
                SetStatus(actor.Player, tools == null ? "Сервис выдачи предметов не загружен." : tools.GiveItem(actor, actor.CSteamID.m_SteamID, s.ItemId, s.Amount)); return;
            }
            if (action == "AdminVehicle")
            {
                if (!ToolAllowed(actor, "veh")) { SetStatus(actor.Player, "Нет права на вызов транспорта."); return; }
                var tools = MreAdminToolsPlugin.Instance;
                SetStatus(actor.Player, tools == null ? "Сервис транспорта не загружен." : tools.SpawnVehicle(actor, s.VehicleId)); return;
            }
            if (action == "AdminStopSpectate") { SetStatus(actor.Player, api.StopObservation(actor, false)); return; }
            if (action.StartsWith("AdminStaff", StringComparison.Ordinal)) { ExecuteStaff(actor, s, action); return; }
            if (action.StartsWith("AdminInventory", StringComparison.Ordinal) || action == "AdminRestorePrepare") { ExecuteInventory(actor, s, action); return; }
            if (action == "AdminAirdropLaunch" || action == "AdminAirdropStatus")
            {
                var drop = MreAirdropPlugin.Instance;
                if (drop == null) { SetStatus(actor.Player, "MreAirdrop не загружен."); return; }
                try { SetStatus(actor.Player, action == "AdminAirdropLaunch" ? drop.LaunchManual(actor) : drop.GetStatus(actor)); }
                catch (Exception error) { SetStatus(actor.Player, Safe(error.Message)); }
                return;
            }

            string policy = PolicyAction(action);
            if (policy == null || !Allowed(actor, policy)) { SetStatus(actor.Player, "Нет права на это действие."); return; }
            if (NeedsTarget(policy) && !ValidTarget(target, actor)) { SetStatus(actor.Player, "Выберите другого подключённого игрока."); return; }
            if (policy == "remove")
            {
                var preview = api.PreviewRemoval(actor);
                if (preview == null) { SetStatus(actor.Player, "Наведи прицел на наземную постройку в пределах 6 метров."); return; }
                s.PendingToken = preview.Token; s.PendingAction = "remove"; s.PendingUntil = preview.ExpiresUtc;
                s.PendingDescription = "Удалить " + preview.Kind + " №" + preview.AssetId + " (объект " + preview.InstanceId + ")? Удаление необратимо; содержимое контейнера может выпасть. Подтверждение действует 15 секунд.";
                SetStatus(actor.Player, s.PendingDescription); return;
            }
            if (policy == "kill")
            {
                var preview = api.PreviewKill(actor, target);
                if (preview == null) { SetStatus(actor.Player, "Нужен другой живой подключённый игрок."); return; }
                s.PendingToken = preview.Token; s.PendingAction = "kill"; s.PendingTargetId = preview.TargetId; s.PendingTargetPlayer = FindPlayer(preview.TargetId); s.PendingUntil = preview.ExpiresUtc;
                s.PendingDescription = "Убить игрока " + Safe(preview.TargetName) + " [" + preview.TargetId + "]? Подтверждение действует 15 секунд.";
                SetStatus(actor.Player, s.PendingDescription); return;
            }
            if (policy == "kick")
            {
                var selected = Provider.clients.FirstOrDefault(c => c != null && c.player != null && c.playerID.steamID.m_SteamID == target);
                if (selected == null) { SetStatus(actor.Player, "Игрок вышел; выберите цель заново."); return; }
                s.PendingAction = "kick"; s.PendingTargetId = target; s.PendingTargetPlayer = selected.player; s.PendingUntil = DateTime.UtcNow.AddSeconds(15);
                s.PendingDescription = "Исключить " + Safe(selected.playerID.characterName) + " [" + target + "]? Действие отменится, если эта игровая сессия завершится. Подтверждение действует 15 секунд.";
                SetStatus(actor.Player, s.PendingDescription); return;
            }
            if (policy == "spectate") result = api.StartObservation(actor, target, false);
            else if (policy == "fly")
            {
                var flight = MreFlightPlugin.Instance;
                result = flight == null ? "Модуль полёта тела MreFlight не загружен." : flight.SetFlight(actor, !flight.IsFlying(actor));
            }
            else if (policy == "freecam") result = api.SetFreecamPermission(actor, !actor.Player.look.canUseFreecam);
            else if (policy == "god" || policy == "vanish") result = api.SetStaffMode(actor, policy, !CurrentMode(actor, policy));
            else if (policy == "workzone") result = api.SetWorkzonePermission(actor, !actor.Player.look.canUseWorkzone);
            else if (policy == "stats") result = api.SetStatsPermission(actor, !actor.Player.look.canUseSpecStats);
            else if (policy == "door") result = api.ToggleAimedDoor(actor);
            else if (policy == "storage") result = api.OpenAimedStorage(actor);
            else result = api.ExecuteAdmin(actor, policy, NeedsTarget(policy) ? target : 0);
            SetStatus(actor.Player, result);
        }

        private void Confirm(UnturnedPlayer actor, Session s)
        {
            if (String.IsNullOrEmpty(s.PendingAction) || DateTime.UtcNow > s.PendingUntil) { ClearPending(s); SetStatus(actor.Player, "Подтверждение истекло. Запросите его заново."); return; }
            string action = s.PendingAction, token = s.PendingToken;
            ulong target = s.PendingTargetId;
            Player targetSession = s.PendingTargetPlayer;
            string staffButton = s.PendingStaffButton, staffOperationId = s.PendingStaffOperationId;
            string staffReason = s.StaffReason, staffDuration = s.StaffDuration;
            ClearPending(s);
            if (action == "staff")
            {
                var staff = MreModerationPlugin.Instance;
                if (staff == null) { SetStatus(actor.Player, "MreModeration не загружен."); return; }
                string kind = staffButton == "AdminStaffKick" ? "kick" : staffButton == "AdminStaffBan" ? "ban" : staffButton == "AdminStaffMute" ? "mute" : null;
                if (kind == null || !staff.GetStaffActions(actor).Contains(kind)) { SetStatus(actor.Player, "Право отозвано; действие отменено."); return; }
                if (kind == "kick" && (targetSession == null || !ValidTarget(target, actor) || !ReferenceEquals(FindPlayer(target), targetSession)))
                { SetStatus(actor.Player, "Игровая сессия цели завершилась или сменилась; исключение отменено."); return; }
                int duration = (kind == "ban" || kind == "mute") ? MreModeration.StaffPolicy.DurationSeconds(staffDuration) : 0;
                string staffResult = kind == "kick" ? staff.KickPlayer(actor,target,staffReason,staffOperationId) : kind == "ban" ? staff.BanPlayer(actor,target,duration,staffReason,staffOperationId) : staff.MutePlayer(actor,target,duration,staffReason,staffOperationId);
                SetStatus(actor.Player, staffResult); return;
            }
            if (action != "restore" && !Allowed(actor, action)) { SetStatus(actor.Player, "Право отозвано; действие отменено."); return; }
            if (action == "kick")
            {
                if (targetSession == null || !ValidTarget(target, actor) || !ReferenceEquals(FindPlayer(target), targetSession)) { SetStatus(actor.Player, "Игровая сессия цели завершилась или сменилась; исключение отменено."); return; }
                SetStatus(actor.Player, api.ExecuteAdmin(actor, "kick", target)); return;
            }
            if (action == "kill" && (targetSession == null || !ValidTarget(target, actor) || !ReferenceEquals(FindPlayer(target), targetSession)))
            { SetStatus(actor.Player, "Игровая сессия цели завершилась или сменилась; действие отменено."); return; }
            if (action == "restore")
            {
                var inventory = MreInventoryAuditPlugin.Instance;
                if (inventory == null) { SetStatus(actor.Player, "MreInventoryAudit не загружен."); return; }
                SetStatus(actor.Player, inventory.ConfirmRestore(actor, token)); return;
            }
            if (String.IsNullOrEmpty(token)) { SetStatus(actor.Player, "Подтверждение недействительно."); return; }
            SetStatus(actor.Player, action == "remove" ? api.ConfirmRemoval(actor, token) : action == "kill" ? api.ConfirmKill(actor, token) : "Подтверждение недействительно.");
        }

        private static void ClearPending(Session s)
        {
            s.PendingToken = null; s.PendingAction = null; s.PendingTargetId = 0; s.PendingTargetPlayer = null; s.PendingDescription = null;
            s.PendingStaffButton = null; s.PendingStaffOperationId = null; s.PendingUntil = DateTime.MinValue;
            if (s.RestoreToken != null) s.RestoreToken = null;
        }

        private void ExecuteStaff(UnturnedPlayer actor, Session s, string button)
        {
            var staff = MreModerationPlugin.Instance;
            if (staff == null) { SetStatus(actor.Player, "MreModeration не загружен."); return; }
            try
            {
                switch (button)
                {
                    case "AdminStaffList":
                        s.StaffView = button;
                        var page = staff.SearchProfiles(actor, s.StaffQuery, null, s.StaffOffset, 3);
                        s.StaffTotal = page.Total;
                        if (s.StaffOffset >= s.StaffTotal && s.StaffTotal > 0) { s.StaffOffset = ((s.StaffTotal - 1) / 3) * 3; page = staff.SearchProfiles(actor, s.StaffQuery, null, s.StaffOffset, 3); }
                        SetStatus(actor.Player, "Профили " + PageRange(s.StaffOffset, page.Items.Length, page.Total) + ": " + String.Join(" | ", page.Items.Select(x => Safe(x.Name) + " [" + x.SteamId + "] trust=" + x.Trustscore).ToArray())); return;
                    case "AdminStaffProfile":
                        s.StaffView = button;
                        s.StaffTotal = 1; s.StaffOffset = 0;
                        ulong profileTarget = ResolveStaffTarget(s);
                        var profile = staff.GetProfile(actor, profileTarget);
                        SetStatus(actor.Player, profile.Name + " [" + profile.SteamId + "] · доверие " + profile.Trustscore + " · предупреждения " + profile.Warnings + " · благодарности " + profile.Commendations); return;
                    case "AdminStaffHistory":
                        s.StaffView = button;
                        ulong historyTarget = ResolveStaffTarget(s);
                        var history = staff.SearchHistory(actor, historyTarget, null, null, 0, s.StaffOffset, 3);
                        s.StaffTotal = history.Total;
                        if (s.StaffOffset >= s.StaffTotal && s.StaffTotal > 0) { s.StaffOffset = ((s.StaffTotal - 1) / 3) * 3; history = staff.SearchHistory(actor, historyTarget, null, null, 0, s.StaffOffset, 3); }
                        SetStatus(actor.Player, "История " + PageRange(s.StaffOffset, history.Items.Length, history.Total) + ": " + String.Join(" | ", history.Items.Select(x => x.Record.AtUtc.ToString("yyyy-MM-dd") + " " + x.Record.Kind + "/" + x.Record.Status + " #" + x.Record.Id + " " + Safe(x.Record.Reason)).ToArray())); return;
                    case "AdminStaffLogs":
                        s.StaffView = button;
                        ulong logsTarget = ResolveStaffTarget(s);
                        var logs = staff.SearchMessages(actor, logsTarget, null, null, null, s.StaffOffset, 3);
                        s.StaffTotal = logs.Total;
                        if (s.StaffOffset >= s.StaffTotal && s.StaffTotal > 0) { s.StaffOffset = ((s.StaffTotal - 1) / 3) * 3; logs = staff.SearchMessages(actor, logsTarget, null, null, null, s.StaffOffset, 3); }
                        SetStatus(actor.Player, "Журнал " + PageRange(s.StaffOffset, logs.Items.Length, logs.Total) + ": " + String.Join(" | ", logs.Items.Select(x => x.Message.AtUtc.ToString("yyyy-MM-dd HH:mm") + " " + x.Message.Kind + ": " + Clip(Safe(x.Message.Text), 54)).ToArray())); return;
                }
                ulong target = ResolveStaffTarget(s);
                string kind = button == "AdminStaffNote" ? "note" : button == "AdminStaffWarn" ? "warn" : button == "AdminStaffCommend" ? "commend" : button == "AdminStaffKick" ? "kick" : button == "AdminStaffBan" ? "ban" : button == "AdminStaffMute" ? "mute" : null;
                if (kind != null)
                {
                    int duration = (kind == "ban" || kind == "mute") ? MreModeration.StaffPolicy.DurationSeconds(s.StaffDuration) : 0;
                    if (kind == "kick" || kind == "ban" || kind == "mute")
                    {
                        if (!staff.GetStaffActions(actor).Contains(kind)) { SetStatus(actor.Player, "Нет права на это действие."); return; }
                        Player targetSession = kind == "kick" ? FindPlayer(target) : null;
                        if (kind == "kick" && targetSession == null) { SetStatus(actor.Player, "Исключение возможно только для онлайн-игрока."); return; }
                        string key = button + ":" + target + ":" + s.StaffReason + ":" + duration;
                        if (s.PendingOperationKey != key) { s.PendingOperationKey = key; s.PendingOperationId = Guid.NewGuid().ToString("N"); }
                        s.PendingStaffOperationId = s.PendingOperationId;
                        s.PendingStaffButton = button; s.PendingTargetId = target; s.PendingTargetPlayer = targetSession;
                        s.PendingAction = "staff"; s.PendingUntil = DateTime.UtcNow.AddSeconds(15);
                        string targetName = FindPlayerName(target);
                        string durationLabel = kind == "ban" || kind == "mute" ? (duration == 0 ? "постоянно" : "на " + s.StaffDuration) : String.Empty;
                        s.PendingDescription = "Подтвердить " + kind + " для " + (targetName ?? target.ToString()) + " [" + target + "] " + durationLabel + ". Причина: " + Clip(Safe(s.StaffReason), 100) + ". Для kick подтверждается та же онлайн-сессия. Действие действует 15 секунд.";
                        SetStatus(actor.Player, s.PendingDescription); return;
                    }
                    string keyDirect = kind + ":" + target + ":" + s.StaffReason;
                    if (keyDirect != s.PendingOperationKey) { s.PendingOperationKey = keyDirect; s.PendingOperationId = Guid.NewGuid().ToString("N"); }
                    string result = kind == "note" ? staff.AddNote(actor,target,s.StaffReason,s.PendingOperationId) : kind == "warn" ? staff.WarnPlayer(actor,target,s.StaffReason,s.PendingOperationId) : staff.CommendPlayer(actor,target,s.StaffReason,s.PendingOperationId);
                    SetStatus(actor.Player, result); return;
                }
                if (button == "AdminStaffUnban") { SetStatus(actor.Player, staff.UnbanPlayer(actor,target,s.StaffReason)); return; }
                if (button == "AdminStaffUnmute") { SetStatus(actor.Player, staff.UnmutePlayer(actor,target,s.StaffReason)); return; }
                if (button == "AdminStaffRevoke") { SetStatus(actor.Player, staff.RevokeModerationRecord(actor,target,s.StaffRecordId,s.StaffReason)); return; }
            }
            catch (Exception error) { SetStatus(actor.Player, Safe(error.Message)); }
        }

        private static ulong ResolveStaffTarget(Session s)
        {
            ulong id; if (UInt64.TryParse(s.StaffQuery, out id) && id != 0) return id;
            if (s.TargetId != 0) return s.TargetId;
            throw new InvalidOperationException("Введите SteamID64 в поле поиска или выберите игрока.");
        }

        private static string PageRange(int skip,int count,int total) { return total==0 ? "0 / 0" : (skip+1)+"–"+(skip+count)+" из "+total; }
        private static string Clip(string value,int length) { value=value??String.Empty; return value.Length<=length?value:value.Substring(0,length-1)+"…"; }
        private static Player FindPlayer(ulong id) { var client=Provider.clients.FirstOrDefault(c=>c!=null&&c.player!=null&&c.playerID.steamID.m_SteamID==id); return client==null?null:client.player; }
        private static string FindPlayerName(ulong id) { var client=Provider.clients.FirstOrDefault(c=>c!=null&&c.player!=null&&c.playerID.steamID.m_SteamID==id); return client==null?null:Safe(client.playerID.characterName); }

        private void ExecuteInventory(UnturnedPlayer actor, Session s, string button)
        {
            var inventory = MreInventoryAuditPlugin.Instance;
            if (inventory == null) { SetStatus(actor.Player, "MreInventoryAudit не загружен."); return; }
            try
            {
                ulong target = ResolveInventoryTarget(s);
                if (FindPlayer(target) == null) { InvalidateInventory(s); SetStatus(actor.Player, "Осмотр и снимки доступны только для сейчас подключённой цели. Проверьте SteamID64 или выберите игрока."); return; }
                if (s.InventoryTargetId != target) InvalidateInventory(s);
                s.InventoryTargetId = target;
                if (button == "AdminInventoryInspect")
                {
                    bool sameView=s.InventoryMode=="inspect";
                    var view = inventory.Inspect(actor,target);
                    s.InventoryItems = view.Items ?? new InventoryItemView[0]; if(!sameView)s.InventoryOffset=0; s.InventoryMode = "inspect";
                    int pages = Math.Max(1,(s.InventoryItems.Length+3)/4); s.InventoryOffset = Math.Min(s.InventoryOffset,Math.Max(0,(pages-1)*4));
                    SetStatus(actor.Player, FormatInventoryPage(s)); return;
                }
                if (button == "AdminInventoryDeaths")
                {
                    bool sameView=s.InventoryMode=="deaths";
                    s.Deaths = inventory.GetDeaths(actor,target) ?? new InventoryView[0]; s.InventoryMode = "deaths"; if(!sameView)s.DeathsOffset=0;
                    int pages=Math.Max(1,(s.Deaths.Length+3)/4);s.DeathsOffset=Math.Min(s.DeathsOffset,Math.Max(0,(pages-1)*4));
                    SetStatus(actor.Player, FormatInventoryPage(s)); return;
                }
                if (button == "AdminInventoryDetails")
                {
                    bool sameView=s.InventoryMode=="details"&&s.SnapshotDetails!=null&&s.SnapshotDetails.SnapshotId==s.SnapshotId;
                    s.Deaths = inventory.GetDeaths(actor,target) ?? new InventoryView[0];
                    var snapshot = s.Deaths.FirstOrDefault(d => d.SnapshotId == s.SnapshotId);
                    if (snapshot == null) { SetStatus(actor.Player, "Снимок не найден в загруженном списке. Сначала обновите список снимков."); return; }
                    s.SnapshotDetails = snapshot; s.InventoryMode = "details"; if(!sameView)s.InventoryOffset=0;
                    int pages=Math.Max(1,(snapshot.Items.Length+3)/4);s.InventoryOffset=Math.Min(s.InventoryOffset,Math.Max(0,(pages-1)*4));
                    SetStatus(actor.Player, FormatInventoryPage(s)); return;
                }
                if (String.IsNullOrWhiteSpace(s.SnapshotId)) { SetStatus(actor.Player, "Введите стабильный SnapshotId."); return; }
                var preview = inventory.PrepareRestoreById(actor,target,s.SnapshotId);
                s.PendingToken = preview.Token; s.RestoreToken = preview.Token; s.PendingAction = "restore"; s.PendingTargetId = target; s.PendingTargetPlayer = FindPlayer(target); s.PendingUntil = preview.ExpiresUtc;
                s.PendingDescription = "Выдать как компенсацию " + preview.Inventory.Items.Length + " предметов из снимка " + preview.Inventory.SnapshotId + " игроку [" + target + "]? Лут на месте смерти не удаляется и остаётся в мире. Если в личном инвентаре не хватит места, выдачи не будет. Одноразовое подтверждение действует 30 секунд.";
                SetStatus(actor.Player, s.PendingDescription);
            }
            catch (Exception error) { SetStatus(actor.Player, Safe(error.Message)); }
        }

        private static ulong ResolveInventoryTarget(Session s)
        {
            ulong explicitId;
            if (!String.IsNullOrWhiteSpace(s.StaffQuery))
            {
                if (UInt64.TryParse(s.StaffQuery,out explicitId) && explicitId != 0) return explicitId;
                throw new InvalidOperationException("Для операций с инвентарём укажите SteamID64 или очистите поле и выберите игрока.");
            }
            if (s.TargetId != 0) return s.TargetId;
            throw new InvalidOperationException("Введите SteamID64 в поле поиска или выберите подключённого игрока.");
        }

        private static string FormatInventoryPage(Session s)
        {
            const int pageSize=4;
            if (s.InventoryMode=="inspect")
            {
                var items=s.InventoryItems??new InventoryItemView[0]; int start=s.InventoryOffset; int end=Math.Min(items.Length,start+pageSize);
                return "Инвентарь ["+s.InventoryTargetId+"] · "+PageRange(start,end-start,items.Length)+": "+String.Join(" | ",items.Skip(start).Take(pageSize).Select(i=>"#"+i.Id+" "+Clip(Safe(i.Name),30)+" ×"+i.Amount+" q"+i.Quality).ToArray());
            }
            if (s.InventoryMode=="details" && s.SnapshotDetails!=null)
            {
                var snapshot=s.SnapshotDetails; int start=s.InventoryOffset; int end=Math.Min(snapshot.Items.Length,start+pageSize);
                return "Снимок "+snapshot.SnapshotId+" · "+snapshot.AtUtc.ToString("yyyy-MM-dd HH:mm")+" · "+snapshot.RestoreStatus+" · предметы "+PageRange(start,end-start,snapshot.Items.Length)+": "+String.Join(" | ",snapshot.Items.Skip(start).Take(pageSize).Select(i=>"#"+i.Id+" "+Clip(Safe(i.Name),30)+" ×"+i.Amount).ToArray());
            }
            var deaths=s.Deaths??new InventoryView[0]; int deathStart=s.DeathsOffset; int deathEnd=Math.Min(deaths.Length,deathStart+pageSize);
            return "Снимки смерти ["+s.InventoryTargetId+"] · "+PageRange(deathStart,deathEnd-deathStart,deaths.Length)+": "+String.Join(" | ",deaths.Skip(deathStart).Take(pageSize).Select(d=>d.SnapshotId+" "+d.AtUtc.ToString("MM-dd HH:mm")+" ["+d.RestoreStatus+"]").ToArray())+". Введите SnapshotId для деталей или выдачи компенсации.";
        }

        private bool ValidTarget(ulong id, UnturnedPlayer actor)
        {
            return id != 0 && id != actor.CSteamID.m_SteamID && Provider.clients.Any(c => c != null && c.player != null && c.playerID.steamID.m_SteamID == id);
        }

        private static SteamPlayer[] OnlinePlayers() { return Provider.clients.Where(c => c != null && c.player != null).OrderBy(c => c.playerID.characterName, StringComparer.OrdinalIgnoreCase).ToArray(); }
        private static bool NeedsTarget(string action) { return action == "tp" || action == "bring" || action == "heal" || action == "kick" || action == "spectate" || action == "freeze" || action == "kill"; }
        private static string PolicyAction(string button)
        {
            switch (button)
            {
                case "AdminTp": return "tp"; case "AdminBring": return "bring"; case "AdminHeal": return "heal"; case "AdminKick": return "kick";
                case "AdminFly": return "fly"; case "AdminFreecam": return "freecam"; case "AdminSpectate": return "spectate";
                case "AdminGod": return "god"; case "AdminVanish": return "vanish"; case "AdminFreeze": return "freeze";
                case "AdminWorkzone": return "workzone"; case "AdminStats": return "stats"; case "AdminDoor": return "door";
                case "AdminStorage": return "storage"; case "AdminOwner": return "owner"; case "AdminRemove": return "remove"; case "AdminKill": return "kill";
                case "AdminSave": return "save"; case "AdminDay": return "day"; case "AdminNight": return "night";
                default: return null;
            }
        }
        private static bool CurrentMode(UnturnedPlayer actor, string mode) { return mode == "god" ? actor.GodMode : actor.VanishMode; }
        private static string Safe(string value) { return new String(value ?? String.Empty).Replace("<", String.Empty).Replace(">", String.Empty).Replace("\n", " "); }

        private void Render(Player player, Session s)
        {
            var c = player.channel.GetOwnerTransportConnection();
            var viewer = UnturnedPlayer.FromPlayer(player);
            ulong shownTarget=s.TargetId; ulong queryId;
            if (s.Tab==5 && UInt64.TryParse(s.StaffQuery,out queryId) && queryId!=0) shownTarget=queryId;
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminTarget", TargetLabel(shownTarget));
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminItemValue", s.ItemId.ToString());
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminAmountValue", s.Amount.ToString());
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminVehicleValue", s.VehicleId.ToString());
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminAnnouncementText", s.AnnouncementText);
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminRestartMinutes", s.RestartMinutes);
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminHorizontalSpeed", s.HorizontalSpeed);
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminVerticalSpeed", s.VerticalSpeed);
            var flight = MreFlightPlugin.Instance;
            if (flight != null)
            {
                float[] speeds = flight.GetSpeed(viewer);
                EffectManager.sendUIEffectText(EffectKey, c, true, "AdminFlightSpeedCurrent", speeds[0].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " / " + speeds[1].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminPage", (s.Page + 1).ToString());
            bool canListPlayers = api != null && api.CanExecuteAdmin(viewer, "players");
            var list = canListPlayers ? OnlinePlayers() : new SteamPlayer[0];
            for (int i = 0; i < 6; i++)
            {
                int index = s.Page * 6 + i; bool visible = index < list.Length;
                s.ListedPlayers[i] = visible ? list[index].playerID.steamID.m_SteamID : 0;
                EffectManager.sendUIEffectVisibility(EffectKey, c, true, "AdminPlayerRow" + i, visible && canListPlayers);
                if (visible)
                {
                    var p = list[index]; ulong id = p.playerID.steamID.m_SteamID;
                    EffectManager.sendUIEffectText(EffectKey, c, true, "AdminPlayerName" + i, Safe(p.playerID.characterName));
                    EffectManager.sendUIEffectText(EffectKey, c, true, "AdminPlayerId" + i, id.ToString());
                    EffectManager.sendUIEffectVisibility(EffectKey, c, true, "AdminPlayerSelected" + i, id == s.TargetId);
                }
            }
            string[][] policyButtons = {
                new[]{"AdminTp","tp"}, new[]{"AdminBring","bring"}, new[]{"AdminHeal","heal"}, new[]{"AdminHealSelf","heal"}, new[]{"AdminKick","kick"}, new[]{"AdminSpectate","spectate"},
                new[]{"AdminFreeze","freeze"}, new[]{"AdminKill","kill"}, new[]{"AdminFly","fly"}, new[]{"AdminFreecam","freecam"}, new[]{"AdminGod","god"}, new[]{"AdminVanish","vanish"},
                new[]{"AdminWorkzone","workzone"}, new[]{"AdminStats","stats"}, new[]{"AdminDoor","door"}, new[]{"AdminStorage","storage"}, new[]{"AdminOwner","owner"}, new[]{"AdminRemove","remove"},
                new[]{"AdminSave","save"}, new[]{"AdminDay","day"}, new[]{"AdminNight","night"}, new[]{"AdminAnnounceEvent","announce"}, new[]{"AdminAnnounceRestart","announce"}
            };
            foreach (var route in policyButtons) EffectManager.sendUIEffectVisibility(EffectKey,c,true,route[0],api != null && api.CanExecuteAdmin(viewer,route[1]));
            bool canAnnounce = api != null && api.CanExecuteAdmin(viewer,"announce");
            bool canFly = api != null && api.CanExecuteAdmin(viewer,"fly");
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminStopSpectate",api != null && api.CanExecuteAdmin(viewer,"spectate"));
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminFlightSpeed",canFly);
            foreach (string name in new[]{"AdminAnnouncementText","AdminRestartMinutes"}) EffectManager.sendUIEffectVisibility(EffectKey,c,true,name,canAnnounce);
            foreach (string name in new[]{"AdminHorizontalSpeed","AdminVerticalSpeed","AdminFlightSpeedCurrent"}) EffectManager.sendUIEffectVisibility(EffectKey,c,true,name,canFly);
            bool baseAdmin = api != null && api.HasAdminPermission(viewer,"mre.admin");
            bool canGive = baseAdmin && api.HasAdminPermission(viewer,"mre.admin.give");
            bool canVehicle = baseAdmin && api.HasAdminPermission(viewer,"mre.admin.veh");
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminGive",canGive);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminGiveSelf",canGive);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminVehicle",canVehicle);
            foreach (string name in new[]{"AdminItemValue","AdminItemDown10","AdminItemDown","AdminItemUp","AdminItemUp10","AdminAmountValue","AdminAmountDown10","AdminAmountDown","AdminAmountUp","AdminAmountUp10"}) EffectManager.sendUIEffectVisibility(EffectKey,c,true,name,canGive);
            foreach (string name in new[]{"AdminVehicleValue","AdminVehicleDown10","AdminVehicleDown","AdminVehicleUp","AdminVehicleUp10"}) EffectManager.sendUIEffectVisibility(EffectKey,c,true,name,canVehicle);
            bool canInventoryInspect = baseAdmin && api.HasAdminPermission(viewer,"mre.inventory.inspect");
            bool canInventoryDeaths = baseAdmin && api.HasAdminPermission(viewer,"mre.inventory.deaths");
            bool canInventoryRestore = baseAdmin && api.HasAdminPermission(viewer,"mre.inventory.restore");
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminInventoryInspect",canInventoryInspect);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminInventoryDeaths",canInventoryDeaths);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminInventoryDetails",canInventoryDeaths);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminSnapshotId",canInventoryDeaths || canInventoryRestore);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminRestorePrepare",canInventoryRestore);
            int inventoryCount = s.InventoryMode=="inspect" ? (s.InventoryItems==null?0:s.InventoryItems.Length) :
                s.InventoryMode=="details" ? (s.SnapshotDetails==null||s.SnapshotDetails.Items==null?0:s.SnapshotDetails.Items.Length) :
                (s.Deaths==null?0:s.Deaths.Length);
            bool inventoryCanPage = s.InventoryMode=="inspect" ? canInventoryInspect : s.InventoryMode=="details" || s.InventoryMode=="deaths" ? canInventoryDeaths : false;
            bool inventoryHasPages = inventoryCanPage && inventoryCount > 4;
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminInventoryPrev",inventoryHasPages);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminInventoryNext",inventoryHasPages);
            bool canAirdrop = api != null && api.HasAdminPermission(viewer,"mre.airdrop.admin");
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminAirdropStatus",canAirdrop);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminAirdropLaunch",canAirdrop);
            for (int i = 0; i < 6; i++) EffectManager.sendUIEffectVisibility(EffectKey, c, true, "AdminTabSelected" + i, i == s.Tab);
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminStaffQuery", s.StaffQuery);
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminStaffReason", s.StaffReason);
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminStaffDuration", s.StaffDuration);
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminStaffRecordId", s.StaffRecordId);
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminSnapshotId", s.SnapshotId);
            bool hasStaff = MreModerationPlugin.Instance != null;
            string[] staffActions = new string[0];
            if (hasStaff && api != null && api.HasAdminPermission(viewer, "mre.staff"))
                try { staffActions = MreModerationPlugin.Instance.GetStaffActions(viewer); } catch { staffActions = new string[0]; }
            string[] staffButtons = { "list", "profile", "history", "note", "warn", "commend", "kick", "logs", "revoke", "ban", "mute", "unban", "unmute" };
            for (int i = 0; i < staffButtons.Length; i++) EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminStaff" + Char.ToUpperInvariant(staffButtons[i][0]) + staffButtons[i].Substring(1),staffActions.Contains(staffButtons[i]));
            string staffPageAction=s.StaffView=="AdminStaffList"?"list":s.StaffView=="AdminStaffHistory"?"history":s.StaffView=="AdminStaffLogs"?"logs":null;
            bool staffHasPages=staffPageAction!=null&&staffActions.Contains(staffPageAction)&&s.StaffTotal>3;
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminStaffPrev",staffHasPages);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminStaffNext",staffHasPages);
            bool canStaff = staffActions.Length > 0;
            bool canInventory = canInventoryInspect || canInventoryDeaths || canInventoryRestore || canAirdrop;
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminTab4",canStaff);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminTabSelected4",canStaff && s.Tab==4);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminSection4",canStaff && s.Tab==4);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminTab5",canInventory);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminTabSelected5",canInventory && s.Tab==5);
            EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminSection5",canInventory && s.Tab==5);
            bool[] adminSections = {
                CanAnyAdmin(viewer,"players","tp","bring","heal","kick","spectate","freeze","kill"),
                CanAnyAdmin(viewer,"fly","freecam","god","vanish","workzone","stats"),
                CanAnyAdmin(viewer,"owner","door","storage","remove"),
                CanAnyAdmin(viewer,"save","day","night","announce") || canGive || canVehicle
            };
            for (int i = 0; i < adminSections.Length; i++)
            {
                EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminTab"+i,adminSections[i]);
                EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminTabSelected"+i,adminSections[i] && i==s.Tab);
                EffectManager.sendUIEffectVisibility(EffectKey,c,true,"AdminSection"+i,adminSections[i] && i==s.Tab);
            }
            EffectManager.sendUIEffectVisibility(EffectKey, c, true, "AdminContent", true);
            EffectManager.sendUIEffectVisibility(EffectKey, c, true, "AdminLauncher", false);
            if (!String.IsNullOrEmpty(s.PendingAction) && DateTime.UtcNow > s.PendingUntil) ClearPending(s);
            EffectManager.sendUIEffectText(EffectKey, c, true, "AdminConfirmPrompt", s.PendingAction == null ? String.Empty : Safe(s.PendingDescription));
            EffectManager.sendUIEffectVisibility(EffectKey, c, true, "AdminConfirmPanel", !String.IsNullOrEmpty(s.PendingAction));
        }

        private static string TargetLabel(ulong id)
        {
            if (id == 0) return "Игрок не выбран";
            var p = Provider.clients.FirstOrDefault(x => x != null && x.player != null && x.playerID.steamID.m_SteamID == id);
            return p == null ? "Игрок вышел — выберите цель" : Safe(p.playerID.characterName) + "  [" + id + "]";
        }
        private bool CanAnyAdmin(UnturnedPlayer actor, params string[] actions)
        {
            return api != null && actions.Any(action => api.CanExecuteAdmin(actor, action));
        }
        private void HideEffect(Player player)
        {
            try { if (player != null && player.channel != null) EffectManager.ClearEffectByGuid(EffectGuid, player.channel.GetOwnerTransportConnection()); }
            catch (Exception error) { Logger.LogException(error, "[MRE ADMIN UI] Не удалось убрать кнопку панели."); }
        }
        private static void SetStatus(Player player, string message)
        {
            if (player == null || player.channel == null) return;
            EffectManager.sendUIEffectText(EffectKey, player.channel.GetOwnerTransportConnection(), true, "AdminStatus", Safe(message));
        }
    }
}
