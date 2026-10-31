using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Rocket.Core;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreInterface
{
    public sealed partial class MreInterfacePlugin : RocketPlugin
    {
        public static MreInterfacePlugin Instance { get; private set; }
        public event Action<CombatRecord> KillRecorded;
        public event Action<CombatRecord> DamageRecorded;
        public event Func<UnturnedPlayer, bool> AdminMenuRequested;
        private readonly HashSet<ulong> menusBeingOpened = new HashSet<ulong>();
        private readonly RecentRecords<CombatRecord> kills = new RecentRecords<CombatRecord>(5);
        private readonly Dictionary<ulong, RecentRecords<CombatRecord>> damage = new Dictionary<ulong, RecentRecords<CombatRecord>>();
        private readonly Dictionary<ulong, DateTime> nextAction = new Dictionary<ulong, DateTime>();
        private int gameThread;
        private bool commandVerified;
        private DamageObserver observer;

        protected override void Load()
        {
            gameThread = Thread.CurrentThread.ManagedThreadId;
            Instance = this;
            // Rocket автоматически регистрирует IRocketCommand после Load.
            // Выбор /admin задаётся Commands.config.xml ДО запуска сервера.
            PlayerLife.onPlayerDied += OnPlayerDied;
            Provider.onServerDisconnected += OnDisconnected;
            VehicleManager.onEnterVehicleRequested += OnEnterVehicleRequested;
            observer = new DamageObserver(this);
            try { observer.Start(); }
            catch (Exception error)
            {
                observer.Dispose(); observer = null;
                Logger.LogException(error, "[MRE] Наблюдатель урона отключён; серверная админка продолжает работать.");
            }
            Logger.Log("[MRE] MreInterface загружен: серверная /admin, история убийств и события урона. Визуальный UI предоставляется отдельно.");
        }

        protected override void Unload()
        {
            foreach (ulong id in observations.Keys.Concat(freecamGrants.Keys).Concat(godGrants.Keys).Concat(vanishGrants.Keys).Distinct().ToArray()) CleanupStaff(id, true);
            foreach (ulong id in workzoneGrants.Keys.Concat(statsGrants.Keys).Concat(storageGrants.Keys).Distinct().ToArray()) CleanupStaff(id, true);
            foreach (ulong id in freezes.Keys.ToArray()) StopFreeze(id);
            killsPending.Clear();
            removals.Clear();
            if (observer != null) observer.Dispose();
            PlayerLife.onPlayerDied -= OnPlayerDied;
            Provider.onServerDisconnected -= OnDisconnected;
            VehicleManager.onEnterVehicleRequested -= OnEnterVehicleRequested;
            damage.Clear(); nextAction.Clear();
            menusBeingOpened.Clear(); AdminMenuRequested = null; KillRecorded = null; DamageRecorded = null;
            Instance = null;
        }

        private void Update()
        {
            if (Level.isLoaded) TickStaffModes();
            if (Level.isLoaded) TickAdditionalTools();
            if (commandVerified || !Level.isLoaded) return;
            var selected = R.Commands.GetCommand("admin") as Rocket.Core.Commands.RocketCommandManager.RegisteredRocketCommand;
            bool ours = selected != null && selected.Command is CommandAdmin;
            Logger.Log("[MRE] /admin dispatch=" + (ours ? "MreInterface OK" : "CONFLICT; use /mreadmin and review Commands.config.xml") + "; damage observer=" + (observer != null && observer.Enabled));
            commandVerified = true;
        }

        private void RequireGameThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != gameThread) throw new InvalidOperationException("MRE API вызывается только на главном игровом потоке.");
        }

        private static bool IsOnline(UnturnedPlayer actor)
        {
            return actor != null && actor.Player != null && Provider.clients.Any(c => c != null && c.player == actor.Player && c.playerID.steamID == actor.CSteamID);
        }

        public bool HasAdminPermission(UnturnedPlayer actor, string permission)
        {
            RequireGameThread();
            return IsOnline(actor) && (actor.IsAdmin || R.Permissions.HasPermission(actor, new List<string> { permission }));
        }

        public bool CanExecuteAdmin(UnturnedPlayer actor, string action)
        {
            RequireGameThread();
            return AdminPolicy.CanExecute(action, p => HasAdminPermission(actor, p));
        }

        public string ExecuteAdmin(UnturnedPlayer actor, string action, ulong targetId = 0)
        {
            RequireGameThread();
            if (!CanExecuteAdmin(actor, action)) return "Нет прав на это действие.";
            if (!Provider.isServer || !Level.isLoaded) return "Мир ещё не готов.";
            if (action == "fly") return RunBodyFlight(actor, null);
            if (action == "freecam") return SetFreecamPermission(actor, true);
            if (action == "spectate") return StartObservation(actor, targetId);
            if (action == "god") return SetStaffMode(actor, action, !actor.GodMode);
            if (action == "vanish") return SetStaffMode(actor, action, !actor.VanishMode);
            if (action == "remove") return DescribeRemoval(PreviewRemoval(actor));
            if (action == "owner") return DescribeOwnership(InspectOwnership(actor));
            if (action == "workzone") return SetWorkzonePermission(actor, true);
            if (action == "stats") return SetStatsPermission(actor, true);
            if (action == "door") return ToggleAimedDoor(actor);
            if (action == "storage") return OpenAimedStorage(actor);
            if (action == "freeze") return SetFreeze(actor, targetId, !freezes.ContainsKey(targetId));
            if (action == "kill") return DescribeKill(PreviewKill(actor, targetId));
            DateTime next;
            if (nextAction.TryGetValue(actor.CSteamID.m_SteamID, out next) && DateTime.UtcNow < next) return "Подожди секунду перед следующим действием.";
            nextAction[actor.CSteamID.m_SteamID] = DateTime.UtcNow.AddSeconds(0.5);
            if (action == "players") return string.Join("; ", Provider.clients.Where(p => p != null).Select(p => p.playerID.characterName + " [" + p.playerID.steamID.m_SteamID + "]").ToArray());
            if (action == "save") { SaveManager.save(); Audit(actor, action, 0); return "Мир сохранён."; }
            if (action == "day" || action == "night")
            {
                LightingManager.time = (uint)(LightingManager.cycle * (action == "day" ? LevelLighting.transition : LevelLighting.bias + LevelLighting.transition));
                Audit(actor, action, 0); return action == "day" ? "Установлен день." : "Установлена ночь.";
            }
            if (targetId == 0 && action == "heal") targetId = actor.CSteamID.m_SteamID;
            SteamPlayer target = Provider.clients.FirstOrDefault(p => p != null && p.player != null && p.playerID.steamID.m_SteamID == targetId);
            if (target == null) return "Игрок не найден или уже вышел.";
            if ((action == "tp" || action == "bring") && (target.player.life.isDead || actor.Player.life.isDead)) return "Нельзя телепортировать мёртвого игрока.";
            bool succeeded = true;
            switch (action)
            {
                case "tp": succeeded = actor.Player.teleportToLocation(target.player.transform.position, target.player.look.yaw); break;
                case "bring": succeeded = target.player.teleportToLocation(actor.Player.transform.position, actor.Player.look.yaw); break;
                case "heal":
                    if (target.player.life.isDead) return "Нельзя вылечить мёртвого игрока.";
                    target.player.life.askHeal(100, true, true); break;
                case "kick":
                    if (target.playerID.steamID == actor.CSteamID) return "Нельзя исключить себя через это меню.";
                    Provider.kick(target.playerID.steamID, "Исключён администратором MRE."); break;
            }
            if (!succeeded) return "Телепортация отклонена игровым сервером.";
            Audit(actor, action, targetId); return "Готово: " + action + ".";
        }

        private static void Audit(UnturnedPlayer actor, string action, ulong target)
        {
            Logger.Log("[MRE ADMIN] actor=" + actor.CSteamID.m_SteamID + " action=" + action + " target=" + target);
        }

        public void HandleAdminCommand(UnturnedPlayer actor, string[] args)
        {
            RequireGameThread();
            // Выход из собственного временного режима доступен и после отзыва прав.
            if (args.Length == 2 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                string cleanup = args[0].ToLowerInvariant();
                if (cleanup == "spectate" || cleanup == "fly" || cleanup == "freecam" || cleanup == "god" || cleanup == "vanish" || cleanup == "workzone" || cleanup == "stats")
                { HandleStaffCommand(actor, cleanup, new[] { cleanup, "off" }); return; }
            }
            if (!HasAdminPermission(actor, "mre.admin")) { UnturnedChat.Say(actor, "MRE: нет доступа к админке."); return; }
            if (args.Length == 0 && TryOpenAdminMenu(actor)) return;
            if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                UnturnedChat.Say(actor, "MRE /admin: " + string.Join(", ", AdminPolicy.Actions.Where(a => CanExecuteAdmin(actor, a)).ToArray()) + ". tp/bring/kick требуют SteamID64 или точное имя; heal без имени лечит себя.");
                UnturnedChat.Say(actor, "MRE: профили и наказания — /staff; инвентарь — /inspectinventory; смерти — /previewdeath и /restoredeath. Эти модули требуют отдельных прав."); return;
            }
            string action = args[0].ToLowerInvariant();
            if (!AdminPolicy.KnownAction(action)) { UnturnedChat.Say(actor, "MRE: неизвестное действие. /admin help"); return; }
            if (action == "fly" || action == "freecam" || action == "god" || action == "vanish" || action == "workzone" || action == "stats" || action == "remove" || (action == "kill" && args.Length == 3 && args[1].Equals("confirm", StringComparison.OrdinalIgnoreCase)) || (action == "spectate" && args.Length == 2 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase)))
            {
                HandleStaffCommand(actor, action, args); return;
            }
            ulong targetId = 0;
            if (args.Length > 1)
            {
                string query = string.Join(" ", args.Skip(1).ToArray());
                if (!ulong.TryParse(query, out targetId))
                {
                    var matches = Provider.clients.Where(p => p != null && (p.playerID.characterName.Equals(query, StringComparison.OrdinalIgnoreCase) || p.playerID.playerName.Equals(query, StringComparison.OrdinalIgnoreCase))).ToArray();
                    if (matches.Length != 1) { UnturnedChat.Say(actor, "MRE: нужно уникальное точное имя или SteamID64 из /admin players."); return; }
                    targetId = matches[0].playerID.steamID.m_SteamID;
                }
            }
            try { UnturnedChat.Say(actor, "MRE: " + ExecuteAdmin(actor, action, targetId)); }
            catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка действия /admin."); UnturnedChat.Say(actor, "MRE: действие не выполнено, ошибка записана в журнал."); }
        }

        public CombatRecord[] GetRecentKills() { RequireGameThread(); return kills.Snapshot(); }
        public bool TryOpenAdminMenu(UnturnedPlayer actor)
        {
            RequireGameThread();
            if (!HasAdminPermission(actor, "mre.admin") || !menusBeingOpened.Add(actor.CSteamID.m_SteamID)) return false;
            try { return MenuRequestDispatch.TryOpen(AdminMenuRequested, actor, error => Logger.LogException(error, "[MRE] UI админки не открылся; доступна /admin help.")); }
            finally { menusBeingOpened.Remove(actor.CSteamID.m_SteamID); }
        }
        public CombatRecord[] GetRecentDamage(UnturnedPlayer viewer)
        {
            RequireGameThread();
            RecentRecords<CombatRecord> history;
            return IsOnline(viewer) && damage.TryGetValue(viewer.CSteamID.m_SteamID, out history) ? history.Snapshot() : new CombatRecord[0];
        }

        private void OnDisconnected(CSteamID id) { CleanupStaff(id.m_SteamID, false); CleanupFreezesFor(id.m_SteamID); damage.Remove(id.m_SteamID); nextAction.Remove(id.m_SteamID); }
        private void OnPlayerDied(PlayerLife victim, EDeathCause cause, ELimb limb, CSteamID attacker)
        {
            CleanupStaff(victim.channel.owner.playerID.steamID.m_SteamID, false);
            CleanupFreezesFor(victim.channel.owner.playerID.steamID.m_SteamID);
            var record = MakeRecord(victim, attacker, cause, limb, 0, true);
            kills.Add(record); Publish(KillRecorded, record);
        }

        internal void RecordDamage(PlayerLife victim, CSteamID attacker, EDeathCause cause, ELimb limb, byte amount)
        {
            if (amount == 0 || attacker == CSteamID.Nil) return;
            var record = MakeRecord(victim, attacker, cause, limb, amount, false);
            RecentRecords<CombatRecord> history;
            if (!damage.TryGetValue(attacker.m_SteamID, out history)) { history = new RecentRecords<CombatRecord>(4); damage[attacker.m_SteamID] = history; }
            history.Add(record); Publish(DamageRecorded, record);
        }

        private static CombatRecord MakeRecord(PlayerLife victim, CSteamID attacker, EDeathCause cause, ELimb limb, byte amount, bool kill)
        {
            Player source = PlayerTool.getPlayer(attacker);
            return new CombatRecord { AtUtc = DateTime.UtcNow, AttackerId = attacker.m_SteamID, AttackerName = source != null ? source.channel.owner.playerID.characterName : "Окружение", VictimId = victim.channel.owner.playerID.steamID.m_SteamID, VictimName = victim.channel.owner.playerID.characterName, Damage = amount, Cause = cause.ToString(), Limb = limb.ToString(), WeaponId = source != null && source.equipment.asset != null ? source.equipment.asset.id : (ushort)0, IsKill = kill };
        }

        private static void Publish(Action<CombatRecord> listeners, CombatRecord record)
        {
            SafeEventDispatch.Publish(listeners, record, error => Logger.LogException(error, "[MRE] Ошибка подписчика события боя."));
        }
    }
}
