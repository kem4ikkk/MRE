using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MreShared;
using Rocket.Core.Plugins;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreAirdrop
{
    public sealed class MreAirdropPlugin : RocketPlugin<MreAirdropConfiguration>
    {
        public static MreAirdropPlugin Instance { get; private set; }
        public bool Running { get; private set; }
        public DateTime NextUtc { get; private set; }
        private readonly System.Random random = new System.Random();
        private Harmony harmony;
        private FieldInfo planeList;
        private bool oldNativeEnabled, nativeWasChanged;
        private DateTime nextCheck;
        internal AirdropInfo ActivePlane;
        internal SpawnAsset ActiveCargo;
        internal ushort[] ActiveLoot;
        internal DateTime ActiveUntil;
        internal readonly Dictionary<Carepackage, ushort[]> Packages = new Dictionary<Carepackage, ushort[]>();

        protected override void Load()
        {
            if (!Configuration.Instance.Enabled) { Logger.Log("[MRE] Аирдроп отключён в конфигурации."); return; }
            if (Configuration.Instance.IntervalSeconds < 60 || Configuration.Instance.IntervalSeconds > 604800) throw new InvalidOperationException("Некорректный интервал аирдропа.");
            LootPlan.Validate(Configuration.Instance.Categories);
            // Не подменять нативный планировщик при неподдерживаемом SDK.
            planeList = typeof(LevelManager).GetField("airdrops", BindingFlags.Static | BindingFlags.NonPublic);
            var spawn = AccessTools.Method(typeof(LevelManager), "SpawnCarepackage", new[] { typeof(Vector3), typeof(SpawnAsset), typeof(float) });
            var collision = AccessTools.Method(typeof(Carepackage), "OnCollisionEnter", new[] { typeof(Collision) });
            var resolve = AccessTools.Method(typeof(SpawnTableTool), "ResolveLegacyId", new[] { typeof(SpawnAsset), typeof(EAssetType), typeof(Func<string>) });
            if (planeList == null || spawn == null || collision == null || resolve == null || AccessTools.Field(typeof(Carepackage), "isExploded") == null) throw new InvalidOperationException("SDK аирдропа несовместим: нативные сбросы не изменены.");
            harmony = new Harmony("mre.airdrop.v1");
            try
            {
                harmony.Patch(spawn, new HarmonyMethod(typeof(AirdropHooks), "BeforeSpawn"), new HarmonyMethod(typeof(AirdropHooks), "AfterSpawn"));
                harmony.Patch(collision, new HarmonyMethod(typeof(AirdropHooks), "BeforeCollision"), finalizer: new HarmonyMethod(typeof(AirdropHooks), "AfterCollision"));
                harmony.Patch(resolve, new HarmonyMethod(typeof(AirdropHooks), "BeforeResolve"));
                LoadNext();
                Instance = this;
                oldNativeEnabled = Provider.modeConfigData.Events.Use_Airdrops;
                Provider.modeConfigData.Events.Use_Airdrops = false; nativeWasChanged = true;
                Running = true;
                Logger.Log("[MRE] Планировщик аирдропов включён; нативный таймер временно выключен.");
            }
            catch { if (harmony != null) harmony.UnpatchAll("mre.airdrop.v1"); Instance = null; throw; }
        }
        protected override void Unload()
        {
            Running = false;
            if (harmony != null) harmony.UnpatchAll("mre.airdrop.v1");
            if (nativeWasChanged && Provider.modeConfigData != null) Provider.modeConfigData.Events.Use_Airdrops = oldNativeEnabled;
            Packages.Clear(); ActivePlane = null; ActiveLoot = null; Instance = null;
        }
        private string SchedulePath { get { return Path.Combine(Directory, "next-airdrop.utc"); } }
        private void LoadNext()
        {
            if (File.Exists(SchedulePath))
            {
                long ticks;
                if (!Int64.TryParse(File.ReadAllText(SchedulePath).Trim(), out ticks) || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) throw new InvalidDataException("Повреждено расписание аирдропа.");
                NextUtc = new DateTime(ticks, DateTimeKind.Utc);
            }
            else { NextUtc = DateTime.UtcNow.AddSeconds(Configuration.Instance.IntervalSeconds); SaveNext(NextUtc); }
        }
        private void SaveNext(DateTime next)
        {
            string temp = SchedulePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            byte[] bytes = Encoding.ASCII.GetBytes(next.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
            if (File.Exists(SchedulePath)) File.Replace(temp, SchedulePath, null); else File.Move(temp, SchedulePath);
        }
        private void Update()
        {
            if (!Running || DateTime.UtcNow < nextCheck) return;
            nextCheck = DateTime.UtcNow.AddSeconds(1);
            if (ActivePlane != null && DateTime.UtcNow > ActiveUntil) { Logger.LogWarning("[MRE] Аирдроп не был идентифицирован; штатное содержимое не меняется."); ActivePlane = null; ActiveCargo = null; ActiveLoot = null; }
            if (!Provider.isServer || !Level.isLoaded || DateTime.UtcNow < NextUtc) return;
            try
            {
                // Сохраняем следующее время до запуска: после сбоя не будет серии повторов.
                DateTime next = DateTime.UtcNow.AddSeconds(Configuration.Instance.IntervalSeconds);
                SaveNext(next); NextUtc = next;
                if (ActivePlane != null) { Logger.LogWarning("[MRE] Прошлый аирдроп ещё активен; событие пропущено."); return; }
                Launch();
            }
            catch (Exception error)
            {
                Running = false;
                if (nativeWasChanged && Provider.modeConfigData != null)
                { Provider.modeConfigData.Events.Use_Airdrops = oldNativeEnabled; nativeWasChanged = false; }
                Logger.LogException(error, "[MRE] Планировщик остановлен; штатная настройка аирдропов восстановлена. Нужна проверка конфигурации и диска.");
            }
        }
        private void Launch()
        {
            var selected = LootPlan.Select(Configuration.Instance.Categories, random.NextDouble(), random.NextDouble());
            var ids = new List<ushort>();
            foreach (var entry in selected.Item2.Items)
            {
                if (!(Assets.find(EAssetType.ITEM, entry.Id) is ItemAsset)) throw new InvalidOperationException("Отсутствует предмет " + entry.Id);
                for (int i = 0; i < entry.Count; i++) ids.Add(entry.Id);
            }
            // Проверка полного комплекта до появления самолёта: переполнение
            // не должно молча превращать редкий комплект в частичную выдачу.
            var dropRef = Level.getAsset()?.airdropRef ?? AssetReference<AirdropAsset>.invalid;
            if (dropRef.isNull) dropRef = AirdropAsset.defaultAirdrop;
            var dropAsset = dropRef.Find();
            var barricade = dropAsset == null ? null : dropAsset.barricadeRef.Find();
            if (barricade == null) barricade = Assets.find(EAssetType.ITEM, 1374) as ItemBarricadeAsset;
            var box = barricade as ItemStorageAsset;
            if (box == null) throw new InvalidOperationException("Недоступен asset ящика аирдропа.");
            int cells = (int)box.storage_x * box.storage_y;
            // Native inventory packet ограничивает число предметов байтом;
            // также держимся ниже server drag-item лимита 200.
            if (cells < 1 || cells > 196) throw new InvalidOperationException("Неподдерживаемый размер ящика аирдропа (максимум 196 клеток).");
            var preview = new Items(PlayerInventory.STORAGE);
            preview.loadSize(box.storage_x, box.storage_y);
            foreach (ushort id in ids)
                if (!preview.tryAddItem(new Item(id, EItemOrigin.ADMIN), false))
                    throw new InvalidOperationException("Комплект не помещается в ящик: " + selected.Item2.Name);
            ushort[] fillers;
            switch (selected.Item1.Name)
            {
                case "Оружейный":
                    if (selected.Item2.Name == "Grizzly") fillers = new ushort[] { 298, 394 };
                    else if (selected.Item2.Name == "Timberwolf") fillers = new ushort[] { 20, 394 };
                    else fillers = new ushort[] { 394 };
                    break;
                case "Строительный": fillers = new ushort[] { 285 }; break;
                case "Рейдовый": fillers = new ushort[] { 254, 1100 }; break;
                default: throw new InvalidOperationException("Неизвестная категория для добора ящика: " + selected.Item1.Name);
            }
            foreach (ushort filler in fillers)
            {
                var fillerAsset = Assets.find(EAssetType.ITEM, filler) as ItemAsset;
                if (fillerAsset == null || fillerAsset.size_x != 1 || fillerAsset.size_y != 1)
                    throw new InvalidOperationException("Предмет добора должен иметь размер 1×1: " + filler);
            }
            int topUp = 0;
            byte freeX, freeY, rotation;
            while (preview.tryFindSpace(1, 1, out freeX, out freeY, out rotation))
            {
                if (ids.Count >= cells || topUp >= cells)
                    throw new InvalidOperationException("Нарушен предел заполнения ящика.");
                ushort filler = fillers[topUp % fillers.Length];
                if (!preview.tryAddItem(new Item(filler, EItemOrigin.ADMIN), false))
                    throw new InvalidOperationException("Не удалось заполнить найденную свободную клетку.");
                ids.Add(filler); topUp++;
            }
            Logger.Log("[MRE] Комплект " + selected.Item1.Name + "/" + selected.Item2.Name +
                ": ящик " + box.storage_x + "×" + box.storage_y + ", добор [" + String.Join(",", fillers.Select(x => x.ToString()).ToArray()) + "] ×" + topUp +
                ", всего экземпляров " + ids.Count + "; свободных клеток нет.");
            var node = LevelManager.GetRandomAirdropNode();
            if (node == null) throw new InvalidOperationException("На карте нет точки аирдропа.");
            var cargo = node.GetCargoSpawnTableOrLogWarning();
            if (cargo == null) throw new InvalidOperationException("У точки аирдропа нет таблицы груза.");
            var planes = planeList.GetValue(null) as IList;
            if (planes == null) throw new InvalidOperationException("Список самолётов недоступен.");
            var before = new HashSet<object>(planes.Cast<object>());
            LevelManager.SpawnAirdrop(node.transform.position, cargo);
            var added = planes.Cast<object>().Where(x => !before.Contains(x)).ToArray();
            if (added.Length != 1 || !(added[0] is AirdropInfo)) throw new InvalidOperationException("Не удалось однозначно определить новый самолёт.");
            ActivePlane = (AirdropInfo)added[0]; ActiveCargo = cargo; ActiveLoot = ids.ToArray(); ActiveUntil = DateTime.UtcNow.AddMinutes(20);
            MreChat.Say("MRE: в пути " + selected.Item1.Name.ToLowerInvariant() + " аирдроп!");
        }
    }
}
