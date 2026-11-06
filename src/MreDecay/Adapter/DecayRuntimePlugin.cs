using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MreDecay.Adapter;
using MreDecay.Engine;
using Rocket.API;
using Rocket.Core.Plugins;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreDecay.Runtime
{
    public sealed class DecayRuntimeConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled;
        public void LoadDefaults() { Enabled = false; }
    }

    // Candidate runtime shell only. The working server must not enable this plugin:
    // all production readiness adapters below intentionally fail closed until verified.
    public sealed class DecayRuntimePlugin : RocketPlugin<DecayRuntimeConfiguration>
    {
        private ProbeTimer timer;
        private DecayRuntimePipeline pipeline;
        private IDecayRuntimeAdapter adapter;
        private bool armed, started;
        private int serverThread;

        protected override void Load()
        {
            armed = false;
            started = false;
            serverThread = 0;
            if (Configuration.Instance == null || !Configuration.Instance.Enabled) return;
            timer = new ProbeTimer();
            pipeline = new DecayRuntimePipeline();
            adapter = new UnavailableRuntimeAdapter(this);
            armed = true;
            Logger.Log("[MRE decay] Включён только заблокированный runtime shell: обязательные интеграции не подтверждены.");
        }

        protected override void Unload()
        {
            armed = false;
            timer = null;
            pipeline = null;
            adapter = null;
        }

        private void Update()
        {
            if (!armed || !Provider.isServer || !Level.isLoaded) return;
            int currentThread = Thread.CurrentThread.ManagedThreadId;
            if (serverThread == 0) serverThread = currentThread;
            if (serverThread != currentThread) { armed = false; Logger.LogWarning("[MRE decay] Поток сервера изменился; runtime остановлен."); return; }
            if (timer == null || pipeline == null || adapter == null) { armed = false; return; }

            double now = Time.realtimeSinceStartupAsDouble;
            if (!started)
            {
                started = true;
                if (!timer.Start(now)) { armed = false; Logger.LogWarning("[MRE decay] Ошибка монотонных часов; runtime остановлен."); return; }
                return;
            }
            if (!timer.Poll(now))
            {
                if (timer.Faulted) { armed = false; Logger.LogWarning("[MRE decay] Ошибка таймера; runtime остановлен."); }
                return;
            }

            RuntimePipelineStatus status = pipeline.Execute(Configuration.Instance != null && Configuration.Instance.Enabled, adapter);
            if (status == RuntimePipelineStatus.BlockedByReadiness)
            {
                armed = false;
                Logger.LogWarning("[MRE decay] Нет подтверждённых зон, raid, полного обзора мира/vehicles и durable transaction; цикл остановлен до чтения и записи мира.");
            }
            else if (status == RuntimePipelineStatus.CommitFailed || pipeline.IsFaulted)
            {
                armed = false;
                Logger.LogWarning("[MRE decay] Неизвестный результат транзакции; дальнейшие записи остановлены, требуется сверка состояния.");
            }
            else if (status != RuntimePipelineStatus.Applied && status != RuntimePipelineStatus.NoChanges)
            {
                armed = false;
                Logger.LogWarning("[MRE decay] Проверка runtime snapshot/plan завершилась отказом; runtime остановлен: " + status);
            }
        }

        private sealed class UnavailableRuntimeAdapter : IDecayRuntimeAdapter
        {
            private readonly DecayRuntimePlugin plugin;
            internal UnavailableRuntimeAdapter(DecayRuntimePlugin plugin) { this.plugin = plugin; }
            public bool IsOnServerThread { get { return plugin.serverThread != 0 && Thread.CurrentThread.ManagedThreadId == plugin.serverThread; } }
            public bool IsBusy { get { return false; } }
            public bool IsFaulted { get { return !plugin.armed; } }

            // These fields are code-owned prerequisites, never configuration. Current
            // zone, raid, vehicle-completeness, durable-state and recovery adapters are absent.
            public RuntimeEvidence GetVerifiedReadiness() { return new RuntimeEvidence(); }
            public bool TryObserve(out Snapshot snapshot) { snapshot = null; return false; }
            public bool IsAllowedTarget(Building building) { return false; }
            public bool CommitCheckedAtomically(Snapshot snapshot, Plan plan) { return false; }
        }
    }
}
