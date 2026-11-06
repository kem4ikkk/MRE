using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using MreShared;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreSpawnProtection
{
    public sealed class MreSpawnProtectionPlugin : RocketPlugin
    {
        private const string PatchId = "mre.spawnprotection.v1";
        private const string GameHash = "15666f1e20afc32a01a5510eb71eef2512a6eb4064ee628b806ee333ab4e4660";
        private const double Duration = 15;
        public static MreSpawnProtectionPlugin Instance { get; private set; }
        private readonly ProtectionTimer timer = new ProtectionTimer();
        private readonly Dictionary<ulong, Player> tracked = new Dictionary<ulong, Player>();
        private Harmony harmony;
        private static FieldInfo attachmentsField;
        private static double Now { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }
        private static ulong Id(Player player) { return player != null && player.channel != null && player.channel.owner != null ? player.channel.owner.playerID.steamID.m_SteamID : 0; }
        public double GetRemainingSeconds(Player player) { Player session; ulong id = Id(player); return player == null || player.life == null || player.life.isDead || !tracked.TryGetValue(id, out session) || !ProtectionPolicy.SameSession(session, player) ? 0 : timer.Remaining(id, Now); }
        public bool IsProtected(Player player) { return GetRemainingSeconds(player) > 0; }
        protected override void Load()
        {
            try
            {
                string hash;
                using (var f = File.OpenRead(typeof(PlayerLife).Assembly.Location)) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                if (hash != GameHash) throw new InvalidOperationException("Непроверенная версия Assembly-CSharp.dll.");
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                var damage = typeof(PlayerLife).GetMethod("askDamage", flags, null, new[] { typeof(byte), typeof(Vector3), typeof(EDeathCause), typeof(ELimb), typeof(CSteamID), typeof(EPlayerKill).MakeByRefType(), typeof(bool), typeof(ERagdollEffect), typeof(bool), typeof(bool) }, null);
                var direct = typeof(PlayerLife).GetMethod("doDamage", flags, null, new[] { typeof(byte), typeof(Vector3), typeof(EDeathCause), typeof(ELimb), typeof(CSteamID), typeof(EPlayerKill).MakeByRefType(), typeof(bool), typeof(ERagdollEffect), typeof(bool) }, null);
                var tool = typeof(DamageTool).GetMethod("damagePlayer", flags, null, new[] { typeof(DamagePlayerParameters), typeof(EPlayerKill).MakeByRefType() }, null);
                var input = typeof(PlayerEquipment).GetMethod("simulate", flags, null, new[] { typeof(uint), typeof(EAttackInputFlags), typeof(EAttackInputFlags), typeof(bool) }, null);
                var jab = typeof(UseableGun).GetMethod("jab", flags, null, Type.EmptyTypes, null);
                var interact = typeof(UseableGun).GetMethod("askInteractGun", flags, null, Type.EmptyTypes, null);
                var breakLegs = typeof(PlayerLife).GetMethod("breakLegs", flags, null, Type.EmptyTypes, null);
                attachmentsField = typeof(UseableGun).GetField("thirdAttachments", flags);
                if (damage == null || direct == null || tool == null || input == null || jab == null || interact == null || breakLegs == null || attachmentsField == null || attachmentsField.FieldType != typeof(Attachments)) throw new MissingMethodException("Изменились сигнатуры защиты возрождения.");
                harmony = new Harmony(PatchId);
                harmony.Patch(damage, prefix: new HarmonyMethod(typeof(MreSpawnProtectionPlugin), "BeforeLife") { priority = Priority.First });
                harmony.Patch(direct, prefix: new HarmonyMethod(typeof(MreSpawnProtectionPlugin), "BeforeLife") { priority = Priority.First });
                harmony.Patch(tool, prefix: new HarmonyMethod(typeof(MreSpawnProtectionPlugin), "BeforeTool") { priority = Priority.First });
                harmony.Patch(input, prefix: new HarmonyMethod(typeof(MreSpawnProtectionPlugin), "BeforeInput") { priority = Priority.First });
                harmony.Patch(interact, prefix: new HarmonyMethod(typeof(MreSpawnProtectionPlugin), "BeforeInteract") { priority = Priority.First });
                harmony.Patch(breakLegs, prefix: new HarmonyMethod(typeof(MreSpawnProtectionPlugin), "BeforeBreakLegs") { priority = Priority.First });
                harmony.Patch(jab, prefix: new HarmonyMethod(typeof(MreSpawnProtectionPlugin), "BeforeJab") { priority = Priority.First });
                Instance = this;
                PlayerLife.OnRevived_Global += OnRevived;
                PlayerLife.onPlayerDied += OnDeath;
                Provider.onServerDisconnected += OnDisconnect;
                Logger.Log("[MRE] Защита после возрождения включена на 15 секунд.");
            }
            catch (Exception e) { Cleanup(); Logger.LogException(e, "[MRE] Защита возрождения отключена из-за ошибки."); }
        }
        private static bool BeforeLife(PlayerLife __instance, EDeathCause __2, ref EPlayerKill __5)
        {
            if (!Provider.isServer || Instance == null || __2 == EDeathCause.SUICIDE || __instance == null || !Instance.IsProtected(__instance.player)) return true;
            __5 = EPlayerKill.NONE; return false;
        }
        private static bool BeforeTool(DamagePlayerParameters __0, ref EPlayerKill __1)
        {
            if (!Provider.isServer || Instance == null) return true;
            var attacker = PlayerTool.getPlayer(__0.killer);
            if (attacker != null && attacker != __0.player && Instance.IsProtected(attacker)) Instance.End(attacker, "Защита после возрождения снята: вы атаковали.");
            if (__0.player == null || __0.cause == EDeathCause.SUICIDE || !Instance.IsProtected(__0.player)) return true;
            __1 = EPlayerKill.NONE; return false;
        }
        private static void BeforeInput(PlayerEquipment __instance, EAttackInputFlags __1, EAttackInputFlags __2)
        {
            if (!Provider.isServer || Instance == null || __instance == null || !Instance.IsProtected(__instance.player)) return;
            bool primary = (__1 & EAttackInputFlags.Start) != 0;
            bool secondary = (__2 & EAttackInputFlags.Start) != 0;
            var item = __instance.useable;
            AttackItem kind = item == null ? AttackItem.Empty : item is UseableGun ? AttackItem.Gun : item is UseableMelee ? AttackItem.Melee : item is UseableThrowable ? AttackItem.Throwable : item is UseableDetonator ? AttackItem.Detonator : AttackItem.Other;
            if (ProtectionPolicy.IsAttack(kind, primary, secondary))
                Instance.End(__instance.player, "Защита после возрождения снята: вы попытались атаковать.");
        }
        private static bool BeforeBreakLegs(PlayerLife __instance)
        {
            return !Provider.isServer || Instance == null || __instance == null || !Instance.IsProtected(__instance.player);
        }
        private static void BeforeInteract(UseableGun __instance)
        {
            if (!Provider.isServer || Instance == null || __instance == null || !Instance.IsProtected(__instance.player)) return;
            var attachments = attachmentsField.GetValue(__instance) as Attachments;
            if (attachments != null && attachments.tacticalAsset != null && attachments.tacticalAsset.isMelee)
                Instance.End(__instance.player, "Защита после возрождения снята: вы попытались атаковать.");
        }
        private static void BeforeJab(UseableGun __instance)
        {
            if (Provider.isServer && Instance != null && __instance != null && Instance.IsProtected(__instance.player))
                Instance.End(__instance.player, "Защита после возрождения снята: вы попытались атаковать.");
        }
        private void OnRevived(PlayerLife life)
        {
            if (!Provider.isServer || life == null || life.player == null) return;
            ulong id = Id(life.player); if (id == 0) return;
            timer.Start(id, Now, Duration); tracked[id] = life.player;
            Say(life.player, "Защита после возрождения на 15 секунд. Попытка атаки снимет защиту.");
        }
        private void OnDeath(PlayerLife life, EDeathCause cause, ELimb limb, CSteamID killer) { if (life != null) Remove(Id(life.player)); }
        private void OnDisconnect(CSteamID id) { Remove(id.m_SteamID); }
        private void Remove(ulong id) { timer.Remove(id); tracked.Remove(id); }
        private void End(Player player, string message) { ulong id = Id(player); if (!timer.Remove(id)) return; tracked.Remove(id); Say(player, message); }
        private static void Say(Player player, string message) { if (player != null) MreChat.SayServer(UnturnedPlayer.FromPlayer(player), message); }
        private void Update()
        {
            if (Instance != this || tracked.Count == 0) return;
            var finished = new List<Player>();
            var stale = new List<ulong>();
            foreach (var pair in tracked) {
                if (!ProtectionPolicy.SameSession(pair.Value, PlayerTool.getPlayer(new CSteamID(pair.Key))) || pair.Value == null || pair.Value.life == null || pair.Value.life.isDead) stale.Add(pair.Key);
                else if (timer.Remaining(pair.Key, Now) <= 0) finished.Add(pair.Value);
            }
            foreach (var id in stale) Remove(id);
            foreach (var player in finished) End(player, "Защита после возрождения закончилась.");
        }
        protected override void Unload() { Cleanup(); }
        private void Cleanup()
        {
            Instance = null;
            PlayerLife.OnRevived_Global -= OnRevived;
            PlayerLife.onPlayerDied -= OnDeath;
            Provider.onServerDisconnected -= OnDisconnect;
            if (harmony != null) { try { harmony.UnpatchAll(PatchId); } catch (Exception e) { Logger.LogException(e, "[MRE] Ошибка снятия перехватов защиты."); } }
            harmony = null; attachmentsField = null; timer.Clear(); tracked.Clear();
        }
    }
}
