using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MreDecay.Engine
{
    // Offline TEMP-only fingerprint utility; caller must stop/quiesce the server.
    // A hash does not prove the snapshot is complete or saved durably.
    public static class IsolatedWorldFingerprint
    {
        private static readonly string[] Files = { "Barricades.dat", "Structures.dat" };
        public static byte[] Compute(string directory)
        {
            try {
                if (String.IsNullOrEmpty(directory)) return null;
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string path = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!path.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(path) || HasReparse(path)) return null;
                using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
                    foreach (string name in Files) {
                        string file = Path.Combine(path, name);
                        if (!File.Exists(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) return null;
                        var info = new FileInfo(file); if (info.Length <= 0 || info.Length > 512L * 1024 * 1024) return null;
                        byte[] hash; long length;
                        using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                        using (var sha = SHA256.Create()) { length = input.Length; hash = sha.ComputeHash(input); if (input.Length != length) return null; }
                        writer.Write(name); writer.Write(length); writer.Write(hash);
                    }
                    writer.Flush(); using (var sha = SHA256.Create()) return sha.ComputeHash(stream.ToArray());
                }
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { return null; }
        }
        private static bool HasReparse(string path) {
            var current = new DirectoryInfo(path);
            while (current != null) {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
                current = current.Parent;
            }
            return false;
        }
    }
}
