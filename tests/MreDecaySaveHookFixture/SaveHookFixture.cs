// Test-only diagnostic. It observes SaveManager callbacks and performs one
// ordinary save command; it does not change world state or infer durability.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using Rocket.API;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Logger = Rocket.Core.Logging.Logger;
using MreDecay.Adapter;

namespace MreDecay.SaveHookFixture
{
    public sealed class SaveHookFixtureConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled;
        public string ServerId;
        public string RunToken;
        public void LoadDefaults() { Enabled = false; ServerId = "MREDecaySaveHookFixture"; RunToken = ""; }
    }

    public sealed class SaveHookFixture : RocketPlugin<SaveHookFixtureConfiguration>
    {
        private const string ExpectedServerId = "MREDecaySaveHookFixture";
        private const string OptInVariable = "MRE_DECAY_SAVE_HOOK_FIXTURE";
        private const string Marker = "[MRE save hook fixture]";
        private bool armed;
        private bool requested;
        private bool shutdownRequested;
        private bool shutdownAwaiting;
        private bool subscribed;
        private int sequence;
        private int preCount;
        private int postCount;
        private int serverUpdateThreadId;
        private double shutdownAt;
        private double shutdownDeadline;
        private string serverDirectory;
        private string worldSaveDirectory;
        private string markerPath;
        private string stageReceiptPath;
        private string phase = "startup";
        private bool recordingFailed;
        private readonly object recordLock = new object();
        private FileStream markerStream;

        protected override void Load()
        {
            try
            {
                if (Configuration.Instance == null || !Configuration.Instance.Enabled ||
                    !String.Equals(Configuration.Instance.ServerId, ExpectedServerId, StringComparison.Ordinal) ||
                    !ValidRunToken(Configuration.Instance.RunToken) ||
                    !String.Equals(Environment.GetEnvironmentVariable(OptInVariable), Configuration.Instance.RunToken, StringComparison.Ordinal) ||
                    !String.Equals(Provider.serverID, ExpectedServerId, StringComparison.Ordinal) || !ExecutableUnderTemp())
                {
                    Logger.Log(Marker + " REFUSED default-off or TEMP/server identity guard");
                    return;
                }

                string executable = Path.GetFullPath(ProcessPath());
                string root = Path.GetFullPath(UnturnedPaths.RootDirectory.FullName);
                if (!String.Equals(root, Path.GetDirectoryName(executable), StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(root, Path.GetFullPath(Environment.CurrentDirectory), StringComparison.OrdinalIgnoreCase) || !UnderTemp(root))
                { Logger.Log(Marker + " REFUSED TEMP executable, root and working directory mismatch"); return; }
                string serverSavedataRelative = ServerSavedata.directory;
                if (String.IsNullOrWhiteSpace(serverSavedataRelative)) { Logger.Log(Marker + " REFUSED empty savedata directory"); return; }
                serverSavedataRelative = serverSavedataRelative.TrimStart('/', '\\');
                if (serverSavedataRelative != "Servers") { Logger.Log(Marker + " REFUSED unexpected savedata directory"); return; }
                string saveBase = Path.GetFullPath(Path.Combine(root, serverSavedataRelative));
                if (!Under(root, saveBase) || !UnderTemp(saveBase)) { Logger.Log(Marker + " REFUSED savedata root outside TEMP instance"); return; }
                string map = Provider.map;
                if (!ValidMapName(map)) { Logger.Log(Marker + " REFUSED unsafe or unknown map name"); return; }
                serverDirectory = Path.GetFullPath(Path.Combine(saveBase, ExpectedServerId, "Server"));
                worldSaveDirectory = Path.GetFullPath(Path.Combine(saveBase, ExpectedServerId, "Level", map));
                if (!Under(root, serverDirectory) || !Under(root, worldSaveDirectory) || !UnderTemp(serverDirectory) || !UnderTemp(worldSaveDirectory) ||
                    !System.IO.Directory.Exists(serverDirectory) || !NoReparsePath(serverDirectory) ||
                    !System.IO.Directory.Exists(worldSaveDirectory) || !NoReparsePath(worldSaveDirectory))
                { Logger.Log(Marker + " REFUSED server or map save directory missing/outside TEMP instance"); return; }
                stageReceiptPath = Path.Combine(serverDirectory, "mre-save-hook-fixture-stage.txt");
                if (!NoReparsePath(stageReceiptPath) || !ValidStageReceipt(Configuration.Instance.RunToken, map))
                { Logger.Log(Marker + " REFUSED missing or invalid fresh TEMP stage receipt"); return; }
                markerPath = Path.Combine(serverDirectory, "mre-save-hook-fixture.log");
                if (File.Exists(markerPath)) { Logger.Log(Marker + " REFUSED marker already exists; fixture is one-shot"); return; }
                markerStream = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                armed = true;
                Record("LOAD_ARMED", null);
            }
            catch (Exception ex) { armed = false; Logger.Log(Marker + " REFUSED load guard exception=" + ex.GetType().Name); }
        }

        protected override void Unload()
        {
            Detach();
            armed = false;
            if (markerStream != null) { markerStream.Dispose(); markerStream = null; }
        }

        private void Update()
        {
            if (!armed || !Provider.isServer || !Level.isLoaded) return;
            try
            {
                Interlocked.CompareExchange(ref serverUpdateThreadId, Thread.CurrentThread.ManagedThreadId, 0);
                if (Provider.clients == null || Provider.clients.Count != 0)
                { Record("REFUSED clients missing or nonempty", null); Detach(); armed = false; return; }

                if (!requested)
                {
                    ObjectObservation[] current;
                    if (!ReadOnlyWorld.TryObserve(true, out current) || current == null || current.Length != 0)
                    { Record("REFUSED world observation incomplete or nonempty", null); Detach(); armed = false; return; }
                }

                if (shutdownRequested)
                {
                    if (Time.realtimeSinceStartupAsDouble < shutdownAt) return;
                    shutdownRequested = false;
                    phase = "shutdown";
                    Record("SHUTDOWN_COMMAND_BEFORE", null);
                    bool accepted = Commander.execute(Steamworks.CSteamID.Nil, "shutdown");
                    Record("SHUTDOWN_COMMAND_RETURN", "accepted=" + accepted.ToString());
                    if (!accepted) { Record("FAILED shutdown command not accepted", null); Detach(); armed = false; return; }
                    shutdownAwaiting = true;
                    shutdownDeadline = Time.realtimeSinceStartupAsDouble + 60.0;
                    return;
                }

                if (shutdownAwaiting)
                {
                    if (Time.realtimeSinceStartupAsDouble < shutdownDeadline) return;
                    Record("FAILED shutdown did not complete before timeout", null);
                    Detach(); armed = false;
                    return;
                }

                if (requested) return;
                requested = true;
                phase = "manual-save";
                SaveManager.onPreSave += OnPreSave;
                SaveManager.onPostSave += OnPostSave;
                subscribed = true;
                Record("HOOKS_ATTACHED", null);
                Record("MANUAL_SAVE_BEFORE", null);
                bool commandAccepted = Commander.execute(Steamworks.CSteamID.Nil, "save");
                Record("MANUAL_SAVE_RETURN", "accepted=" + commandAccepted.ToString());
                if (!commandAccepted) { Record("FAILED save command not accepted", null); Detach(); armed = false; return; }
                shutdownAt = Time.realtimeSinceStartupAsDouble + 5.0;
                shutdownRequested = true;
            }
            catch (Exception ex)
            {
                RecordSafely("FAILED exception=" + ex.GetType().Name, null);
                Detach(); armed = false;
            }
        }

        private void OnPreSave()
        {
            int count = Interlocked.Increment(ref preCount);
            RecordSafely("PRE_SAVE_CALLBACK", "count=" + count.ToString(CultureInfo.InvariantCulture));
        }

        private void OnPostSave()
        {
            int count = Interlocked.Increment(ref postCount);
            RecordSafely("POST_SAVE_CALLBACK", "count=" + count.ToString(CultureInfo.InvariantCulture));
        }

        private void Detach()
        {
            if (!subscribed) return;
            SaveManager.onPreSave -= OnPreSave;
            SaveManager.onPostSave -= OnPostSave;
            subscribed = false;
            RecordSafely("HOOKS_DETACHED", null);
        }

        private void RecordSafely(string name, string detail)
        {
            try { Record(name, detail); }
            catch (Exception ex)
            {
                recordingFailed = true;
                try { Logger.Log(Marker + " MARKER_WRITE_FAILED event=" + name + " exception=" + ex.GetType().Name); } catch { }
            }
        }

        private void Record(string name, string detail)
        {
            lock (recordLock)
            {
                int number = Interlocked.Increment(ref sequence);
                if (recordingFailed) throw new IOException("earlier marker write failed");
                string structure = HashOrMissing("Structures.dat");
                string barricade = HashOrMissing("Barricades.dat");
                string line = String.Join("|", "v1", number.ToString(CultureInfo.InvariantCulture),
                    Stopwatch.GetTimestamp().ToString(CultureInfo.InvariantCulture),
                    Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture),
                    "update_thread=" + Interlocked.CompareExchange(ref serverUpdateThreadId, 0, 0).ToString(CultureInfo.InvariantCulture),
                    "server_id=" + ExpectedServerId, "map=" + Provider.map, "run_token=" + Configuration.Instance.RunToken, phase,
                    name, detail ?? "", "Structures.dat=" + structure, "Barricades.dat=" + barricade);
                byte[] bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                if (markerStream == null) throw new IOException("marker stream not open");
                markerStream.Write(bytes, 0, bytes.Length);
                markerStream.Flush(true);
                Logger.Log(Marker + " " + line);
            }
        }

        private string HashOrMissing(string name)
        {
            string path = Path.Combine(worldSaveDirectory, name);
            try
            {
                if (!File.Exists(path)) return "MISSING";
                if (!NoReparsePath(path)) return "REPARSE_REFUSED";
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
            catch (Exception ex) { return "ERROR_" + ex.GetType().Name; }
        }

        private static bool ExecutableUnderTemp()
        {
            try
            {
                string path = ProcessPath();
                return !String.IsNullOrWhiteSpace(path) && UnderTemp(Path.GetFullPath(path)) && NoReparsePath(Path.GetFullPath(path));
            }
            catch { return false; }
        }

        private static string ProcessPath()
        {
            using (Process process = Process.GetCurrentProcess()) return process.MainModule == null ? null : process.MainModule.FileName;
        }

        private static bool UnderTemp(string path)
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Under(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValidMapName(string map)
        {
            return !String.IsNullOrWhiteSpace(map) && map != "." && map != ".." &&
                String.Equals(Path.GetFileName(map), map, StringComparison.Ordinal) &&
                map.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && map.IndexOf('/') < 0 && map.IndexOf('\\') < 0;
        }

        private static bool ValidRunToken(string token)
        {
            Guid parsed;
            return !String.IsNullOrWhiteSpace(token) && Guid.TryParseExact(token, "N", out parsed) && parsed != Guid.Empty;
        }

        private bool ValidStageReceipt(string token, string map)
        {
            try
            {
                if (!File.Exists(stageReceiptPath) || !NoReparsePath(stageReceiptPath)) return false;
                string[] lines = File.ReadAllLines(stageReceiptPath, Encoding.UTF8);
                return lines.Length == 5 && lines[0] == "MRE_SAVE_HOOK_FIXTURE_V1" &&
                    lines[1] == "ServerId=" + ExpectedServerId && lines[2] == "BuildId=26491864" &&
                    lines[3] == "Map=" + map && lines[4] == "RunToken=" + token;
            }
            catch { return false; }
        }

        private static bool NoReparsePath(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetPathRoot(full);
                if (String.IsNullOrWhiteSpace(root)) return false;
                string current = root;
                FileAttributes rootAttributes = File.GetAttributes(current);
                if ((rootAttributes & FileAttributes.ReparsePoint) != 0) return false;
                string relative = full.Substring(root.Length);
                foreach (string part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, part);
                    if (!File.Exists(current) && !System.IO.Directory.Exists(current)) return false;
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                }
                return true;
            }
            catch { return false; }
        }
    }
}
