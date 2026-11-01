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
        Check(vote.Evaluate(players, now) == VoteOutcome.Passed, "Кворум достигнут");
        Check(vote.Evaluate(new ulong[] { 11, 33 }, now) == VoteOutcome.Active, "Отключенный голос не считается; порог не уменьшается");
        Check(vote.RequiredVotes == 2, "Выход игроков не снижает порог");
        Check(vote.Evaluate(players, now.AddSeconds(60)) == VoteOutcome.Expired, "Граница срока закрывает голосование");
        Check(vote.TryVote(33, players, now.AddSeconds(60)) == VoteResult.Expired, "Просроченный голос отвергается");
        Check(new VoteSession(new ulong[] { 11, 22, 33, 44 }, 60, now).RequiredVotes == 3, "60% от четырех округляется вверх");
        Check(new VoteSession(new ulong[] { 11, 22, 33, 44, 55 }, 60, now).RequiredVotes == 3, "60% от пяти равно трем");
        Check(new VoteSession(new ulong[] { 11, 11, 22 }, 60, now).EligibleCount == 2, "Снимок не содержит повторных SteamID");
        var solo = new VoteSession(new ulong[] { 11 }, 60, now.AddSeconds(60));
        Check(solo.RequiredVotes == 1, "Один игрок требует ровно один голос");
        Check(solo.Evaluate(new ulong[] { 11 }, now) == VoteOutcome.Active, "Одиночное голосование ожидает голос");
        Check(solo.TryVote(11, new ulong[] { 11 }, now) == VoteResult.Added, "Единственный игрок может проголосовать");
        Check(solo.Evaluate(new ulong[] { 11 }, now) == VoteOutcome.Passed, "Одиночный голос завершает голосование");
        bool rejected = false;
        try { new VoteSession(Array.Empty<ulong>(), 60, now); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Без игроков голосование не создается");
        rejected = false;
        try { new VoteSession(players, 49, now); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Порог ниже 50% отвергается");
        CheckBridge();
        CheckDiagnostics();
        Console.WriteLine("Проверки VoteSession и LocalControlBridge: " + checks + " успешно.");
    }

    private static void CheckDiagnostics()
    {
        var configuration = new MreEssentialsConfiguration();
        configuration.LoadDefaults();
        var snapshot = RuntimeDiagnostics.Capture(typeof(DiagnosticsFixtures.Provider), typeof(DiagnosticsFixtures.BarricadeManager), configuration, new[] { typeof(Program).Assembly });
        using var parsed = JsonDocument.Parse(RuntimeDiagnostics.ToJson(snapshot));
        var provider = parsed.RootElement.GetProperty("provider");
        Check(provider.GetProperty("mode").GetString() == "EASY", "Diagnostics читает фактический mode");
        Check(provider.GetProperty("map").GetString() == "PEI", "Diagnostics читает map");
        Check(provider.GetProperty("max_players").GetInt32() == 24, "Diagnostics читает maxPlayers");
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
        shutdownId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(requests, shutdownId + ".cmd"), "Shutdown");
        bridge.Poll();
        Check(shutdownAckWasReady, "Shutdown подтвержден до исполнения");
        Check(Status(root, shutdownId) == "accepted", "Shutdown не объявляет завершение до выхода процесса");
        Check(Directory.GetFiles(requests, "*.processing").Length == 0, "Обработанные промежуточные запросы удалены");
    }
}
