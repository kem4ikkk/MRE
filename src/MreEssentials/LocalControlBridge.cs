using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MreEssentials
{
    // Локальный файловый канал с фиксированным набором команд; сетевого слушателя нет.
    public sealed class LocalControlBridge
    {
        private readonly string requestsDirectory;
        private readonly string resultsDirectory;
        private readonly Func<string, bool> execute;
        private readonly UTF8Encoding utf8 = new UTF8Encoding(false);

        public static string ResolveDirectory(string executablePath, string configuredDirectory)
        {
            string gameRoot = Path.GetDirectoryName(Path.GetFullPath(executablePath));
            string allowedRoot = Path.GetFullPath(Path.Combine(gameRoot, "..", "control"));
            string controlRoot = Path.GetFullPath(Path.Combine(gameRoot, configuredDirectory));
            if (!controlRoot.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("LocalControlDirectory должен находиться внутри runtime/control относительно каталога Unturned.exe.");
            return controlRoot;
        }

        public LocalControlBridge(string rootDirectory, Func<string, bool> executeCommand)
        {
            requestsDirectory = Path.Combine(rootDirectory, "requests");
            resultsDirectory = Path.Combine(rootDirectory, "results");
            execute = executeCommand;
            RejectLinkedAncestors(rootDirectory);
            Directory.CreateDirectory(requestsDirectory);
            Directory.CreateDirectory(resultsDirectory);
            RejectReparsePoint(rootDirectory);
            RejectReparsePoint(requestsDirectory);
            RejectReparsePoint(resultsDirectory);
            foreach (string path in Directory.EnumerateFiles(requestsDirectory).ToArray())
            {
                string id;
                if (!TryRequestId(path, out id)) continue;
                WriteResult(id, "discarded_on_start", "None");
                File.Delete(path);
            }
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Локальный control-каталог не должен быть ссылкой.");
        }

        private static void RejectLinkedAncestors(string path)
        {
            for (DirectoryInfo directory = new DirectoryInfo(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Родительский control-каталог не должен быть ссылкой.");
        }

        private static bool TryRequestId(string path, out string id)
        {
            id = null;
            string extension = Path.GetExtension(path);
            if (extension != ".cmd" && extension != ".processing") return false;
            Guid parsed;
            string name = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(name, "N", out parsed)) return false;
            id = parsed.ToString("N");
            return true;
        }

        private static string NormalizeCommand(string text)
        {
            foreach (string allowed in new[] { "Save", "Shutdown", "Players", "Day", "Night", "Diagnostics" })
                if (String.Equals(text.Trim(), allowed, StringComparison.OrdinalIgnoreCase)) return allowed;
            return null;
        }

        private void WriteResult(string id, string status, string command)
        {
            string destination = Path.Combine(resultsDirectory, id + ".json");
            string temporary = destination + ".tmp";
            string json = "{\"id\":\"" + id + "\",\"status\":\"" + status +
                "\",\"command\":\"" + command + "\",\"at_utc\":\"" + DateTime.UtcNow.ToString("o") + "\"}";
            File.WriteAllText(temporary, json, utf8);
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(temporary, destination);
        }

        public void Poll()
        {
            RejectLinkedAncestors(requestsDirectory);
            RejectLinkedAncestors(resultsDirectory);
            RejectReparsePoint(requestsDirectory);
            RejectReparsePoint(resultsDirectory);
            foreach (string request in Directory.EnumerateFiles(requestsDirectory, "*.cmd").OrderBy(p => p).Take(8).ToArray())
            {
                string id;
                if (!TryRequestId(request, out id)) continue;
                RejectReparsePoint(request);
                if (File.Exists(Path.Combine(resultsDirectory, id + ".json")))
                {
                    File.Delete(request); // Уже обработанный GUID не исполняется повторно.
                    continue;
                }
                string claimed = Path.Combine(requestsDirectory, id + ".processing");
                File.Move(request, claimed);
                try
                {
                    if (new FileInfo(claimed).Length > 64)
                    { WriteResult(id, "rejected", "Invalid"); continue; }
                    string command = NormalizeCommand(File.ReadAllText(claimed, utf8));
                    if (command == null) { WriteResult(id, "rejected", "Invalid"); continue; }
                    if (command == "Shutdown") WriteResult(id, "accepted", command);
                    bool completed = execute(command);
                    if (command != "Shutdown" || !completed)
                        WriteResult(id, completed ? "completed" : "failed", command);
                    if (command == "Shutdown") return;
                }
                catch (Exception)
                {
                    WriteResult(id, "failed", "Invalid");
                    throw;
                }
                finally
                {
                    if (File.Exists(claimed)) File.Delete(claimed);
                }
            }
        }
    }
}
