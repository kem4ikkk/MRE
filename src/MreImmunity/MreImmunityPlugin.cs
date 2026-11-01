using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Rocket.Core.Plugins;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreImmunity
{
    public sealed class MreImmunityPlugin : RocketPlugin<MreImmunityConfiguration>
    {
        private const string Id = "mre.immunity.v1";
        private const string GameHash = "15666f1e20afc32a01a5510eb71eef2512a6eb4064ee628b806ee333ab4e4660";
        private static MreImmunityPlugin active;
        private readonly SyncGate<PlayerLife> gate = new SyncGate<PlayerLife>();
        private readonly HashSet<PlayerLife> seen = new HashSet<PlayerLife>();
        private Harmony harmony;
        private MethodInfo infect, radiate;
        private float nextScan;
        private float syncSeconds;

        protected override void Load()
        {
            var c = Configuration.Instance;
            if (!c.Enabled || !c.Verified) { Logger.Log("[MreImmunity] Отключён до проверки и явного включения."); return; }
            if (float.IsNaN(c.RadiationSyncSeconds) || c.RadiationSyncSeconds < 1f || c.RadiationSyncSeconds > 10f)
            { Logger.LogError("[MreImmunity] Интервал должен быть 1–10 секунд; плагин отключён."); return; }
            try
            {
                string hash;
                using (var stream = File.OpenRead(typeof(PlayerLife).Assembly.Location))
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (hash != GameHash) throw new InvalidOperationException("Непроверенная версия Assembly-CSharp.dll.");
                infect = Target("askInfect"); radiate = Target("askRadiate");
                syncSeconds = c.RadiationSyncSeconds;
                harmony = new Harmony(Id);
                harmony.Patch(infect, prefix: new HarmonyMethod(typeof(MreImmunityPlugin), "SkipInfect"));
                harmony.Patch(radiate, prefix: new HarmonyMethod(typeof(MreImmunityPlugin), "SkipRadiate"));
                active = this;
                PlayerLife.OnRevived_Global += OnRevived;
                Logger.Log("[MreImmunity] Включён; клиентская радиация синхронизируется с ограничением частоты.");
            }
            catch (Exception e) { Cleanup(); Logger.LogException(e, "[MreImmunity] Не включён; штатная механика сохранена."); }
        }

        private static MethodInfo Target(string name)
        {
            var method = typeof(PlayerLife).GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(byte) }, null);
            if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(typeof(PlayerLife).FullName, name);
            return method;
        }
        private static bool SkipInfect() { return active == null || !Provider.isServer; }
        private static bool SkipRadiate(PlayerLife __instance, byte __0)
        {
            var plugin = active;
            if (plugin == null || !Provider.isServer) return true;
            try
            {
                if (__instance != null && !__instance.isDead && __0 != 0 && plugin.gate.ShouldSend(__instance, Time.realtimeSinceStartup, plugin.syncSeconds))
                    __instance.askDisinfect(100);
            }
            catch (Exception e) { Logger.LogException(e, "[MreImmunity] Ошибка синхронизации радиации."); }
            return false;
        }
        private void OnRevived(PlayerLife life)
        {
            if (!Provider.isServer || life == null) return;
            try { gate.Remove(life); life.askDisinfect(100); }
            catch (Exception e) { Logger.LogException(e, "[MreImmunity] Ошибка восстановления после возрождения."); }
        }
        private void Update()
        {
            if (active != this || !Provider.isServer || !Level.isLoaded || Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 1f;
            var online = new HashSet<PlayerLife>(Provider.clients.Where(x => x != null && x.player != null && x.player.life != null).Select(x => x.player.life));
            foreach (var life in seen.Where(x => !online.Contains(x)).ToArray()) { seen.Remove(life); gate.Remove(life); }
            foreach (var life in online)
            {
                if (life.isDead) continue;
                // Новое подключение синхронизируем, низкое состояние исправляем даже при записи сторонним кодом.
                if (!seen.Contains(life) || life.virus < 100)
                {
                    try { life.askDisinfect(100); seen.Add(life); }
                    catch (Exception e) { Logger.LogException(e, "[MreImmunity] Ошибка восстановления иммунитета."); }
                }
            }
        }
        protected override void Unload() { Cleanup(); }
        private void Cleanup()
        {
            active = null;
            PlayerLife.OnRevived_Global -= OnRevived;
            try
            {
                if (harmony != null && infect != null) harmony.Unpatch(infect, HarmonyPatchType.All, Id);
                if (harmony != null && radiate != null) harmony.Unpatch(radiate, HarmonyPatchType.All, Id);
            }
            catch (Exception e) { Logger.LogException(e, "[MreImmunity] Не удалось снять перехват; перезапустите сервер."); }
            harmony = null; seen.Clear(); gate.Clear();
        }
    }
}
