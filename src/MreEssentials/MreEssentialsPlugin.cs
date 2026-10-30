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
        private VoteSession session;
        private DateTime nextVoteUtc;
        private DateTime nextSaveUtc;
        private DateTime nextTickUtc;
        private DateTime nextControlTickUtc;
        private LocalControlBridge control;
        private string localControlDirectory;

        protected override void Load()
        {
            var c = Configuration.Instance;
            if (c.MinimumPlayers < 2 || c.RequiredPercent < 50 || c.RequiredPercent > 100 ||
                c.VoteDurationSeconds < 10 || c.VoteDurationSeconds > 600 ||
                c.VoteCooldownSeconds < c.VoteDurationSeconds || c.AutoSaveIntervalSeconds < 30)
                throw new InvalidOperationException("Некорректная конфигурация MreEssentials. Проверьте minimum/percent/duration/cooldown/autosave.");
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
            Instance = this;
            Logger.Log("[MRE] MreEssentials загружен: /vote day и автоматическое сохранение.");
        }

        protected override void Unload()
        {
            session = null;
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
            if (now < nextTickUtc) return;
            nextTickUtc = now.AddSeconds(1);
            if (!Ready()) return;

            var c = Configuration.Instance;
            if (c.AutoSaveEnabled && now >= nextSaveUtc)
            {
                nextSaveUtc = now.AddSeconds(c.AutoSaveIntervalSeconds);
                try { SaveManager.save(); Logger.Log("[MRE] Автосохранение мира завершено."); }
                catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка автосохранения."); }
            }
            ResolveVote(now);
        }

        private bool ExecuteLocalCommand(string command)
        {
            if (command == "Save") { SaveManager.save(); return true; }
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

        public void HandleVoteCommand(IRocketPlayer caller, string[] args)
        {
            if (!Ready()) { UnturnedChat.Say(caller, "MRE: уровень еще не готов."); return; }
            if (args.Length > 1) { UnturnedChat.Say(caller, "Использование: /vote day, /vote yes, /vote status или /cv."); return; }
            string action = args.Length == 0 ? "yes" : args[0].ToLowerInvariant();
            DateTime now = DateTime.UtcNow;
            ResolveVote(now);

            if (action == "status")
            {
                if (session == null) UnturnedChat.Say(caller, "MRE: активного голосования нет.");
                else UnturnedChat.Say(caller, String.Format("MRE: за день {0}/{1}; осталось {2} с.",
                    session.CountOnlineVotes(OnlinePlayers()), session.RequiredVotes,
                    Math.Max(0, (int)Math.Ceiling((session.DeadlineUtc - now).TotalSeconds))));
                return;
            }
            if (action != "day" && action != "yes")
            { UnturnedChat.Say(caller, "Использование: /vote day, /vote yes, /vote status или /cv."); return; }
            var player = caller as UnturnedPlayer;
            if (player == null) { UnturnedChat.Say(caller, "Голосовать могут только подключенные игроки."); return; }
            var c = Configuration.Instance;
            if (!c.VoteDayEnabled) { UnturnedChat.Say(caller, "Голосование за день отключено."); return; }
            if (session == null)
            {
                if (action != "day") { UnturnedChat.Say(caller, "Сейчас нет голосования. Ночью начать: /vote day."); return; }
                if (c.NightOnly && !LightingManager.isNighttime) { UnturnedChat.Say(caller, "Уже день. Голосование доступно ночью."); return; }
                if (now < nextVoteUtc) { UnturnedChat.Say(caller, "До следующего голосования: " + Math.Ceiling((nextVoteUtc - now).TotalSeconds) + " с."); return; }
                var online = OnlinePlayers();
                if (online.Count < c.MinimumPlayers) { UnturnedChat.Say(caller, "Для голосования нужно игроков: " + c.MinimumPlayers + "."); return; }
                session = new VoteSession(online, c.RequiredPercent, now.AddSeconds(c.VoteDurationSeconds));
                nextVoteUtc = now.AddSeconds(c.VoteCooldownSeconds);
                UnturnedChat.Say(String.Format("MRE: начато голосование за день! Нужно {0} из {1}. Голос: /vote yes или /cv. Время: {2} с.",
                    session.RequiredVotes, session.EligibleCount, c.VoteDurationSeconds));
            }
            VoteResult result = session.TryVote(player.CSteamID.m_SteamID, OnlinePlayers(), now);
            if (result == VoteResult.Added) UnturnedChat.Say(caller, "Ваш голос за день принят.");
            else if (result == VoteResult.Duplicate) UnturnedChat.Say(caller, "Вы уже голосовали.");
            else if (result == VoteResult.NotEligible) UnturnedChat.Say(caller, "Вы подключились после начала. Участвовать можно в следующем голосовании.");
            else UnturnedChat.Say(caller, "Голос уже не может быть принят.");
            ResolveVote(now);
        }

        private void ResolveVote(DateTime now)
        {
            if (session == null || !Ready()) return;
            if (Configuration.Instance.NightOnly && !LightingManager.isNighttime)
            { session = null; UnturnedChat.Say("MRE: наступил день; голосование отменено."); return; }
            VoteOutcome outcome = session.Evaluate(OnlinePlayers(), now);
            if (outcome == VoteOutcome.Expired)
            { session = null; UnturnedChat.Say("MRE: голосование завершено без кворума."); return; }
            if (outcome != VoteOutcome.Passed) return;
            session = null;
            LightingManager.time = (uint)(LightingManager.cycle * LevelLighting.transition);
            UnturnedChat.Say("MRE: голосование принято — наступил день!");
        }
    }
}
