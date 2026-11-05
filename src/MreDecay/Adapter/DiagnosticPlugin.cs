using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MreDecay.Adapter;
using Rocket.API;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Logger = Rocket.Core.Logging.Logger;

namespace MreDecay.IsolatedDiagnostic
{
    public sealed class DiagnosticConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled;
        public List<string> SafeAssetGuids;
        public void LoadDefaults() { Enabled = false; SafeAssetGuids = new List<string>(); }
    }

    // Unity invokes Update on its main thread. No commands, damage, repair or timers.
    public sealed class DiagnosticPlugin : RocketPlugin<DiagnosticConfiguration>
    {
        private bool armed;
        protected override void Load()
        {
            armed = false;
            if (Configuration.Instance == null || !Configuration.Instance.Enabled ||
                Environment.GetEnvironmentVariable("MRE_DECAY_ISOLATED_TEST") != "YES" ||
                !String.Equals(Provider.serverID, "MREDecayDiagnostic", StringComparison.Ordinal) || !ExecutableInTemp()) return;
            armed = true;
        }
        protected override void Unload() { armed = false; }

        private void Update()
        {
            if (!armed || !Provider.isServer || !Level.isLoaded) return;
            armed = false;
            try
            {
                if (Provider.clients == null || Provider.clients.Count != 0) { Logger.LogWarning("[MRE decay diagnostic] Отказ: подключены игроки или список недоступен."); return; }
                var configured = Configuration.Instance.SafeAssetGuids;
                var safes = new HashSet<Guid>();
                if (configured == null || configured.Count == 0) { Logger.LogWarning("[MRE decay diagnostic] Отказ: не задан список GUID сейфов."); return; }
                foreach (string text in configured)
                {
                    Guid id;
                    if (!Guid.TryParse(text, out id) || id == Guid.Empty || !safes.Add(id) || !(Assets.find(id) is ItemStorageAsset))
                    { Logger.LogWarning("[MRE decay diagnostic] Отказ: неполный или неверный список ассетов сейфов."); return; }
                }
                ObjectObservation[] observations;
                if (!ReadOnlyWorld.TryObserve(true, out observations)) { Logger.LogWarning("[MRE decay diagnostic] Отказ: наземное наблюдение неполно."); return; }
                int structures = 0, barricades = 0, safeCount = 0, generators = 0;
                foreach (var o in observations)
                {
                    if (o.Kind == "structure") structures++;
                    else if (o.Kind == "barricade")
                    {
                        barricades++;
                        if (safes.Contains(o.AssetGuid)) { if (!o.IsStorage) { Logger.LogWarning("[MRE decay diagnostic] Отказ: тип сейфа не совпал."); return; } safeCount++; }
                        if (o.IsGenerator) generators++;
                    }
                    else { Logger.LogWarning("[MRE decay diagnostic] Отказ: неизвестный тип объекта."); return; }
                }
                Logger.Log("[MRE decay diagnostic] Наземные регионы: structures=" + structures + " barricades=" + barricades + " configured_safes=" + safeCount + " generators=" + generators);
            }
            catch (Exception) { Logger.LogWarning("[MRE decay diagnostic] Отказ: ошибка чтения."); }
        }

        private static bool ExecutableInTemp()
        {
            try
            {
                string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string exe = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                return exe.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
    }
}
