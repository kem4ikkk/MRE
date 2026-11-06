using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MreEssentials;
using RestoreMonarchy.Teleportation;
using RestoreMonarchy.Teleportation.Utils;
using Rocket.Core.Plugins;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreHud
{
    public sealed class MreHudPlugin : RocketPlugin<MreHudConfiguration>
    {
        private sealed class View
        {
            public Player Player;
            public bool Spawned;
            public bool Failed;
            public BuildingHitState BuildingHit;
            public bool BuildingRestart;
            public readonly DamageSlots Damage = new DamageSlots(8);
            public readonly Dictionary<string, MeterAnimation> Meters = new Dictionary<string, MeterAnimation>();
            public readonly Dictionary<string, int> SegmentCounts = new Dictionary<string, int>();
            public float LastMeterTime;
            public bool MeterTimeValid;
            public EPluginWidgetFlags NativeFlagsChanged;
            public readonly Dictionary<string, string> Text = new Dictionary<string, string>();
            public readonly Dictionary<string, bool> Visibility = new Dictionary<string, bool>();
            public DateTime NextStatsUtc;
        }

        private readonly Dictionary<ulong, View> views = new Dictionary<ulong, View>();
        private DateTime nextTickUtc;
        private DateTime nextAssetCheckUtc;
        private TeleportationPlugin teleportation;
        private EffectAsset effect;
        private bool waitingLogged;
        private IDisposable punchObserver;
        private IDisposable damageObserver;
        private DateTime nextStatsDiscoveryUtc;
        private PropertyInfo statsInstance, statsKills, statsDeaths, statsKd, statsZombies;
        private MethodInfo statsGetSnapshot;
        private Type statsSnapshotType;
        private DateTime nextProtectionDiscoveryUtc;
        private PropertyInfo protectionInstance;
        private MethodInfo protectionRemaining;
        private static readonly EPluginWidgetFlags[] ReplacedFlags = {
            EPluginWidgetFlags.ShowHealth, EPluginWidgetFlags.ShowFood, EPluginWidgetFlags.ShowWater,
            EPluginWidgetFlags.ShowVirus, EPluginWidgetFlags.ShowStamina, EPluginWidgetFlags.ShowOxygen,
            EPluginWidgetFlags.ShowUseableGunStatus
        };

        protected override void Load()
        {
            var c = Configuration.Instance;
            Guid guid;
            if (!Guid.TryParse(c.EffectGuid, out guid) || guid == Guid.Empty || c.EffectId == 0 ||
                c.EffectKey < 1 || float.IsNaN(c.RefreshIntervalSeconds) ||
                c.RefreshIntervalSeconds < 0.1f || c.RefreshIntervalSeconds > 1f)
                throw new InvalidOperationException("MreHud: проверьте GUID, ID/key и интервал 0.1–1 секунды.");
            BarricadeManager.onDamageBarricadeRequested += OnBarricadeHit;
            StructureManager.onDamageStructureRequested += OnStructureHit;
            try { punchObserver = StartPunchObserver(this); }
            catch (Exception error) { Logger.LogException(error, "[MRE UI] Harmony недоступен; остаются штатный HUD и damage-event."); }
            try { damageObserver = StartDamageObserver(this); }
            catch (Exception error) { Logger.LogException(error, "[MRE UI] Индикация урона отключена."); }
            Logger.Log("[MRE UI] HUD загружен. AssetsVerified=" + c.AssetsVerified +
                ". До проверки клиентского bundle штатные показатели сохраняются.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IDisposable StartDamageObserver(MreHudPlugin plugin)
        {
            var observer = new DamageNumberObserver(plugin);
            observer.Start();
            return observer;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IDisposable StartPunchObserver(MreHudPlugin plugin)
        {
            var observer = new PunchInputObserver(plugin);
            observer.Start();
            return observer;
        }

        protected override void Unload()
        {
            if (damageObserver != null) damageObserver.Dispose();
            damageObserver = null;
            if (punchObserver != null) punchObserver.Dispose();
            punchObserver = null;
            BarricadeManager.onDamageBarricadeRequested -= OnBarricadeHit;
            StructureManager.onDamageStructureRequested -= OnStructureHit;
            if (teleportation != null) teleportation.RequestStateChanged -= OnRequestChanged;
            foreach (var view in views.Values) Restore(view);
            views.Clear();
            teleportation = null;
            effect = null;
        }

        // Только главный Unity-поток. На сеть отправляются различия, а не весь HUD каждый кадр.
        private void Update()
        {
            if (DateTime.UtcNow < nextTickUtc) return;
            nextTickUtc = DateTime.UtcNow.AddSeconds(Configuration.Instance.RefreshIntervalSeconds);
            if (!Provider.isServer || !Level.isLoaded) return;
            if (teleportation != TeleportationPlugin.Instance)
            {
                if (teleportation != null) teleportation.RequestStateChanged -= OnRequestChanged;
                teleportation = TeleportationPlugin.Instance;
                if (teleportation != null) teleportation.RequestStateChanged += OnRequestChanged;
            }

            var c = Configuration.Instance;
            if (!c.Enabled || !c.AssetsVerified)
            {
                foreach (var view in views.Values) Restore(view);
                views.Clear();
                return;
            }
            if (DateTime.UtcNow >= nextAssetCheckUtc)
            {
                nextAssetCheckUtc = DateTime.UtcNow.AddSeconds(5);
                var byGuid = Assets.find(Guid.Parse(c.EffectGuid)) as EffectAsset;
                effect = byGuid != null && byGuid.id == c.EffectId ? byGuid : null;
                if (effect == null && !waitingLogged)
                {
                    waitingLogged = true;
                    Logger.LogWarning("[MRE UI] Нужный EffectAsset не найден по ID+GUID. Штатный HUD сохранён.");
                }
            }
            if (effect == null)
            {
                foreach (var view in views.Values) Restore(view);
                views.Clear();
                return;
            }

            var online = Provider.clients.Where(x => x != null && x.player != null).ToArray();
            var ids = new HashSet<ulong>(online.Select(x => x.playerID.steamID.m_SteamID));
            foreach (ulong stale in views.Keys.Where(x => !ids.Contains(x)).ToArray()) views.Remove(stale);
            foreach (var client in online)
            {
                ulong id = client.playerID.steamID.m_SteamID;
                View view;
                if (!views.TryGetValue(id, out view) || view.Player != client.player)
                {
                    if (view != null) Restore(view);
                    view = new View { Player = client.player };
                    views[id] = view;
                }
                if (view.Failed) continue;
                try
                {
                    if (!view.Spawned)
                    {
                        EffectManager.SendUIEffect(effect, c.EffectKey,
                            client.player.channel.GetOwnerTransportConnection(), true);
                        view.Spawned = true;
                    }
                    Render(view);
                    if (c.HideNativeHud && !view.Player.life.isDead) HideNative(view);
                    else RestoreNative(view);
                }
                catch (Exception error)
                {
                    Restore(view);
                    view.Failed = true;
                    Logger.LogException(error, "[MRE UI] HUD отключён для игрока; штатные показатели восстановлены.");
                }
            }
        }

        private void OnRequestChanged(CSteamID id)
        {
            // Источник состояния — Teleportation, не отдельный таймер интерфейса.
            nextTickUtc = DateTime.MinValue;
        }

        private void OnBarricadeHit(CSteamID id, Transform target, ref ushort damage, ref bool allow, EDamageOrigin origin)
        {
            View view;
            if (!CanObservePunch(id, origin, out view)) return;
            var drop = BarricadeManager.FindBarricadeByRootTransform(target);
            if (drop == null || drop.asset == null) return;
            var data = drop.GetServersideData();
            if (data == null || data.barricade == null) return;
            var barricade = data.barricade;
            RememberHit(view, drop.asset.itemName, drop.asset.health, () => barricade.health);
        }

        internal void ObservePunchInput(PlayerInput input, InputInfo hit)
        {
            if (input == null || input.player == null || hit.transform == null || input.player.look == null || input.player.look.aim == null) return;
            Player player = input.player;
            if (player.channel == null || player.channel.owner == null || player.equipment == null || player.life == null) return;
            View view;
            if (!CanObservePunch(player.channel.owner.playerID.steamID, EDamageOrigin.Punch, out view) || view.Player != player) return;
            float distanceSquared = (hit.point - player.look.aim.position).sqrMagnitude;
            bool isBarricade = hit.type == ERaycastInfoType.BARRICADE && hit.transform.CompareTag("Barricade");
            bool isStructure = hit.type == ERaycastInfoType.STRUCTURE && hit.transform.CompareTag("Structure");
            if (!BuildingHitState.IsValidatedInput(distanceSquared, isBarricade || isStructure)) return;
            // Уязвимые объекты обрабатываются существующим damage-event после этого postfix.
            if (isBarricade)
            {
                var drop = BarricadeManager.FindBarricadeByRootTransform(hit.transform);
                if (drop == null || drop.asset == null || (drop.asset.canBeDamaged && drop.asset.isVulnerable)) return;
                var data = drop.GetServersideData();
                if (data == null || data.barricade == null) return;
                var barricade = data.barricade;
                RememberHit(view, drop.asset.itemName, drop.asset.health, () => barricade.health);
            }
            else
            {
                var drop = StructureManager.FindStructureByRootTransform(hit.transform);
                if (drop == null || drop.asset == null || (drop.asset.canBeDamaged && drop.asset.isVulnerable)) return;
                var data = drop.GetServersideData();
                if (data == null || data.structure == null) return;
                var structure = data.structure;
                RememberHit(view, drop.asset.itemName, drop.asset.health, () => structure.health);
            }
        }

        private void OnStructureHit(CSteamID id, Transform target, ref ushort damage, ref bool allow, EDamageOrigin origin)
        {
            View view;
            if (!CanObservePunch(id, origin, out view)) return;
            var drop = StructureManager.FindStructureByRootTransform(target);
            if (drop == null || drop.asset == null) return;
            var data = drop.GetServersideData();
            if (data == null || data.structure == null) return;
            var structure = data.structure;
            RememberHit(view, drop.asset.itemName, drop.asset.health, () => structure.health);
        }

        private bool CanObservePunch(CSteamID id, EDamageOrigin origin, out View view)
        {
            view = null;
            return views.TryGetValue(id.m_SteamID, out view) && view.Player != null &&
                BuildingHitState.IsEligible(origin == EDamageOrigin.Punch, !view.Player.life.isDead, view.Player.equipment.asset == null);
        }

        private void RememberHit(View view, string name, int maximum, Func<int> readHealth)
        {
            if (maximum <= 0) return;
            // Restart the local animation during the render loop.
            view.BuildingRestart = true;
            view.BuildingHit = new BuildingHitState { Name = HudFormatting.BuildingName(name), Maximum = maximum,
                ReadHealth = readHealth, ExpiresUtc = DateTime.UtcNow.AddSeconds(5) };
            nextTickUtc = DateTime.MinValue;
        }

        internal void ObserveDamage(CSteamID attacker, int damage, DamageKind kind)
        {
            View view;
            bool found = views.TryGetValue(attacker.m_SteamID, out view);
            if (Environment.GetEnvironmentVariable("MRE_HUD_DAMAGE_DIAGNOSTICS") == "1")
                Logger.Log("[MRE UI] Damage diagnostic: HUD view=" + found + " spawned=" + (found && view.Spawned) +
                    " alive=" + (found && view.Player != null && view.Player.life != null && !view.Player.life.isDead) + " damage=" + damage);
            if (damage <= 0 || attacker.m_SteamID == 0 || !found ||
                view.Player == null || view.Player.life == null || view.Player.life.isDead || !view.Spawned) return;
            int slot = view.Damage.Add(DateTime.UtcNow, TimeSpan.FromSeconds(1.2));
            string suffix = slot.ToString(CultureInfo.InvariantCulture);
            // Restart an occupied slot even when the next value and color match.
            Visible(view, "DamageNumberPanel" + suffix, false);
            // Send each hit immediately: several hits can occur between HUD refreshes.
            Text(view, "DamageNumberText" + suffix, "<color=" + DamageNumber.Color(kind) + ">" + DamageNumber.Format(damage) + "</color>");
            Visible(view, "DamageNumberPanel" + suffix, true);
        }

        private void RenderBuildingHit(View view)
        {
            var hit = view.BuildingHit;
            bool visible = hit != null && hit.IsVisible(DateTime.UtcNow, !view.Player.life.isDead);
            if (visible && view.BuildingRestart) Visible(view, "BuildingPanel", false);
            view.BuildingRestart = false;
            if (!visible) { Visible(view, "BuildingPanel", false); view.BuildingHit = null; return; }
            int health = Math.Max(0, Math.Min(hit.Maximum, hit.ReadHealth()));
            Text(view, "BuildingName", hit.Name);
            Text(view, "BuildingHealth", health.ToString(CultureInfo.InvariantCulture) + " / " + hit.Maximum.ToString(CultureInfo.InvariantCulture));
            int filled = HudFormatting.FilledSegments(health, hit.Maximum, 100);
            SetSegments(view, "Building", filled, 100);
            Visible(view, "BuildingPanel", true);
        }

        private void Render(View view)
        {
            Player p = view.Player;
            bool alive = !p.life.isDead;
            if (!alive) { view.BuildingHit = null; view.Damage.Clear(); view.Meters.Clear(); view.MeterTimeValid = false; Visible(view, "HudContent", false); return; }
            float now = Time.realtimeSinceStartup;
            double elapsed = view.MeterTimeValid ? Math.Max(0, now - view.LastMeterTime) : 0;
            view.LastMeterTime = now; view.MeterTimeValid = true;
            Meter(view, "Health", p.life.health, elapsed);
            Meter(view, "Food", p.life.food, elapsed);
            Meter(view, "Water", p.life.water, elapsed);
            Meter(view, "Stamina", p.life.stamina, elapsed);
            Meter(view, "Oxygen", p.life.oxygen, elapsed);
            Visible(view, "OxygenPanel", p.life.oxygen < 100);
            Visible(view, "LowHealth", p.life.health <= 25);
            Visible(view, "BleedingPanel", p.life.isBleeding);
            Visible(view, "BrokenPanel", p.life.isBroken);
            Visible(view, "VitalsLowLayout", Configuration.Instance.HideNativeHud);
            Visible(view, "VitalsHighLayout", !Configuration.Instance.HideNativeHud);
            Visible(view, "WeaponLowLayout", Configuration.Instance.HideNativeHud);
            Visible(view, "WeaponHighLayout", !Configuration.Instance.HideNativeHud);

            CSteamID id = p.channel.owner.playerID.steamID;
            bool hasTpa = teleportation != null && teleportation.TPRequests != null;
            Visible(view, "CombatPanel", hasTpa && teleportation.IsPlayerInCombat(id));
            Visible(view, "RaidPanel", hasTpa && teleportation.IsPlayerInRaid(id));
            if (hasTpa)
            {
                Text(view, "CombatTimer", Math.Ceiling(teleportation.GetCombatRemainingSeconds(id)).ToString(CultureInfo.InvariantCulture) + " с");
                Text(view, "RaidTimer", Math.Ceiling(teleportation.GetRaidRemainingSeconds(id)).ToString(CultureInfo.InvariantCulture) + " с");
            }
            RenderWeapon(view);
            RenderBuildingHit(view);
            for (int i = 0; i < view.Damage.Count; i++)
                Visible(view, "DamageNumberPanel" + i.ToString(CultureInfo.InvariantCulture), view.Damage.IsVisible(i, DateTime.UtcNow));
            RenderTeleport(view, hasTpa, id);
            RenderVote(view, id.m_SteamID);
            RenderSpawnProtection(view);
            RenderStats(view, id.m_SteamID);
            // При первом показе сначала заполняем значения; в prefab корень скрыт.
            Visible(view, "HudContent", true);
        }

        // Reflection keeps MreStats optional, including when it is loaded after HUD.
        private void RenderSpawnProtection(View view)
        {
            try
            {
                if (DateTime.UtcNow >= nextProtectionDiscoveryUtc)
                {
                    nextProtectionDiscoveryUtc = DateTime.UtcNow.AddSeconds(2);
                    Type type = AppDomain.CurrentDomain.GetAssemblies().Reverse()
                        .Select(a => a.GetType("MreSpawnProtection.MreSpawnProtectionPlugin", false)).FirstOrDefault(t => t != null);
                    protectionInstance = type == null ? null : type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    protectionRemaining = type == null ? null : type.GetMethod("GetRemainingSeconds", new[] { typeof(Player) });
                }
                object instance = protectionInstance == null ? null : protectionInstance.GetValue(null, null);
                object result = instance == null || protectionRemaining == null ? null : protectionRemaining.Invoke(instance, new object[] { view.Player });
                double seconds = result is double ? (double)result : 0;
                bool shown = !Double.IsNaN(seconds) && !Double.IsInfinity(seconds) && seconds > 0;
                if (shown) Text(view, "SpawnProtectionTimer", Math.Ceiling(Math.Min(999, seconds)).ToString(CultureInfo.InvariantCulture) + " с");
                Visible(view, "SpawnProtectionPanel", shown);
            }
            catch (Exception) { Visible(view, "SpawnProtectionPanel", false); }
        }

        private void RenderStats(View view, ulong id)
        {
            if (DateTime.UtcNow < view.NextStatsUtc) return;
            view.NextStatsUtc = DateTime.UtcNow.AddSeconds(1);
            try
            {
                if (DateTime.UtcNow >= nextStatsDiscoveryUtc)
                {
                    nextStatsDiscoveryUtc = DateTime.UtcNow.AddSeconds(2);
                    Type type = AppDomain.CurrentDomain.GetAssemblies().Reverse().Select(a => a.GetType("MreStats.MreStatsPlugin", false)).FirstOrDefault(t => t != null);
                    statsInstance = type == null ? null : type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    statsGetSnapshot = type == null ? null : type.GetMethod("GetSnapshot", new[] { typeof(ulong) });
                }
                object instance = statsInstance == null ? null : statsInstance.GetValue(null, null);
                object snapshot = instance == null || statsGetSnapshot == null ? null : statsGetSnapshot.Invoke(instance, new object[] { id });
                if (snapshot == null) { HideStats(view); return; }
                Type snapshotType = snapshot.GetType();
                if (snapshotType != statsSnapshotType)
                {
                    statsSnapshotType = snapshotType;
                    statsKills = snapshotType.GetProperty("Kills"); statsDeaths = snapshotType.GetProperty("Deaths");
                    statsKd = snapshotType.GetProperty("Kd"); statsZombies = snapshotType.GetProperty("Zombies");
                }
                object kills = statsKills?.GetValue(snapshot, null);
                object deaths = statsDeaths?.GetValue(snapshot, null);
                object kd = statsKd?.GetValue(snapshot, null);
                object zombies = statsZombies?.GetValue(snapshot, null);
                if (!(kills is long) || !(deaths is long) || !(kd is double) || (zombies != null && !(zombies is long)))
                { HideStats(view); return; }
                Text(view, "StatsKillsValue", ((long)kills).ToString(CultureInfo.InvariantCulture));
                Text(view, "StatsDeathsValue", ((long)deaths).ToString(CultureInfo.InvariantCulture));
                Text(view, "StatsKdValue", ((double)kd).ToString("0.00", CultureInfo.InvariantCulture));
                Text(view, "StatsZombiesValue", zombies == null ? "—" : ((long)zombies).ToString(CultureInfo.InvariantCulture));
                Visible(view, "StatsPanel", true);
            }
            catch (Exception) { HideStats(view); }
        }

        private void HideStats(View view) { try { Visible(view, "StatsPanel", false); } catch (Exception) { } }

        private void RenderWeapon(View view)
        {
            var equipment = view.Player.equipment;
            var gun = equipment.asset as ItemGunAsset;
            bool equipped = gun != null;
            Visible(view, "WeaponPanel", equipped);
            if (!equipped) return;
            Text(view, "WeaponName", HudFormatting.PlayerName(gun.itemName));
            int ammo = HudFormatting.Ammo(equipment.state);
            int magazineId = HudFormatting.MagazineId(equipment.state);
            Text(view, "AmmoValue", ammo < 0 || magazineId == 0 ? "—" : ammo.ToString(CultureInfo.InvariantCulture));
            var magazine = magazineId <= 0 ? null : Assets.find(EAssetType.ITEM, (ushort)magazineId) as ItemMagazineAsset;
            Text(view, "MagazineName", magazineId == 0 ? "Без магазина" : magazineId < 0 ? "Магазин: —" :
                HudFormatting.MagazineName(magazineId, magazine == null ? null : magazine.itemName));
            Text(view, "AmmoCapacity", "/ " + (magazine == null ? "—" : magazine.amount.ToString(CultureInfo.InvariantCulture)));
            Text(view, "DurabilityValue", equipment.quality + "%");
            string firemode = "—";
            if (equipment.state != null && equipment.state.Length > GunStateIndices.FIREMODE)
            {
                switch ((EFiremode)equipment.state[GunStateIndices.FIREMODE])
                {
                    case EFiremode.SAFETY: firemode = "ПРЕДОХРАНИТЕЛЬ"; break;
                    case EFiremode.SEMI: firemode = "ОДИНОЧНЫЙ"; break;
                    case EFiremode.AUTO: firemode = "АВТО"; break;
                    case EFiremode.BURST: firemode = "ОЧЕРЕДЬ"; break;
                }
            }
            Text(view, "FireModeValue", firemode);
            Visible(view, "EmptyMagazine", magazineId > 0 && ammo == 0);
        }

        private void RenderTeleport(View view, bool available, CSteamID id)
        {
            var home = available ? teleportation.GetHomeUiRequest(id) : null;
            if (home != null && !home.IsCanceled)
            {
                Visible(view, "TpaPanel", false);
                Text(view, "HomeName", String.IsNullOrWhiteSpace(home.HomeName) ? "Дом" : HudFormatting.PlayerName(home.HomeName));
                Text(view, "HomeTimer", ((int)Math.Ceiling(Math.Max(0, home.RemainingSeconds))).ToString(CultureInfo.InvariantCulture) + " с");
                double duration = Math.Max(0.001, home.DelaySeconds);
                int progress = (int)Math.Round(Math.Max(0, Math.Min(1, 1 - home.RemainingSeconds / duration)) * 100);
                Segments(view, "HomeProgress", progress, 100);
                Visible(view, "HomePanel", true);
                return;
            }
            Visible(view, "HomePanel", false);
            var request = available ? teleportation.GetUiRequest(id) : null;
            Visible(view, "TpaPanel", request != null);
            if (request == null) return;
            bool incoming = request.Target == id;
            string name = incoming ? request.SenderPlayer.DisplayName : request.TargetPlayer.DisplayName;
            Text(view, "TpaPlayerName", HudFormatting.PlayerName(name));
            Visible(view, "TpaAcceptPanel", incoming && !request.IsExecuting);
            Text(view, "TpaAcceptKey", Configuration.Instance.AcceptKeyLabel);
            Text(view, "TpaDenyKey", Configuration.Instance.DenyKeyLabel);
            if (request.IsExecuting)
            {
                int seconds = (int)Math.Ceiling(Math.Max(0, request.RemainingSeconds));
                Text(view, "TpaTitle", incoming ? "ИГРОК ТЕЛЕПОРТИРУЕТСЯ К ВАМ" : "ТЕЛЕПОРТАЦИЯ К ИГРОКУ");
                Text(view, "TpaTimer", seconds.ToString(CultureInfo.InvariantCulture) + " с");
                Text(view, "TpaHint", "Можно двигаться");
                Text(view, "TpaDenyText", "ОТМЕНИТЬ");
                double total = Math.Max(0.001, request.DelaySeconds);
                int percent = (int)Math.Round(Math.Max(0, Math.Min(1, 1 - request.RemainingSeconds / total)) * 100);
                Segments(view, "TpaProgress", percent, 100);
            }
            else
            {
                int seconds = (int)Math.Ceiling(Math.Max(0, (request.ExpireDate - DateTime.Now).TotalSeconds));
                Text(view, "TpaTitle", incoming ? "ЗАПРОС НА ТЕЛЕПОРТАЦИЮ" : "ЗАПРОС ОТПРАВЛЕН");
                Text(view, "TpaTimer", seconds.ToString(CultureInfo.InvariantCulture) + " с");
                Text(view, "TpaHint", incoming ? "хочет телепортироваться к вам" : "Ожидаем согласия игрока");
                Text(view, "TpaDenyText", incoming ? "ОТКЛОНИТЬ" : "ОТМЕНИТЬ");
                Segments(view, "TpaProgress", 0, 100);
            }
        }

        private void RenderVote(View view, ulong playerId)
        {
            var essentials = MreEssentialsPlugin.Instance;
            var vote = essentials != null ? essentials.GetVoteUiSnapshot(playerId) : null;
            Visible(view, "VotePanel", vote != null);
            if (vote == null) return;
            bool active = vote.Phase == "active";
            bool passed = vote.Phase == "passed";
            Text(view, "VoteTopic", String.IsNullOrWhiteSpace(vote.Topic) ? "Наступление дня" : vote.Topic);
            Text(view, "VoteInitiator", "Инициатор: " + HudFormatting.PlayerName(vote.InitiatorName));
            Text(view, "VoteCount", vote.YesVotes.ToString(CultureInfo.InvariantCulture) + " за  ·  " + vote.NoVotes.ToString(CultureInfo.InvariantCulture) + " против");
            Text(view, "VoteEligible", "голосов за · участников " + vote.EligibleCount.ToString(CultureInfo.InvariantCulture));
            int seconds = active ? (int)Math.Ceiling(Math.Max(0, vote.RemainingSeconds)) : 0;
            Text(view, "VoteTimer", active ? seconds.ToString(CultureInfo.InvariantCulture) + " с" : "");
            // Plugin Key #3/#4 can be assigned to physical Q/E in the client controls.
            // The server receives only those plugin-key events, so the HUD deliberately
            // shows the requested Q/E labels instead of pretending to read raw keyboard input.
            bool showChoices = active && (vote.CanVote || vote.HasVoted);
            Visible(view, "VoteYesPanel", showChoices);
            Visible(view, "VoteNoPanel", showChoices);
            Visible(view, "VoteCount", active);
            Visible(view, "VoteTimerBack", active);
            Visible(view, "VoteProgressTrack", active);
            Visible(view, "VoteHoldCaption", active && vote.CanVote);
            Visible(view, "VoteStatus", !showChoices);
            Text(view, "VoteYesKey", showChoices ? "Q" : "");
            Text(view, "VoteNoKey", showChoices ? "E" : "");
            Visible(view, "VoteYesSelected", vote.VotedYes);
            Visible(view, "VoteNoSelected", vote.VotedNo);
            string status = active && vote.HasVoted
                ? (vote.VotedYes ? "Голос принят: ЗА" : vote.VotedNo ? "Голос принят: ПРОТИВ" : "Голос принят")
                : active ? "Участие со следующего голосования" : passed ? "Наступил день" :
                vote.Phase == "expired" ? "Голосование не принято" : "Голосование отменено";
            Text(view, "VoteStatus", status);
            Text(view, "VoteHint", !active ? (passed ? "Спасибо за участие" : "Новый запуск: /vote day после перерыва") :
                vote.HasVoted ? "Ваш голос принят" : vote.CanVote ? "За день: /vote yes  ·  /cv" : "Участие доступно со следующего голосования");
            Visible(view, "VoteSuccessAccent", !active && passed);
            Visible(view, "VoteFailureAccent", !active && !passed);
            int percent = !active || vote.DurationSeconds <= 0 ? 0 : (int)Math.Round(Math.Max(0, Math.Min(1, vote.RemainingSeconds / vote.DurationSeconds)) * 100);
            Segments(view, "VoteProgress", percent, 100);
        }

        private void Meter(View view, string name, int value, double elapsed)
        {
            Text(view, name + "Value", Math.Max(0, Math.Min(100, value)).ToString(CultureInfo.InvariantCulture));
            MeterAnimation meter;
            if (!view.Meters.TryGetValue(name, out meter)) view.Meters[name] = meter = new MeterAnimation();
            Segments(view, name, meter.Advance(value, elapsed), 100);
        }

        private void Segments(View view, string name, int value, int segments)
        {
            SetSegments(view, name, HudFormatting.FilledSegments(value, 100, segments), segments);
        }

        private void SetSegments(View view, string name, int filled, int segments)
        {
            int previous;
            if (view.SegmentCounts.TryGetValue(name, out previous))
            {
                if (previous == filled) return;
                if (previous > 0) Visible(view, name + "Fill" + previous.ToString("00"), false);
            }
            if (filled > 0) Visible(view, name + "Fill" + filled.ToString("00"), true);
            view.SegmentCounts[name] = filled;
        }

        private void Text(View view, string child, string value)
        {
            string old;
            if (view.Text.TryGetValue(child, out old) && old == value) return;
            EffectManager.sendUIEffectText(Configuration.Instance.EffectKey,
                view.Player.channel.GetOwnerTransportConnection(), true, child, value);
            if (IsDualLayoutChild(child))
                EffectManager.sendUIEffectText(Configuration.Instance.EffectKey, view.Player.channel.GetOwnerTransportConnection(), true, child + "High", value);
            view.Text[child] = value;
        }

        private void Visible(View view, string child, bool value)
        {
            bool old;
            if (view.Visibility.TryGetValue(child, out old) && old == value) return;
            EffectManager.sendUIEffectVisibility(Configuration.Instance.EffectKey,
                view.Player.channel.GetOwnerTransportConnection(), true, child, value);
            if (IsDualLayoutChild(child))
                EffectManager.sendUIEffectVisibility(Configuration.Instance.EffectKey, view.Player.channel.GetOwnerTransportConnection(), true, child + "High", value);
            view.Visibility[child] = value;
        }

        private static bool IsDualLayoutChild(string child)
        {
            return child.StartsWith("Health", StringComparison.Ordinal) || child.StartsWith("Food", StringComparison.Ordinal) ||
                child.StartsWith("Water", StringComparison.Ordinal) || child.StartsWith("Stamina", StringComparison.Ordinal) ||
                child.StartsWith("Oxygen", StringComparison.Ordinal) || child == "LowHealth" ||
                child == "BleedingPanel" || child == "BrokenPanel" || child == "WeaponPanel" ||
                child == "WeaponName" || child == "MagazineName" || child == "AmmoValue" || child == "AmmoCapacity" ||
                child == "FireModeValue" || child == "DurabilityValue" || child == "EmptyMagazine" ||
                child == "StatsPanel" || child == "StatsKillsValue" || child == "StatsDeathsValue" ||
                child == "StatsKdValue" || child == "StatsZombiesValue";
        }

        private static void HideNative(View view)
        {
            foreach (var flag in ReplacedFlags)
            {
                if (!view.Player.isPluginWidgetFlagActive(flag)) continue;
                view.NativeFlagsChanged |= flag;
                view.Player.disablePluginWidgetFlag(flag);
            }
        }

        private static void RestoreNative(View view)
        {
            if (view.Player == null) return;
            foreach (var flag in ReplacedFlags)
                if ((view.NativeFlagsChanged & flag) != 0) view.Player.enablePluginWidgetFlag(flag);
            view.NativeFlagsChanged = EPluginWidgetFlags.None;
        }

        private void Restore(View view)
        {
            try { RestoreNative(view); }
            catch (Exception error) { Logger.LogException(error, "[MRE UI] Ошибка восстановления штатного HUD."); }
            if (view.Player != null && view.Spawned)
            {
                try { EffectManager.ClearEffectByGuid(Guid.Parse(Configuration.Instance.EffectGuid), view.Player.channel.GetOwnerTransportConnection()); }
                catch (Exception error) { Logger.LogException(error, "[MRE UI] Ошибка очистки UI."); }
            }
            view.Spawned = false;
            view.Text.Clear();
            view.Visibility.Clear();
            view.SegmentCounts.Clear();
            view.Meters.Clear();
            view.MeterTimeValid = false;
        }
    }
}
