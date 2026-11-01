using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreHud
{
    // Read-only postfix: результат уже извлечен и проверен игровым getInput.
    internal sealed class PunchInputObserver : IDisposable
    {
        private const string HarmonyId = "mre.hud.punch-input.v1";
        private const string VerifiedGameHash = "15666f1e20afc32a01a5510eb71eef2512a6eb4064ee628b806ee333ab4e4660";
        private static PunchInputObserver active;
        private readonly MreHudPlugin plugin;
        private Harmony harmony;
        private MethodInfo target;

        internal PunchInputObserver(MreHudPlugin plugin) { this.plugin = plugin; }

        internal void Start()
        {
            try
            {
                string hash;
                using (var stream = File.OpenRead(typeof(PlayerInput).Assembly.Location))
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (hash != VerifiedGameHash) { Logger.LogWarning("[MRE UI] Версия игры не проверена для перехвата удара; доступен штатный damage-event."); return; }
                target = typeof(PlayerInput).GetMethod("getInput", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(bool), typeof(ERaycastInfoUsage), typeof(Vector3?) }, null);
                if (target == null || target.ReturnType != typeof(InputInfo)) { Logger.LogWarning("[MRE UI] Сигнатура getInput изменилась; перехват удара отключен."); return; }
                harmony = new Harmony(HarmonyId);
                active = this;
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(PunchInputObserver), "AfterInput") { priority = Priority.Last });
                Logger.Log("[MRE UI] Наблюдение штатного ввода удара включено.");
            }
            catch (Exception error)
            {
                Dispose();
                Logger.LogException(error, "[MRE UI] Наблюдение удара отключено; штатный HUD и damage-event сохранены.");
            }
        }

        private static void AfterInput(PlayerInput __instance, bool __0, ERaycastInfoUsage __1, Vector3? __2, InputInfo __result)
        {
            try
            {
                if (active == null || !Provider.isServer || !__0 || __1 != ERaycastInfoUsage.Punch || __2.HasValue || __result == null) return;
                active.plugin.ObservePunchInput(__instance, __result);
            }
            catch (Exception error) { Logger.LogException(error, "[MRE UI] Ошибка чтения удара; игровой ввод не изменен."); }
        }

        public void Dispose()
        {
            if (active == this) active = null;
            try { if (harmony != null && target != null) harmony.Unpatch(target, HarmonyPatchType.All, HarmonyId); }
            catch (Exception error) { Logger.LogException(error, "[MRE UI] Не удалось снять собственный перехват удара."); }
            harmony = null; target = null;
        }
    }
}
