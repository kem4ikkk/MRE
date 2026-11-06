// Separate isolated Rocket assembly. Never install on the working server.
using System;
using System.Diagnostics;
using System.IO;
using MreDecay.Adapter;
using Rocket.API;
using Rocket.Core.Plugins;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreDecay.IsolatedRuntime
{
    public sealed class ProbeConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled;
        public void LoadDefaults() { Enabled = false; }
    }
    public sealed class ProbePlugin : RocketPlugin<ProbeConfiguration>
    {
        private ProbeTimer timer;
        private bool armed;
        protected override void Load()
        {
            armed = false; started = false;
            if (Configuration.Instance == null || !Configuration.Instance.Enabled ||
                Environment.GetEnvironmentVariable("MRE_DECAY_PROBE") != "YES" ||
                !String.Equals(Provider.serverID, "MREDecayProbe", StringComparison.Ordinal) || !ExecutableInTemp()) return;
            timer = new ProbeTimer(); armed = true;
        }
        protected override void Unload() { armed = false; timer = null; }
        private void Update()
        {
            if (!armed || !Provider.isServer || !Level.isLoaded) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (timer == null) { armed = false; return; }
            if (!started) {
                started = true;
                if (!timer.Start(now)) { armed = false; return; }
                Logger.Log("[MRE decay probe] Изолированный режим: запись запрещена; ожидание 300 секунд.");
                return;
            }
            if (!timer.Poll(now)) { if (timer.Faulted) { armed = false; Logger.LogWarning("[MRE decay probe] Clock error; stopped."); } return; }
            try {
                ObjectObservation[] objects;
                if (Provider.clients == null || Provider.clients.Count != 0 || !ReadOnlyWorld.TryObserve(true, out objects)) {
                    armed = false; Logger.LogWarning("[MRE decay probe] Неполное наблюдение или игроки; остановлено."); return;
                }
                // No configurable switch can certify these missing guarantees.
                // No writes until verified zones, device status, raid, stable identity,
                // and atomic world/state recovery are actually integrated.
                if (ProbePlanning.Prepare(objects) != null) { armed = false; Logger.LogWarning("[MRE decay probe] Unexpected plan; stopped."); return; }
                Logger.Log("[MRE decay probe] objects=" + objects.Length + "; запись заблокирована: зоны, рейд и согласование состояния не подтверждены.");
            } catch (Exception) { armed = false; Logger.LogWarning("[MRE decay probe] Ошибка; остановлено."); }
        }
        private bool started;
        private static bool ExecutableInTemp()
        {
            try {
                string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string exe = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                return exe.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            } catch (Exception) { return false; }
        }
    }
}
