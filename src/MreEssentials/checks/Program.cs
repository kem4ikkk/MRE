using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MreEssentials;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
        checks++;
    }

    private static void Main()
    {
        DateTime now = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
        ulong[] players = { 11, 22, 33 };
        var vote = new VoteSession(players, 60, now.AddSeconds(60));
        Check(vote.RequiredVotes == 2, "60% от трех требует два голоса");
        Check(vote.TryVote(11, players, now) == VoteResult.Added, "Первый голос принят");
        Check(vote.TryVote(11, players, now) == VoteResult.Duplicate, "Повтор не считается");
        Check(vote.CountOnlineVotes(players) == 1, "Повтор не увеличивает счет");
        Check(vote.TryVote(44, new ulong[] { 11, 22, 33, 44 }, now) == VoteResult.NotEligible, "Поздний вход не меняет снимок");
        Check(vote.TryVote(22, new ulong[] { 11, 33 }, now) == VoteResult.Disconnected, "Отключенный игрок не голосует");
        Check(vote.TryVote(22, players, now) == VoteResult.Added, "Второй голос принят");
        Check(vote.Evaluate(players, now) == VoteOutcome.Active, "До срока голосование продолжается при достигнутом кворуме");
        Check(vote.Evaluate(new ulong[] { 11, 33 }, now) == VoteOutcome.Active, "Отключенный голос не считается; порог не уменьшается");
        Check(vote.RequiredVotes == 2, "Выход игроков не снижает порог");
        Check(vote.Evaluate(players, now.AddSeconds(60)) == VoteOutcome.Passed, "На границе срока кворум принят");
        Check(vote.TryVote(33, players, now.AddSeconds(60)) == VoteResult.Expired, "Просроченный голос отвергается");
        Check(new VoteSession(new ulong[] { 11, 22, 33, 44 }, 60, now).RequiredVotes == 3, "60% от четырех округляется вверх");
        Check(new VoteSession(new ulong[] { 11, 22, 33, 44, 55 }, 60, now).RequiredVotes == 3, "60% от пяти равно трем");
        Check(new VoteSession(new ulong[] { 11, 11, 22 }, 60, now).EligibleCount == 2, "Снимок не содержит повторных SteamID");
        var solo = new VoteSession(new ulong[] { 11 }, 60, now.AddSeconds(60));
        Check(solo.RequiredVotes == 1, "Один игрок требует ровно один голос");
        Check(solo.Evaluate(new ulong[] { 11 }, now) == VoteOutcome.Active, "Одиночное голосование ожидает голос");
        Check(solo.TryVote(11, new ulong[] { 11 }, now) == VoteResult.Added, "Единственный игрок может проголосовать");
        Check(solo.Evaluate(new ulong[] { 11 }, now.AddSeconds(60)) == VoteOutcome.Passed, "Одиночный голос принимается по истечении срока");
        var opposed = new VoteSession(new ulong[] { 11, 22, 33, 44 }, 50, now.AddSeconds(30));
        Check(opposed.TryVote(11, players, now, VoteChoice.No) == VoteResult.Added, "Голос против принят");
        Check(opposed.TryVote(11, players, now, VoteChoice.Yes) == VoteResult.Duplicate, "Нельзя изменить голос после переподключения");
        Check(opposed.TryVote(22, players, now, VoteChoice.Yes) == VoteResult.Added, "Голос за принят");
        Check(opposed.CountOnlineVotes(players, VoteChoice.No) == 1 && opposed.CountOnlineVotes(players) == 1, "За и против считаются отдельно");
        Check(opposed.Evaluate(players, now.AddSeconds(29.999)) == VoteOutcome.Active, "До границы 30 секунд исход активен");
        Check(opposed.Evaluate(players, now.AddSeconds(30)) == VoteOutcome.Expired, "Равенство голосов отклоняется");
        Check(opposed.TryVote(33, players, now.AddSeconds(30), VoteChoice.No) == VoteResult.Expired, "Голос на границе не принимается");
        Check(opposed.CountOnlineVotes(new ulong[] {22}, VoteChoice.No) == 0 && opposed.HasVoted(11), "Выход не сбрасывает голос, но исключает его из счета");
        Check(opposed.CountOnlineVotes(players, VoteChoice.No) == 1, "Возвращение восстанавливает исходный голос");
        var winner = new VoteSession(players, 60, now.AddSeconds(30));
        winner.TryVote(11, players, now, VoteChoice.Yes); winner.TryVote(22, players, now, VoteChoice.Yes); winner.TryVote(33, players, now, VoteChoice.No);
        Check(winner.Evaluate(players, now.AddSeconds(30)) == VoteOutcome.Passed, "Большинство и исходный кворум принимают предложение");
        Check(winner.Evaluate(new ulong[] {11,33}, now.AddSeconds(30)) == VoteOutcome.Expired, "Выход снимает голос с подсчета, но кворум фиксирован");
        bool rejected = false;
        try { new VoteSession(Array.Empty<ulong>(), 60, now); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Без игроков голосование не создается");
        rejected = false;
        try { new VoteSession(players, 49, now); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Порог ниже 50% отвергается");
        CheckDoubleTap();
        CheckHoldKeys();
        CheckBridge();
        CheckDiagnostics();
        CheckMaxSkills();
        Console.WriteLine("Проверки VoteSession, LocalControlBridge и MaxSkillsPolicy: " + checks + " успешно.");
    }

    private static void CheckDoubleTap()
    {
        var c = new object(); var other = new object(); var t = new VoteDoubleTapKeys(2,3,5,1,0.6);
        t.Observe(c,1,0,false,false); t.Observe(c,2,0.1,true,false); t.Observe(c,3,0.2,false,false);
        Check(t.Observe(c,4,0.3,true,false) == VoteDoubleTapKeys.Choice.Yes, "Два нажатия за дают голос за");
        Check(t.Observe(c,4,0.4,true,false) == VoteDoubleTapKeys.Choice.None, "Повтор кадра не голосует снова");
        t.Observe(c,5,0.4,true,false); t.Observe(c,6,0.5,false,false); t.Observe(c,7,0.6,true,false);
        Check(t.Observe(c,8,0.7,false,true) == VoteDoubleTapKeys.Choice.None, "Смена кнопки не завершает выбор");
        t.Observe(c,9,0.8,false,false); t.Observe(c,10,0.9,false,true); t.Observe(c,11,1.0,false,false);
        Check(t.Observe(c,12,1.1,false,true) == VoteDoubleTapKeys.Choice.No, "Два нажатия против дают голос против");
        t.Observe(c,13,1.2,false,false); t.Observe(c,14,1.3,true,false); t.Observe(c,15,1.4,false,false);
        Check(t.Observe(c,16,1.5,true,true) == VoteDoubleTapKeys.Choice.None, "Обе кнопки отменяют последовательность");
        Check(t.Observe(c,17,1.6,true,false) == VoteDoubleTapKeys.Choice.None, "После одновременного нажатия требуется полное отпускание");
        t.Observe(c,18,1.7,false,false); t.Observe(c,19,1.8,true,false); t.Observe(c,20,2.3,false,false);
        Check(t.Observe(c,21,2.9,true,false) == VoteDoubleTapKeys.Choice.None, "Позднее второе нажатие отклонено");
        t.Observe(c,22,3.0,false,false); t.Observe(c,23,3.1,true,false); t.Observe(c,24,3.2,false,false);
        Check(t.Observe(c,23,3.3,true,false) == VoteDoubleTapKeys.Choice.None, "Старый кадр не меняет автомат");
        Check(t.Observe(other,25,3.4,true,false) == VoteDoubleTapKeys.Choice.None, "Новая сессия требует отпускание");
        t.Observe(other,26,3.5,false,false); t.Observe(other,27,3.6,true,false); t.Reset();
        Check(t.Observe(other,28,3.7,false,false) == VoteDoubleTapKeys.Choice.None && t.Observe(other,29,3.8,true,false) == VoteDoubleTapKeys.Choice.None, "Reset удаляет первую половину последовательности");
        t.Observe(other,30,3.9,false,false); t.Observe(other,31,4.6,true,false);
        Check(t.Observe(other,32,4.7,false,false) == VoteDoubleTapKeys.Choice.None && t.Observe(other,33,4.8,true,false) == VoteDoubleTapKeys.Choice.None, "Разрыв сбрасывает последовательность");
        var wrap = new VoteDoubleTapKeys(2,3,5,1,1); wrap.Observe(c,uint.MaxValue-1,0,false,false); wrap.Observe(c,uint.MaxValue,0.1,true,false); wrap.Observe(c,0,0.2,false,false);
        Check(wrap.Observe(c,1,0.3,true,false) == VoteDoubleTapKeys.Choice.Yes, "Переход uint через ноль сохраняет порядок кадров");
    }
    private static void CheckHoldKeys()
    {
        var c = new object(); var other = new object(); var h = new VoteHoldKeys(2,3,5,2,0.6);
        Check(h.Observe(c,1,0,false,false) == VoteHoldKeys.Choice.None, "Пустой кадр разрешает новое удержание");
        h.Observe(c,2,0.1,true,false); h.Observe(c,3,0.6,true,false); h.Observe(c,4,1.1,true,false); h.Observe(c,5,1.6,true,false);
        Check(h.Observe(c,6,2.09,true,false) == VoteHoldKeys.Choice.None, "Ранее двух секунд голос не засчитывается");
        Check(h.Observe(c,7,2.1,true,false) == VoteHoldKeys.Choice.Yes, "Удержание две секунды подтверждает за");
        Check(h.Observe(c,8,2.2,true,false) == VoteHoldKeys.Choice.None, "Удержание не создаёт повторный голос");
        Check(h.Observe(c,7,2.3,false,false) == VoteHoldKeys.Choice.None, "Устаревший кадр не переоснащает таймер");
        h.Observe(c,9,2.4,false,false); h.Observe(c,10,2.5,true,true);
        h.Observe(c,11,2.6,false,true); h.Observe(c,12,3.1,false,true); h.Observe(c,13,3.6,false,true); h.Observe(c,14,4.1,false,true);
        Check(h.Observe(c,15,4.6,false,true) == VoteHoldKeys.Choice.None, "Обе клавиши блокируют выбор до отпускания");
        h.Observe(c,16,4.7,false,false); h.Observe(c,17,4.8,false,true); h.Observe(c,18,5.3,false,true);
        Check(h.Observe(c,19,6.0,false,true) == VoteHoldKeys.Choice.None, "Разрыв потока сбрасывает удержание");
        h.Observe(other,20,6.1,false,true); h.Observe(other,21,6.6,false,true); h.Observe(other,22,7.1,false,true); h.Observe(other,23,7.6,false,true);
        Check(h.Observe(other,24,8.1,false,true) == VoteHoldKeys.Choice.None, "Новая сессия не наследует нажатие");
        h.Observe(other,25,8.2,false,false); h.Observe(other,26,8.3,false,true); h.Observe(other,27,8.8,false,true); h.Observe(other,28,9.3,false,true); h.Observe(other,29,9.8,false,true);
        Check(h.Observe(other,30,10.3,false,true) == VoteHoldKeys.Choice.No, "Повторная сессия принимает удержание против");
    }
    private sealed class SkillsTarget : IMaxSkillsTarget
    {
        public List<SkillLevelState> Levels = new List<SkillLevelState>();
        public int Unlocks;
        public bool Replicate = true;
        public IEnumerable<SkillLevelState> ReadLevels() { return Levels.ToArray(); }
        public void UnlockAllAndReplicate()
        {
            Unlocks++;
            if (Replicate)
                Levels = Levels.ConvertAll(s => new SkillLevelState(s.Speciality, s.Index, s.Maximum, s.Maximum));
        }
    }

    private static void CheckMaxSkills()
    {
        var empty = new SkillsTarget();
        var emptyResult = MaxSkillsPolicy.Apply(empty);
        Check(!emptyResult.AllAtMaximum && !emptyResult.Applied && empty.Unlocks == 0, "Еще не загруженные навыки не объявляются подтвержденными");
        var current = new SkillsTarget();
        current.Levels.Add(new SkillLevelState(0, 0, 0, 5));
        current.Levels.Add(new SkillLevelState(1, 2, 3, 3));
        var result = MaxSkillsPolicy.Apply(current);
        Check(current.Unlocks == 1 && result.Applied && result.AllAtMaximum, "Недостающий навык максимизируется штатным вызовом");
        Check(result.Levels[0].Level == 5 && result.Levels[1].Level == 3, "Результат перечитывает реальные уровни после применения");
        result = MaxSkillsPolicy.Apply(current);
        Check(current.Unlocks == 1 && !result.Applied && result.AllAtMaximum, "Повторная проверка не рассылает лишнее изменение");
        current.Levels[0] = new SkillLevelState(0, 0, 1, 5);
        Check(MaxSkillsPolicy.Apply(current).AllAtMaximum && current.Unlocks == 2, "После возрождения сброшенный навык снова максимизируется");
        var failed = new SkillsTarget { Replicate = false };
        failed.Levels.Add(new SkillLevelState(0, 0, 1, 5));
        result = MaxSkillsPolicy.Apply(failed);
        Check(result.Applied && !result.AllAtMaximum && result.Levels[0].Level == 1, "Непримененные реальные уровни не объявляются успешными");
    }

    private static void CheckDiagnostics()
    {
        var configuration = new MreEssentialsConfiguration();
        configuration.LoadDefaults();
        Check(configuration.MinimumPlayers == 1 && configuration.MaxSkillsEnabled && configuration.ProtectVehicleTires, "Конфигурация по умолчанию разрешает solo vote, максимальные навыки и защиту шин");
        var snapshot = RuntimeDiagnostics.Capture(typeof(DiagnosticsFixtures.Provider), typeof(DiagnosticsFixtures.BarricadeManager), configuration, new[] { typeof(Program).Assembly });
        using var parsed = JsonDocument.Parse(RuntimeDiagnostics.ToJson(snapshot));
        var provider = parsed.RootElement.GetProperty("provider");
        Check(provider.GetProperty("mode").GetString() == "EASY", "Diagnostics читает фактический mode");
        Check(provider.GetProperty("map").GetString() == "PEI", "Diagnostics читает map");
        Check(provider.GetProperty("max_players").GetInt32() == 24, "Diagnostics читает maxPlayers");
        Check(provider.GetProperty("steam_admin_owner_id").GetString() == "123456", "Diagnostics читает фактический SteamAdminlist.ownerID");
        Check(provider.GetProperty("steam_admin_owner_has_admin").GetBoolean(), "Diagnostics проверяет native admin через checkAdmin(ownerID)");
        Check(provider.GetProperty("group_cap").GetInt32() == 4, "Diagnostics читает вложенный group cap");
        Check(parsed.RootElement.GetProperty("mre_configuration").GetProperty("RequiredPercent").GetInt32() == 60, "Diagnostics читает конфиг MRE");
        Check(parsed.RootElement.GetProperty("barricade_manager").GetProperty("server_set_bed_owner_six_parameters_exists").GetBoolean(), "Diagnostics находит приватный метод с шестью аргументами");
        Check(parsed.RootElement.GetProperty("more_homes_harmony").GetProperty("matched_method_count").GetInt32() == 3, "Diagnostics фильтрует патчи по owner contains");
        var unavailable = RuntimeDiagnostics.Capture(typeof(object), typeof(object), configuration, Array.Empty<System.Reflection.Assembly>());
        using var missing = JsonDocument.Parse(RuntimeDiagnostics.ToJson(unavailable));
        Check(missing.RootElement.GetProperty("provider").TryGetProperty("mode_error", out _), "Отсутствующее API записывается как ошибка");
        Check(missing.RootElement.GetProperty("more_homes_harmony").TryGetProperty("error", out _), "Отсутствие Harmony не вызывает падение");
        using var escaped = JsonDocument.Parse(RuntimeDiagnostics.ToJson("Кавычка\" и новая\nстрока"));
        Check(escaped.RootElement.GetString() == "Кавычка\" и новая\nстрока", "JSON экранирует специальные символы");
    }

    private static string Status(string root, string id)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "results", id + ".json")));
        return json.RootElement.GetProperty("status").GetString();
    }

    private static void CheckBridge()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "control-fixture", Guid.NewGuid().ToString("N"));
        string requests = Path.Combine(root, "requests");
        Directory.CreateDirectory(requests);
        string fakeExecutable = Path.Combine(root, "runtime", "server", "Unturned.exe");
        string expectedControl = Path.GetFullPath(Path.Combine(root, "runtime", "control", "MRE"));
        string previousCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = requests;
            Check(LocalControlBridge.ResolveDirectory(fakeExecutable, "../control/MRE") == expectedControl,
                "Путь управления вычисляется по EXE и не зависит от измененного cwd игры");
        }
        finally { Environment.CurrentDirectory = previousCwd; }
        bool outsideRejected = false;
        try { LocalControlBridge.ResolveDirectory(fakeExecutable, "../../outside"); }
        catch (InvalidOperationException) { outsideRejected = true; }
        Check(outsideRejected, "Выход из runtime/control запрещен");
        string staleId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(requests, staleId + ".cmd"), "Shutdown");
        var executed = new List<string>();
        bool shutdownAckWasReady = false;
        string shutdownId = null;
        var bridge = new LocalControlBridge(root, command =>
        {
            if (command == "Shutdown") shutdownAckWasReady = Status(root, shutdownId) == "accepted";
            executed.Add(command);
            return true;
        });
        Check(executed.Count == 0, "Старый Shutdown не выполняется при старте");
        Check(Status(root, staleId) == "discarded_on_start", "У старого запроса есть явный статус");
        string saveId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(requests, saveId + ".cmd"), "Save");
        bridge.Poll();
        Check(executed.Count == 1 && executed[0] == "Save", "Разрешенный Save исполняется");
        Check(Status(root, saveId) == "completed", "Save получает подтверждение после исполнения");
        File.WriteAllText(Path.Combine(requests, saveId + ".cmd"), "Save");
        bridge.Poll();
        Check(executed.Count == 1, "Повтор обработанного GUID не исполняется");
        string invalidId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(requests, invalidId + ".cmd"), "give 11");
        bridge.Poll();
        Check(Status(root, invalidId) == "rejected" && executed.Count == 1, "Произвольная консольная команда запрещена");
        string combinedId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(requests, combinedId + ".cmd"), "Save\nShutdown");
        bridge.Poll();
        Check(Status(root, combinedId) == "rejected" && executed.Count == 1, "Несколько команд в запросе запрещены");
        string bigId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(requests, bigId + ".cmd"), new string('X', 100));
        bridge.Poll();
        Check(Status(root, bigId) == "rejected", "Слишком большой запрос запрещен");
        string diagnosticsId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(requests, diagnosticsId + ".cmd"), "Diagnostics");
        bridge.Poll();
        Check(executed.Count == 2 && executed[1] == "Diagnostics", "Diagnostics входит в локальный allowlist");
        Check(Status(root, diagnosticsId) == "completed", "Diagnostics получает подтверждение после выполнения");
        string maxSkillsId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(requests, maxSkillsId + ".cmd"), "ApplyMaxSkills");
        bridge.Poll();
        Check(executed.Count == 3 && executed[2] == "ApplyMaxSkills", "ApplyMaxSkills входит в локальный allowlist");
        Check(Status(root, maxSkillsId) == "completed", "Проверка навыков подтверждается после выполнения");
        shutdownId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(requests, shutdownId + ".cmd"), "Shutdown");
        bridge.Poll();
        Check(shutdownAckWasReady, "Shutdown подтвержден до исполнения");
        Check(Status(root, shutdownId) == "accepted", "Shutdown не объявляет завершение до выхода процесса");
        Check(Directory.GetFiles(requests, "*.processing").Length == 0, "Обработанные промежуточные запросы удалены");
    }
}
