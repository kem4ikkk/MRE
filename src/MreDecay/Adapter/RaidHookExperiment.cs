// Isolated fixture only: no production raid decisions.
using System;
using HarmonyLib;
using MreDecay.Adapter;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace MreDecay.IsolatedFixture
{
    internal static class RaidHookExperiment
    {
        internal sealed class State { internal object Target; internal int Before; internal CSteamID Attacker; internal DamageScopeLedger.Scope Scope; internal bool Ended; }
        internal static DamageScopeLedger Ledger = new DamageScopeLedger();
        internal static int Confirmed, Cancelled;
        private static Harmony patcher;
        internal static void Install()
        {
            patcher = new Harmony("mre.decay.fixture.postdamage");
            var b = typeof(BarricadeManager).GetMethod("damage", new[] { typeof(Transform), typeof(float), typeof(float), typeof(bool), typeof(CSteamID), typeof(EDamageOrigin) });
            var s = typeof(StructureManager).GetMethod("damage", new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(float), typeof(bool), typeof(CSteamID), typeof(EDamageOrigin) });
            if (b == null || s == null) throw new InvalidOperationException("Missing damage methods");
            try {
            patcher.Patch(b, prefix: new HarmonyMethod(typeof(RaidHookExperiment), nameof(BeforeBarricade)), postfix: new HarmonyMethod(typeof(RaidHookExperiment), nameof(AfterBarricade)), finalizer: new HarmonyMethod(typeof(RaidHookExperiment), nameof(FinalizeCall)));
            patcher.Patch(s, prefix: new HarmonyMethod(typeof(RaidHookExperiment), nameof(BeforeStructure)), postfix: new HarmonyMethod(typeof(RaidHookExperiment), nameof(AfterStructure)), finalizer: new HarmonyMethod(typeof(RaidHookExperiment), nameof(FinalizeCall)));
            } catch { Uninstall(); throw; }
        }
        internal static void Uninstall() { if (patcher != null) patcher.UnpatchAll("mre.decay.fixture.postdamage"); patcher = null; }
        private static void BeforeBarricade(Transform __0, CSteamID __4, out State __state)
        {
            __state = null;
            var drop = BarricadeManager.FindBarricadeByRootTransform(__0);
            var data = drop == null ? null : drop.GetServersideData();
            if (data != null && data.barricade != null) {
                var target = data.barricade;
                __state = new State { Target = target, Before = target.health, Attacker = __4, Scope = Ledger.Begin(target, target.health, __4.m_SteamID == 76561198000000000UL) };
            } else Ledger.Invalidate();
        }
        private static void AfterBarricade(State __state)
        {
            if (__state == null || __state.Attacker.m_SteamID != 76561198000000000UL) return;
            int after = ((Barricade)__state.Target).health;
            var result = Ledger.End(__state.Scope, __state.Target, after, true, after > 0);
            __state.Ended = true;
            if (result == DamageScopeLedger.Result.PlayerDamage) Confirmed++;
            else if (result == DamageScopeLedger.Result.NoPlayerDamage) Cancelled++;
        }
        private static void BeforeStructure(Transform __0, CSteamID __5, out State __state)
        {
            __state = null;
            var drop = StructureManager.FindStructureByRootTransform(__0);
            var data = drop == null ? null : drop.GetServersideData();
            if (data != null && data.structure != null) {
                var target = data.structure;
                __state = new State { Target = target, Before = target.health, Attacker = __5, Scope = Ledger.Begin(target, target.health, __5.m_SteamID == 76561198000000000UL) };
            } else Ledger.Invalidate();
        }
        private static void AfterStructure(State __state)
        {
            if (__state == null || __state.Attacker.m_SteamID != 76561198000000000UL) return;
            int after = ((Structure)__state.Target).health;
            var result = Ledger.End(__state.Scope, __state.Target, after, true, after > 0);
            __state.Ended = true;
            if (result == DamageScopeLedger.Result.PlayerDamage) Confirmed++;
            else if (result == DamageScopeLedger.Result.NoPlayerDamage) Cancelled++;
        }
        private static Exception FinalizeCall(Exception __exception, State __state)
        {
            if (__exception != null || (__state != null && !__state.Ended)) Ledger.Invalidate();
            return __exception;
        }
    }
}
