using MreShared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreEssentials
{
    public sealed class MreEssentialsPlugin : RocketPlugin<MreEssentialsConfiguration>
    {
        public static MreEssentialsPlugin Instance { get; private set; }
        public const int VoteDurationSeconds = 30;
        private readonly System.Diagnostics.Stopwatch voteClock = new System.Diagnostics.Stopwatch();
        private DateTime voteClockOriginUtc;
        private DateTime VoteNow() { return voteClockOriginUtc.Add(voteClock.Elapsed); }
        private VoteSession session;
        private sealed class VoteInput
        {
            public readonly Player Player;
            public readonly VoteHoldKeys Hold = new VoteHoldKeys(2, 3, ControlsSettings.NUM_PLUGIN_KEYS, 2, 0.6);
            public uint Frame;
            public bool HasFrame, HasYes, HasNo, Yes, No;
            public VoteInput(Player player) { Player = player; }
        }
        private readonly Dictionary<ulong, VoteInput> voteInputs = new Dictionary<ulong, VoteInput>();
        private string voteInitiatorName;
        private VoteSession finishedVote;
        private string finishedVotePhase;
        private List<ulong> finishedVoteOnline;
        private DateTime voteUiVisibleUntilUtc;

        public VoteUiSnapshot GetVoteUiSnapshot(ulong viewerSteamId)
        {
            DateTime now = VoteNow();
            if (session != null)
                return VoteUiSnapshot.Capture(session, OnlinePlayers(), viewerSteamId,
                    voteInitiatorName, VoteDurationSeconds, now);
            if (now >= voteUiVisibleUntilUtc || finishedVote == null) return null;
            var finished = VoteUiSnapshot.Capture(finishedVote, finishedVoteOnline, viewerSteamId,
                voteInitiatorName, VoteDurationSeconds, now);
            return finished == null ? null : finished.WithPhase(finishedVotePhase);
        }

        private void FinishVoteUi(string phase, DateTime now)
        {
            finishedVote = session;
            finishedVotePhase = phase;
            finishedVoteOnline = OnlinePlayers();
            voteUiVisibleUntilUtc = now.AddSeconds(8);
            session = null;
            voteInputs.Clear();
        }
        private DateTime nextVoteUtc;
        private DateTime nextSaveUtc;
        private DateTime nextTickUtc;
        private DateTime nextControlTickUtc;
        private LocalControlBridge control;
        private PresenceAnnouncements presence;
        private string localControlDirectory;
        private readonly HashSet<ulong> pendingSkills = new HashSet<ulong>();
        private bool initialSkillsApplied;
        private DateTime nextSkillsRefreshUtc;

        protected override void Load()
        {
            var c = Configuration.Instance;
            if (c.MinimumPlayers < 1 || c.RequiredPercent < 50 || c.RequiredPercent > 100 ||
                c.VoteCooldownSeconds < VoteDurationSeconds || c.AutoSaveIntervalSeconds < 30)
                throw new InvalidOperationException("Некорректная конфигурация MreEssentials. Проверьте minimum/percent/duration/cooldown/autosave.");
            voteClockOriginUtc = DateTime.UtcNow;
            voteClock.Restart();
            nextVoteUtc = DateTime.MinValue;
            nextTickUtc = DateTime.UtcNow;
            nextSaveUtc = DateTime.UtcNow.AddSeconds(c.AutoSaveIntervalSeconds);
            nextControlTickUtc = DateTime.UtcNow;
            if (c.LocalControlEnabled)
            {
                string executablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string controlRoot = LocalControlBridge.ResolveDirectory(executablePath, c.LocalControlDirectory);
                localControlDirectory = controlRoot;
                control = new LocalControlBridge(controlRoot, ExecuteLocalCommand);
                Logger.Log("[MRE] Local control ready: " + controlRoot);
            }
            presence = new PresenceAnnouncements();
            presence.Start();
            Instance = this;
            initialSkillsApplied = false;
            nextSkillsRefreshUtc = DateTime.UtcNow;
            pendingSkills.Clear();
            Provider.onServerConnected += OnServerConnected;
            Provider.onServerDisconnected += OnServerDisconnected;
            PlayerInput.onPluginKeyTick += OnVotePluginKeyTick;
            PlayerLife.OnRevived_Global += OnPlayerRevived;
            Logger.Log("[MRE] MreEssentials загружен: /vote day, автоматическое сохранение и полные навыки.");
        }

        protected override void Unload()
        {
            if (presence != null) { presence.Stop(); presence = null; }
            Provider.onServerConnected -= OnServerConnected;
            Provider.onServerDisconnected -= OnServerDisconnected;
            PlayerInput.onPluginKeyTick -= OnVotePluginKeyTick;
            PlayerLife.OnRevived_Global -= OnPlayerRevived;
            pendingSkills.Clear();
            voteInputs.Clear();
            session = null;
            finishedVote = null;
            finishedVotePhase = null;
            finishedVoteOnline = null;
            voteUiVisibleUntilUtc = DateTime.MinValue;
            control = null;
            Instance = null;
            Logger.Log("[MRE] MreEssentials выгружен. Активное голосование отменено.");
        }

        private static bool Ready()
        {
            return Provider.isServer && Level.isLoaded && Level.info != null && Level.info.type == ELevelType.SURVIVAL;
        }

        private static List<ulong> OnlinePlayers()
        {
            return Provider.clients.Where(p => p != null && p.player != null)
                .Select(p => p.playerID.steamID.m_SteamID).Distinct().ToList();
        }

        // Unity вызывает Update на главном игровом потоке; фоновых таймеров нет.
        private void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (Ready() && control != null && now >= nextControlTickUtc)
            {
                nextControlTickUtc = now.AddSeconds(0.5);
                try { control.Poll(); }
                catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка локального управления."); }
            }
            if (session != null) ResolveVote(VoteNow());
            if (now < nextTickUtc) return;
            nextTickUtc = now.AddSeconds(1);
            if (!Ready()) return;

            var c = Configuration.Instance;
            if (c.MaxSkillsEnabled)
            {
                if (!initialSkillsApplied || now >= nextSkillsRefreshUtc)
                {
                    foreach (SteamPlayer client in Provider.clients)
                        if (client != null && client.player != null) pendingSkills.Add(client.playerID.steamID.m_SteamID);
                    initialSkillsApplied = true;
                    nextSkillsRefreshUtc = now.AddSeconds(30);
                }
                foreach (ulong playerId in pendingSkills.ToArray())
                {
                    SteamPlayer client = Provider.clients.FirstOrDefault(p => p != null && p.playerID.steamID.m_SteamID == playerId);
                    if (client == null || client.player == null) { pendingSkills.Remove(playerId); continue; }
                    try
                    {
                        MaxSkillsResult result = MaxSkillsPolicy.Apply(new PlayerMaxSkillsTarget(client.player.skills));
                        if (result.AllAtMaximum) pendingSkills.Remove(playerId);
                    }
                    catch (Exception error)
                    {
                        pendingSkills.Remove(playerId);
                        Logger.LogException(error, "[MRE] Не удалось применить максимальные навыки; выполните ApplyMaxSkills после проверки.");
                    }
                }
            }
            if (c.AutoSaveEnabled && now >= nextSaveUtc)
            {
                nextSaveUtc = now.AddSeconds(c.AutoSaveIntervalSeconds);
                try { SaveManager.save(); Logger.Log("[MRE] Автосохранение мира завершено."); }
                catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка автосохранения."); }
            }
        }

        private bool ExecuteLocalCommand(string command)
        {
            if (command == "Save") { SaveManager.save(); return true; }
            if (command == "ApplyMaxSkills") return ApplyMaxSkillsAndReport();
            if (command == "Diagnostics")
            {
                object snapshot = RuntimeDiagnostics.Capture(typeof(Provider), typeof(BarricadeManager), Configuration.Instance, AppDomain.CurrentDomain.GetAssemblies());
                string target = Path.Combine(localControlDirectory, "diagnostics.json");
                string temporary = target + ".tmp";
                File.WriteAllText(temporary, RuntimeDiagnostics.ToJson(snapshot), new System.Text.UTF8Encoding(false));
                if (File.Exists(target)) File.Delete(target);
                File.Move(temporary, target);
                return true;
            }
            return Commander.execute(CSteamID.Nil, command.ToLowerInvariant());
        }

        private void OnServerConnected(CSteamID steamId)
        {
            if (Configuration.Instance.MaxSkillsEnabled) pendingSkills.Add(steamId.m_SteamID);
        }

        private void OnServerDisconnected(CSteamID steamId)
        {
            pendingSkills.Remove(steamId.m_SteamID);
            voteInputs.Remove(steamId.m_SteamID);
        }

        private void OnVotePluginKeyTick(Player player, uint simulation, byte key, bool state)
        {
            if (session == null || player == null || player.channel == null || player.channel.owner == null ||
                (key != 2 && key != 3)) return;
            ulong id = player.channel.owner.playerID.steamID.m_SteamID;
            VoteInput input;
            if (!voteInputs.TryGetValue(id, out input) || input.Player != player)
                voteInputs[id] = input = new VoteInput(player);
            if (!input.HasFrame || unchecked((int)(simulation - input.Frame)) > 0)
            {
                input.Frame = simulation; input.HasFrame = true; input.HasYes = false; input.HasNo = false;
            }
            else if (simulation != input.Frame) return;
            if (key == 2) { input.Yes = state; input.HasYes = true; }
            else { input.No = state; input.HasNo = true; }
            if (!input.HasYes || !input.HasNo) return;
            VoteHoldKeys.Choice choice = input.Hold.Observe(session, simulation, voteClock.Elapsed.TotalSeconds, input.Yes, input.No);
            if (choice == VoteHoldKeys.Choice.None) return;
            UnturnedPlayer caller = UnturnedPlayer.FromCSteamID(player.channel.owner.playerID.steamID);
            if (caller == null || caller.Player != player) return;
            HandleVoteCommand(caller, new[] { choice == VoteHoldKeys.Choice.Yes ? "yes" : "no" });
        }

        private void OnPlayerRevived(PlayerLife life)
        {
            if (!Configuration.Instance.MaxSkillsEnabled || life == null || life.player == null) return;
            SteamPlayer client = Provider.clients.FirstOrDefault(p => p != null && p.player == life.player);
            if (client != null) pendingSkills.Add(client.playerID.steamID.m_SteamID);
        }

        private bool ApplyMaxSkillsAndReport()
        {
            var players = new List<object>();
            int verified = 0;
            int errors = 0;
            foreach (SteamPlayer client in Provider.clients.ToArray())
            {
                if (client == null || client.player == null) continue;
                var player = new Dictionary<string, object>();
                player["steam_id"] = client.playerID.steamID.m_SteamID.ToString();
                try
                {
                    MaxSkillsResult result = MaxSkillsPolicy.Apply(new PlayerMaxSkillsTarget(client.player.skills));
                    player["applied"] = result.Applied;
                    player["all_at_maximum"] = result.AllAtMaximum;
                    player["skill_count"] = result.Levels.Count;
                    player["skills"] = result.Levels.Select(s => (object)new Dictionary<string, object>
                    {
                        { "speciality", s.Speciality }, { "index", s.Index }, { "level", s.Level }, { "maximum", s.Maximum }
                    }).ToArray();
                    if (result.AllAtMaximum) verified++;
                    else errors++;
                }
                catch (Exception error)
                {
                    player["error"] = error.GetType().Name + ": " + error.Message;
                    errors++;
                }
                players.Add(player);
            }
            var report = new Dictionary<string, object>
            {
                { "captured_at_utc", DateTime.UtcNow.ToString("o") },
                { "automatic_enforcement_enabled", Configuration.Instance.MaxSkillsEnabled },
                { "player_count", players.Count }, { "verified_player_count", verified },
                { "all_players_at_maximum", players.Count > 0 && errors == 0 },
                { "errors", errors }, { "players", players }
            };
            string target = Path.Combine(localControlDirectory, "max-skills.json");
            string temporary = target + ".tmp";
            File.WriteAllText(temporary, RuntimeDiagnostics.ToJson(report), new System.Text.UTF8Encoding(false));
            if (File.Exists(target)) File.Delete(target);
            File.Move(temporary, target);
            Logger.Log(String.Format("[MRE] Максимальные навыки проверены: игроков {0}, подтверждено {1}, ошибок {2}.", players.Count, verified, errors));
            return errors == 0;
        }

        public void HandleVoteCommand(IRocketPlayer caller, string[] args)
        {
            if (!Ready()) { MreChat.Say(caller, "MRE: уровень еще не готов."); return; }
            if (args.Length > 1) { MreChat.Say(caller, "Использование: /vote day, /vote yes, /vote no, /vote status или /cv."); return; }
            string action = args.Length == 0 ? "yes" : args[0].ToLowerInvariant();
            DateTime now = VoteNow();
            ResolveVote(now);

            if (action == "status")
            {
                if (session == null) MreChat.Say(caller, "MRE: активного голосования нет.");
                else MreChat.Say(caller, String.Format("MRE: за {0}/{1}, против {3}; осталось {2} с.",
                    session.CountOnlineVotes(OnlinePlayers()), session.RequiredVotes,
                    Math.Max(0, (int)Math.Ceiling((session.DeadlineUtc - now).TotalSeconds)), session.CountOnlineVotes(OnlinePlayers(), VoteChoice.No)));
                return;
            }
            if (action != "day" && action != "yes" && action != "no")
            { MreChat.Say(caller, "Использование: /vote day, /vote yes, /vote no, /vote status или /cv."); return; }
            var player = caller as UnturnedPlayer;
            if (player == null) { MreChat.Say(caller, "Голосовать могут только подключенные игроки."); return; }
            var c = Configuration.Instance;
            if (!c.VoteDayEnabled) { MreChat.Say(caller, "Голосование за день отключено."); return; }
            if (session == null)
            {
                if (action != "day") { MreChat.Say(caller, "Сейчас нет голосования. Ночью начать: /vote day."); return; }
                if (c.NightOnly && !LightingManager.isNighttime) { MreChat.Say(caller, "Уже день. Голосование доступно ночью."); return; }
                if (now < nextVoteUtc) { MreChat.Say(caller, "До следующего голосования: " + Math.Ceiling((nextVoteUtc - now).TotalSeconds) + " с."); return; }
                var online = OnlinePlayers();
                if (online.Count < c.MinimumPlayers) { MreChat.Say(caller, "Для голосования нужно игроков: " + c.MinimumPlayers + "."); return; }
                session = new VoteSession(online, c.RequiredPercent, now.AddSeconds(VoteDurationSeconds));
                voteInputs.Clear();
                voteInitiatorName = player.DisplayName;
                finishedVote = null;
                finishedVotePhase = null;
                finishedVoteOnline = null;
                voteUiVisibleUntilUtc = DateTime.MinValue;
                nextVoteUtc = now.AddSeconds(c.VoteCooldownSeconds);
                MreChat.Say(String.Format("MRE: голосование за день! Нужно {0} из {1}. Удерживайте Code Hotkey #3 (за) или #4 (против) 2 с; /vote yes и /vote no. Время: {2} с.",
                    session.RequiredVotes, session.EligibleCount, VoteDurationSeconds));
            }
            VoteResult result = session.TryVote(player.CSteamID.m_SteamID, OnlinePlayers(), now, action == "no" ? VoteChoice.No : VoteChoice.Yes);
            if (result == VoteResult.Added) MreChat.Say(caller, action == "no" ? "Ваш голос против принят." : "Ваш голос за день принят.");
            else if (result == VoteResult.Duplicate) MreChat.Say(caller, "Вы уже голосовали.");
            else if (result == VoteResult.NotEligible) MreChat.Say(caller, "Вы подключились после начала. Участвовать можно в следующем голосовании.");
            else MreChat.Say(caller, "Голос уже не может быть принят.");
            ResolveVote(now);
        }

        private void ResolveVote(DateTime now)
        {
            if (session == null || !Ready()) return;
            if (Configuration.Instance.NightOnly && !LightingManager.isNighttime)
            { FinishVoteUi("canceled", now); MreChat.Say("MRE: наступил день; голосование отменено."); return; }
            VoteOutcome outcome = session.Evaluate(OnlinePlayers(), now);
            if (outcome == VoteOutcome.Expired)
            { FinishVoteUi("expired", now); MreChat.Say("MRE: голосование отклонено: недостаточно голосов за или нет большинства."); return; }
            if (outcome != VoteOutcome.Passed) return;
            FinishVoteUi("passed", now);
            LightingManager.time = (uint)(LightingManager.cycle * LevelLighting.transition);
            MreChat.Say("MRE: голосование принято — наступил день!");
        }
    }
}
