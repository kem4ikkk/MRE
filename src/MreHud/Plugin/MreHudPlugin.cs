using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            public readonly Dictionary<string, MeterAnimation> Meters = new Dictionary<string, MeterAnimation>();
            public readonly Dictionary<string, int> SegmentCounts = new Dictionary<string, int>();
            public float LastMeterTime;
            public bool MeterTimeValid;
            public EPluginWidgetFlags NativeFlagsChanged;
            public readonly Dictionary<string, string> Text = new Dictionary<string, string>();
            public readonly Dictionary<string, bool> Visibility = new Dictionary<string, bool>();
        }

        private readonly Dictionary<ulong, View> views = new Dictionary<ulong, View>();
        private DateTime nextTickUtc;
        private DateTime nextAssetCheckUtc;
        private TeleportationPlugin teleportation;
        private EffectAsset effect;
        private bool waitingLogged;
        private IDisposable punchObserver;
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
            Logger.Log("[MRE UI] HUD загружен. AssetsVerified=" + c.AssetsVerified +
                ". До проверки клиентского bundle штатные показатели сохраняются.");
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
            view.BuildingHit = new BuildingHitState { Name = HudFormatting.BuildingName(name), Maximum = maximum,
                ReadHealth = readHealth, ExpiresUtc = DateTime.UtcNow.AddSeconds(5) };
            nextTickUtc = DateTime.MinValue;
        }

        private void RenderBuildingHit(View view)
        {
            var hit = view.BuildingHit;
            bool visible = hit != null && hit.IsVisible(DateTime.UtcNow, !view.Player.life.isDead);
            Visible(view, "BuildingPanel", visible);
            if (!visible) { view.BuildingHit = null; return; }
            int health = Math.Max(0, Math.Min(hit.Maximum, hit.ReadHealth()));
            Text(view, "BuildingName", hit.Name);
            Text(view, "BuildingHealth", health.ToString(CultureInfo.InvariantCulture) + " / " + hit.Maximum.ToString(CultureInfo.InvariantCulture));
            int filled = HudFormatting.FilledSegments(health, hit.Maximum, 100);
            SetSegments(view, "Building", filled, 100);
        }

        private void Render(View view)
        {
            Player p = view.Player;
            bool alive = !p.life.isDead;
            if (!alive) { view.BuildingHit = null; view.Meters.Clear(); view.MeterTimeValid = false; Visible(view, "HudContent", false); return; }
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
            RenderWeapon(view);
            RenderBuildingHit(view);
            RenderTeleport(view, hasTpa, id);
            RenderVote(view, id.m_SteamID);
            // При первом показе сначала заполняем значения; в prefab корень скрыт.
            Visible(view, "HudContent", true);
        }

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
            Text(view, "MagazineName", HudFormatting.MagazineName(magazineId, magazine == null ? null : magazine.itemName));
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
                Visible(view, "TpaPanel", true);
                Visible(view, "TpaAcceptPanel", false);
                Text(view, "TpaTitle", "ПЕРЕМЕЩЕНИЕ ДОМОЙ");
                Text(view, "TpaPlayerName", String.IsNullOrWhiteSpace(home.HomeName) ? "Дом" : HudFormatting.PlayerName(home.HomeName));
                Text(view, "TpaTimer", ((int)Math.Ceiling(Math.Max(0, home.RemainingSeconds))).ToString(CultureInfo.InvariantCulture) + " с");
                Text(view, "TpaHint", "Можно двигаться");
                Text(view, "TpaDenyKey", Configuration.Instance.DenyKeyLabel);
                Text(view, "TpaDenyText", "ОТМЕНИТЬ");
                double duration = Math.Max(0.001, home.DelaySeconds);
                int progress = (int)Math.Round(Math.Max(0, Math.Min(1, 1 - home.RemainingSeconds / duration)) * 100);
                Segments(view, "TpaProgress", progress, 100);
                return;
            }
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
            Text(view, "VoteCount", vote.YesVotes.ToString(CultureInfo.InvariantCulture) + " / " + vote.RequiredVotes.ToString(CultureInfo.InvariantCulture));
            Text(view, "VoteEligible", "голосов за · участников " + vote.EligibleCount.ToString(CultureInfo.InvariantCulture));
            int seconds = active ? (int)Math.Ceiling(Math.Max(0, vote.RemainingSeconds)) : 0;
            Text(view, "VoteTimer", active ? seconds.ToString(CultureInfo.InvariantCulture) + " с" : "");
            Text(view, "VoteStatus", active ? "ГОЛОСОВАНИЕ" : passed ? "ПРИНЯТО — НАСТУПИЛ ДЕНЬ" :
                vote.Phase == "expired" ? "ВРЕМЯ ИСТЕКЛО — ГОЛОСОВ НЕДОСТАТОЧНО" : "ГОЛОСОВАНИЕ ОТМЕНЕНО");
            Text(view, "VoteHint", !active ? (passed ? "Спасибо за участие" : "Новый запуск: /vote day после перерыва") :
                vote.HasVoted ? "Ваш голос принят" : vote.CanVote ? "За день: /vote yes  ·  /cv" : "Участие доступно со следующего голосования");
            Visible(view, "VoteSuccessAccent", !active && passed);
            Visible(view, "VoteFailureAccent", !active && !passed);
            int percent = vote.RequiredVotes <= 0 ? 0 : (int)Math.Round(Math.Max(0, Math.Min(1, vote.YesVotes / (double)vote.RequiredVotes)) * 100);
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
                child == "FireModeValue" || child == "DurabilityValue" || child == "EmptyMagazine";
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
