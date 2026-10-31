using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreInterface
{
    // amount в doDamage уже учитывает броню. Публикуем только после подтверждения
    // изменения HP в нативном обработчике, до вызова death/revive подписчиков.
    internal sealed class DamageObserver : IDisposable
    {
        private sealed class Scope
        {
            public PlayerLife Victim;
            public DamageCapture Capture;
            public CSteamID Attacker;
            public EDeathCause Cause;
            public ELimb Limb;
        }
        private static DamageObserver active;
        private readonly MreInterfacePlugin plugin;
        private readonly List<Scope> scopes = new List<Scope>();
        private Harmony harmony;
        private MethodInfo patchedMethod;
        public bool Enabled { get; private set; }
        internal DamageObserver(MreInterfacePlugin plugin) { this.plugin = plugin; }
        internal void Start()
        {
            patchedMethod = typeof(PlayerLife).GetMethod("doDamage", BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(byte), typeof(Vector3), typeof(EDeathCause), typeof(ELimb), typeof(CSteamID), typeof(EPlayerKill).MakeByRefType(), typeof(bool), typeof(ERagdollEffect), typeof(bool) }, null);
            if (patchedMethod == null) { Logger.LogWarning("[MRE] Сигнатура PlayerLife.doDamage изменилась; события урона отключены до проверки SDK."); return; }
            active = this;
            harmony = new Harmony("mre.interface.damage.v1");
            harmony.Patch(patchedMethod,
                new HarmonyMethod(typeof(DamageObserver), "BeforeDamage") { priority = Priority.Last },
                new HarmonyMethod(typeof(DamageObserver), "AfterDamage"),
                finalizer: new HarmonyMethod(typeof(DamageObserver), "OnException"));
            PlayerLife.OnTellHealth_Global += OnHealth;
            Enabled = true;
        }
        private static void BeforeDamage(PlayerLife __instance, byte amount, EDeathCause newCause, ELimb newLimb, CSteamID newKiller, out object __state)
        {
            __state = null;
            if (active == null || __instance.isDead || amount == 0) return;
            Scope scope = new Scope { Victim = __instance, Capture = new DamageCapture(__instance.health), Cause = newCause, Limb = newLimb, Attacker = newKiller };
            active.scopes.Add(scope); __state = scope;
        }
        private void OnHealth(PlayerLife life)
        {
            for (int i = scopes.Count - 1; i >= 0; i--)
                if (scopes[i].Victim == life) { scopes[i].Capture.Observe(life.health); return; }
        }
        private static void AfterDamage(object __state)
        {
            Scope scope = __state as Scope;
            if (active == null || scope == null) return;
            active.scopes.Remove(scope);
            if (scope.Capture.Damage > 0) active.plugin.RecordDamage(scope.Victim, scope.Attacker, scope.Cause, scope.Limb, scope.Capture.Damage);
        }
        private static Exception OnException(Exception __exception, object __state)
        {
            Scope scope = __state as Scope;
            if (active != null && scope != null) active.scopes.Remove(scope);
            return __exception;
        }
        public void Dispose()
        {
            PlayerLife.OnTellHealth_Global -= OnHealth;
            if (harmony != null && patchedMethod != null) harmony.Unpatch(patchedMethod, HarmonyPatchType.All, harmony.Id);
            scopes.Clear(); Enabled = false;
            if (active == this) active = null;
        }
    }
}
