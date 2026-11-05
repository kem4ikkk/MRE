using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreHud
{
    // Наблюдаем только проверенную DLL; никакие аргументы и результаты игры не изменяются.
    internal sealed class DamageNumberObserver : IDisposable
    {
        private const string Id = "mre.hud.damage-number.v1";
        private const string GameHash = "15666f1e20afc32a01a5510eb71eef2512a6eb4064ee628b806ee333ab4e4660";
        private static DamageNumberObserver active;
        private readonly DamageTargetSequence sequences = new DamageTargetSequence();
        private readonly MreHudPlugin plugin;
        private Harmony harmony;
        private readonly List<State> playerScopes = new List<State>();
        private MethodInfo playerMethod, resourceMethod, barricadeMethod, structureMethod, vehicleMethod;
        private sealed class State { public int Before; public long Sequence; public object Target; public CSteamID Attacker; public ResourceSpawnpoint Resource; public PlayerLife Victim; public int After; public DamageKind Kind; public bool HealthObserved; public Func<int> Read; }
        internal DamageNumberObserver(MreHudPlugin plugin) { this.plugin = plugin; }
        internal void Start()
        {
            if (active != null) throw new InvalidOperationException("DamageNumberObserver уже запущен.");
            using (var stream = File.OpenRead(typeof(PlayerLife).Assembly.Location))
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != GameHash)
                { Logger.LogWarning("[MRE UI] Версия игры не проверена: индикация урона выключена."); return; }
            playerMethod = typeof(PlayerLife).GetMethod("askDamage", new[] { typeof(byte), typeof(Vector3), typeof(EDeathCause), typeof(ELimb), typeof(CSteamID), typeof(EPlayerKill).MakeByRefType(), typeof(bool), typeof(ERagdollEffect), typeof(bool), typeof(bool) });
            resourceMethod = typeof(ResourceManager).GetMethod("damage", new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(EPlayerKill).MakeByRefType(), typeof(uint).MakeByRefType(), typeof(CSteamID), typeof(EDamageOrigin), typeof(bool) });
            barricadeMethod = typeof(BarricadeManager).GetMethod("damage", new[] { typeof(Transform), typeof(float), typeof(float), typeof(bool), typeof(CSteamID), typeof(EDamageOrigin) });
            structureMethod = typeof(StructureManager).GetMethod("damage", new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(float), typeof(bool), typeof(CSteamID), typeof(EDamageOrigin) });
            vehicleMethod = typeof(VehicleManager).GetMethod("damage", new[] { typeof(InteractableVehicle), typeof(float), typeof(float), typeof(bool), typeof(CSteamID), typeof(EDamageOrigin) });
            if (playerMethod == null || resourceMethod == null || barricadeMethod == null || structureMethod == null || vehicleMethod == null) throw new InvalidOperationException("Сигнатуры урона отличаются от проверенных.");
            harmony = new Harmony(Id);
            try
            {
                active = this;
                harmony.Patch(playerMethod, prefix: new HarmonyMethod(typeof(DamageNumberObserver), "BeforePlayer") { priority = Priority.Last }, postfix: new HarmonyMethod(typeof(DamageNumberObserver), "AfterPlayer") { priority = Priority.Last }, finalizer: new HarmonyMethod(typeof(DamageNumberObserver), "FinishPlayer"));
                PlayerLife.OnTellHealth_Global += OnPlayerHealth;
                harmony.Patch(resourceMethod, prefix: new HarmonyMethod(typeof(DamageNumberObserver), "BeforeResource") { priority = Priority.Last }, postfix: new HarmonyMethod(typeof(DamageNumberObserver), "AfterResource") { priority = Priority.Last });
                harmony.Patch(barricadeMethod, prefix: new HarmonyMethod(typeof(DamageNumberObserver), "BeforeBarricade") { priority = Priority.Last }, postfix: new HarmonyMethod(typeof(DamageNumberObserver), "AfterObject") { priority = Priority.Last });
                harmony.Patch(structureMethod, prefix: new HarmonyMethod(typeof(DamageNumberObserver), "BeforeStructure") { priority = Priority.Last }, postfix: new HarmonyMethod(typeof(DamageNumberObserver), "AfterObject") { priority = Priority.Last });
                harmony.Patch(vehicleMethod, prefix: new HarmonyMethod(typeof(DamageNumberObserver), "BeforeVehicle") { priority = Priority.Last }, postfix: new HarmonyMethod(typeof(DamageNumberObserver), "AfterObject") { priority = Priority.Last });
            }
            catch { Dispose(); throw; }
        }
        private static void BeforePlayer(PlayerLife __instance, ELimb __3, CSteamID __4, out State __state)
        {
            __state = null;
            try { if (active != null && Provider.isServer && __instance != null) { __state = new State { Before = __instance.health, Attacker = __4, Sequence = active.sequences.Begin(__instance), Target = __instance, Victim = __instance, Kind = __3 == ELimb.SKULL ? DamageKind.PlayerHead : DamageKind.PlayerBody }; active.playerScopes.Add(__state); } }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка наблюдения урона игроку."); }
        }
        private static void AfterPlayer(PlayerLife __instance, State __state)
        {
            try
            {
                if (active == null || __state == null || !active.sequences.IsCurrent(__state.Target, __state.Sequence) || __instance == null || !__state.HealthObserved) return;
                int delta = DamageNumber.Calculate(__state.Before, __state.After, false);
                if (delta > 0 && __instance.channel.owner.playerID.steamID != __state.Attacker) active.plugin.ObserveDamage(__state.Attacker, delta, __state.Kind);
            }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка индикации урона игроку."); }
        }
        private void OnPlayerHealth(PlayerLife victim)
        {
            try
            {
                for (int i = playerScopes.Count - 1; i >= 0; i--)
                {
                    State state = playerScopes[i];
                    if (state.Victim != victim) continue;
                    if (!state.HealthObserved) { state.After = victim.health; state.HealthObserved = true; }
                    return;
                }
            }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка фиксации здоровья цели."); }
        }
        private static Exception FinishPlayer(Exception __exception, State __state)
        {
            try { if (active != null && __state != null) active.playerScopes.Remove(__state); }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка завершения наблюдения урона."); }
            return __exception;
        }
        private static void BeforeResource(Transform __0, CSteamID __7, out State __state)
        {
            __state = null;
            try
            {
                if (active == null || !Provider.isServer || __0 == null) return;
                byte x, y;
                if (!Regions.tryGetCoordinate(__0.position, out x, out y)) return;
                var resources = LevelGround.GetTreesOrNullInRegion(x, y);
                if (resources == null) return;
                foreach (var resource in resources)
                    if (resource != null && resource.model == __0) { __state = new State { Resource = resource, Before = resource.health, Attacker = __7, Sequence = active.sequences.Begin(resource), Target = resource, Kind = DamageKind.Resource }; return; }
            }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка наблюдения урона ресурсу."); }
        }
        private static void AfterResource(State __state)
        {
            try
            {
                if (active == null || __state == null || !active.sequences.IsCurrent(__state.Target, __state.Sequence) || __state.Resource == null) return;
                int delta = DamageNumber.Calculate(__state.Before, __state.Resource.health, false);
                if (delta > 0) active.plugin.ObserveDamage(__state.Attacker, delta, __state.Kind);
            }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка индикации урона ресурсу."); }
        }
        private static State ObjectState(CSteamID attacker, object target, Func<int> read)
        {
            return new State { Attacker = attacker, Before = read(), Read = read, Sequence = active.sequences.Begin(target), Target = target, Kind = DamageKind.Building };
        }
        private static void BeforeBarricade(Transform __0, CSteamID __4, out State __state)
        {
            __state = null;
            try
            {
                if (active == null || !Provider.isServer || __0 == null) return;
                var drop = BarricadeManager.FindBarricadeByRootTransform(__0);
                var data = drop == null ? null : drop.GetServersideData();
                if (data == null || data.barricade == null) return;
                var target = data.barricade;
                __state = ObjectState(__4, target, () => target.health);
            }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка наблюдения баррикады."); }
        }
        private static void BeforeStructure(Transform __0, CSteamID __5, out State __state)
        {
            __state = null;
            try
            {
                if (active == null || !Provider.isServer || __0 == null) return;
                var drop = StructureManager.FindStructureByRootTransform(__0);
                var data = drop == null ? null : drop.GetServersideData();
                if (data == null || data.structure == null) return;
                var target = data.structure;
                __state = ObjectState(__5, target, () => target.health);
            }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка наблюдения постройки."); }
        }
        private static void BeforeVehicle(InteractableVehicle __0, CSteamID __4, out State __state)
        {
            __state = null;
            try
            {
                if (active == null || !Provider.isServer || __0 == null) return;
                __state = ObjectState(__4, __0, () => __0.health);
            }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка наблюдения транспорта."); }
        }
        private static void AfterObject(State __state)
        {
            try
            {
                if (active == null || __state == null || !active.sequences.IsCurrent(__state.Target, __state.Sequence)) return;
                int delta = DamageNumber.Calculate(__state.Before, __state.Read(), false);
                if (delta > 0) active.plugin.ObserveDamage(__state.Attacker, delta, __state.Kind);
            }
            catch (Exception e) { Logger.LogException(e, "[MRE UI] Ошибка индикации урона объекту."); }
        }
        public void Dispose()
        {
            PlayerLife.OnTellHealth_Global -= OnPlayerHealth;
            playerScopes.Clear();
            if (active == this) active = null;
            if (harmony != null)
            {
                try { if (playerMethod != null) harmony.Unpatch(playerMethod, HarmonyPatchType.All, Id); if (resourceMethod != null) harmony.Unpatch(resourceMethod, HarmonyPatchType.All, Id); if (barricadeMethod != null) harmony.Unpatch(barricadeMethod, HarmonyPatchType.All, Id); if (structureMethod != null) harmony.Unpatch(structureMethod, HarmonyPatchType.All, Id); if (vehicleMethod != null) harmony.Unpatch(vehicleMethod, HarmonyPatchType.All, Id); }
                catch (Exception e) { Logger.LogException(e, "[MRE UI] Не удалось снять наблюдение урона."); }
            }
            harmony = null; playerMethod = null; resourceMethod = null;
        }
    }
}
