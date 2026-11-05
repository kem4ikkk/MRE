using System;
using System.Diagnostics;
using MreShared;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Logger = Rocket.Core.Logging.Logger;

namespace MreTips
{
    public sealed class MreTipsPlugin : RocketPlugin<MreTipsConfiguration>
    {
        private static MreTipsPlugin owner;
        private TipSchedule schedule;
        private static double Now { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }

        protected override void Load()
        {
            if (owner != null && owner != this) throw new InvalidOperationException("MreTips уже загружен.");
            if (!Configuration.Instance.Enabled) { Logger.Log("[MRE] Подсказки отключены."); return; }
            schedule = new TipSchedule(Configuration.Instance.IntervalSeconds, Configuration.Instance.Messages, Now);
            owner = this;
            Logger.Log("[MRE] Подсказки включены.");
        }

        protected override void Unload()
        {
            schedule = null;
            if (owner == this) owner = null;
        }

        private void Update()
        {
            if (owner != this || schedule == null) return;
            string message = schedule.Poll(Now, Provider.isServer && Level.isLoaded && Provider.clients != null && Provider.clients.Count > 0);
            if (message == null) return;
            try { MreChat.Say(message); }
            catch (Exception error) { Logger.LogException(error, "[MRE] Не удалось отправить подсказку."); }
        }
    }
}
