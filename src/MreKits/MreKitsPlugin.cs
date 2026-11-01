using MreShared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using RestoreMonarchy.Teleportation;
using RestoreMonarchy.Teleportation.Utils;
using SDG.Unturned;
using Logger = Rocket.Core.Logging.Logger;

namespace MreKits
{
    public sealed class MreKitsPlugin : RocketPlugin<MreKitsConfiguration>
    {
        public static MreKitsPlugin Instance { get; private set; }
        private KitLedger ledger;
        private bool busy;
        private int gameThreadId;
        public event Action<ulong, string> KitClaimed;
        protected override void Load()
        {
            gameThreadId = Thread.CurrentThread.ManagedThreadId;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Configuration.Instance.Kits == null || Configuration.Instance.Kits.Count > 100) throw new InvalidDataException("Некорректный список kit.");
            foreach (KitDefinition kit in Configuration.Instance.Kits) {
                if (kit == null || String.IsNullOrWhiteSpace(kit.Name) || kit.Name.Length > 32 ||
                    kit.Name.Any(c => !(Char.IsLetterOrDigit(c) || c == '_' || c == '-')) || !names.Add(kit.Name) ||
                    kit.CooldownSeconds < 1 || kit.CooldownSeconds > 31536000 || kit.Items == null || kit.Items.Count == 0 ||
                    kit.Items.Any(i => i == null || i.Id == 0 || i.Count < 1 || i.Count > 100) || kit.Items.Sum(i => i.Count) > 100)
                    throw new InvalidDataException("Некорректный kit. Проверьте имя, cooldown и предметы.");
            }
            if (names.Contains("start") && names.Contains("starter"))
                throw new InvalidDataException("Наборы start и starter используют общий журнал и не могут быть настроены одновременно.");
            ledger = new KitLedger(Path.Combine(Directory, "claims.json"));
            Instance = this;
            Logger.Log("[MRE] MreKits загружен: /kit start, /kits; журнал выдачи сохранен на диске.");
        }
        protected override void Unload() { Instance = null; KitClaimed = null; ledger = null; }
        private void RequireGameThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != gameThreadId)
                throw new InvalidOperationException("MreKits API вызывается только с главного игрового потока.");
        }
        private static bool IsOnline(UnturnedPlayer player)
        {
            return player != null && player.Player != null && Provider.clients.Any(client => client != null &&
                client.playerID.steamID == player.CSteamID && Object.ReferenceEquals(client.player, player.Player));
        }
        // API для отдельного UI; проверка прав выполняется и здесь, и в Claim.
        public string[] GetKitNames(UnturnedPlayer player)
        {
            RequireGameThread();
            if (Instance != this || !IsOnline(player) || !player.HasPermission("mre.kit")) return new string[0];
            return Configuration.Instance.Kits.Select(k => k.Name).ToArray();
        }
        public ClaimRecord GetClaim(UnturnedPlayer player, string name)
        {
            RequireGameThread();
            if (Instance != this || !IsOnline(player) || !player.HasPermission("mre.kit") || String.IsNullOrWhiteSpace(name)) return null;
            return ledger.Get(player.CSteamID.m_SteamID, KitNames.LedgerName(name));
        }
        public string Claim(UnturnedPlayer player, string name)
        {
            RequireGameThread();
            if (Instance != this || !IsOnline(player) || !player.HasPermission("mre.kit")) return KitText.Prefix + "У вас нет доступа к наборам.";
            if (player.Player == null || player.Dead) return KitText.Prefix + "Возродитесь, чтобы получить набор.";
            var tpa = TeleportationPlugin.Instance;
            if (tpa == null || tpa.CombatPlayers == null || tpa.RaidPlayers == null || tpa.Configuration == null ||
                tpa.Configuration.Instance == null || tpa.Configuration.Instance.AllowCombat || tpa.Configuration.Instance.AllowRaid)
                return KitText.Prefix + "Проверка боя и рейда недоступна; попробуйте позже.";
            if (tpa.IsPlayerInCombat(player.CSteamID) || tpa.IsPlayerInRaid(player.CSteamID))
                return KitText.Prefix + "Набор недоступен во время боя или рейда.";
            KitDefinition kit = Configuration.Instance.Kits.FirstOrDefault(k => String.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
            if (kit == null) return KitText.Prefix + "Такого набора нет. Доступные наборы: /kits.";
            if (busy) return KitText.Prefix + "Выдаём набор. Дождитесь завершения и повторите команду.";
            busy = true;
            try {
                string ledgerName = KitNames.LedgerName(kit.Name);
                string result = ClaimTransaction.Execute(ledger, player.CSteamID.m_SteamID, ledgerName, DateTime.UtcNow,
                    kit.CooldownSeconds, new InventoryDelivery(player.Player, kit));
                if (result == "inventory_full") return KitText.Prefix + "Освободите место для всего набора и повторите /kit " + kit.Name + ".";
                if (result == "unavailable") {
                    ClaimRecord record = ledger.Get(player.CSteamID.m_SteamID, ledgerName);
                    return record != null && record.Status == "pending"
                        ? KitText.Prefix + "Выдача набора проверяется. Обратитесь к администратору."
                        : KitText.Prefix + "Набор " + kit.Name + " будет доступен через " + KitText.Duration((record.NextClaimUtc - DateTime.UtcNow).TotalSeconds) + ".";
                }
                try { KitClaimed?.Invoke(player.CSteamID.m_SteamID, kit.Name); }
                catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка подписчика KitClaimed."); }
                return KitText.Prefix + "Набор " + kit.Name + " получен. Следующий — через " + KitText.Duration(kit.CooldownSeconds) + ".";
            } catch (Exception error) {
                Logger.LogException(error, "[MRE] Ошибка выдачи kit; проверьте claims.json и инвентарь перед повтором.");
                return KitText.Prefix + "Не удалось выдать набор. Обратитесь к администратору для проверки.";
            } finally { busy = false; }
        }
        public void List(UnturnedPlayer player)
        {
            string[] names = GetKitNames(player);
            MreChat.Say(player, "Наборы: " + (names.Length == 0 ? "пока нет доступных наборов" : String.Join(", ", names)));
        }
    }
}
