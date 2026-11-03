using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HarmonyLib;
using MreShared;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using RestoreMonarchy.Teleportation;
using RestoreMonarchy.Teleportation.Utils;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreTrade
{
    public sealed partial class MreTradePlugin : RocketPlugin<MreTradeConfiguration>
    {
        internal static MreTradePlugin Instance;
        private readonly Dictionary<ulong, TradeSession> sessions = new Dictionary<ulong, TradeSession>();
        private readonly Dictionary<ulong, DateTime> joined = new Dictionary<ulong, DateTime>();
        private TradeJournal journal;
        private Harmony harmony;
        private int gameThread;
        private DateTime loaded;
        protected override void Load()
        {
            gameThread = Thread.CurrentThread.ManagedThreadId; loaded = DateTime.UtcNow;
            journal = new TradeJournal(Path.Combine(Directory, "transactions"));
            harmony = new Harmony("mre.trade.guard"); harmony.PatchAll(typeof(MreTradePlugin).Assembly);
            Provider.onServerConnected += Connected; Provider.onServerDisconnected += Disconnected;
            foreach (var p in Provider.clients) if (p != null) joined[p.playerID.steamID.m_SteamID] = loaded;
            Instance = this;
            if (Configuration.Instance.UiEnabled && Configuration.Instance.AssetsVerified) StartTradeUi();
        }
        protected override void Unload()
        {
            StopTradeUi();
            Provider.onServerConnected -= Connected; Provider.onServerDisconnected -= Disconnected;
            foreach (var session in sessions.Values.Distinct().ToArray()) Cancel(session, "Обмен отменён: плагин выгружен.");
            if (harmony != null) harmony.UnpatchAll("mre.trade.guard");
            Instance = null;
        }
        private void Connected(CSteamID id) { joined[id.m_SteamID] = DateTime.UtcNow; }
        private void Disconnected(CSteamID id) { TradeSession s; if (sessions.TryGetValue(id.m_SteamID, out s) && !s.Committing) Cancel(s, "Обмен отменён: игрок отключился."); joined.Remove(id.m_SteamID); }
        private UnturnedPlayer Online(ulong id)
        {
            var p = Provider.clients.FirstOrDefault(x => x != null && x.player != null && x.playerID.steamID.m_SteamID == id);
            return p == null ? null : UnturnedPlayer.FromPlayer(p.player);
        }
        private bool Allowed(UnturnedPlayer p)
        {
            var tpa = TeleportationPlugin.Instance; DateTime at;
            if (p == null || p.Player == null || p.Player.life.isDead || (p.Player.inventory.isStoring && !IsOwnEditor(p)) || !p.HasPermission("mre.trade") || Online(p.CSteamID.m_SteamID) == null || journal.Blocked(p.CSteamID.m_SteamID)) return false;
            if (tpa == null || tpa.CombatPlayers == null || tpa.RaidPlayers == null || tpa.Configuration == null || tpa.Configuration.Instance == null || tpa.Configuration.Instance.AllowCombat || tpa.Configuration.Instance.AllowRaid) return false;
            double combat = tpa.Configuration.Instance.CombatDuration, raid = tpa.Configuration.Instance.RaidDuration;
            if (Double.IsNaN(combat) || Double.IsInfinity(combat) || Double.IsNaN(raid) || Double.IsInfinity(raid) || combat < 0 || raid < 0) return false;
            double seconds = Math.Max(combat, raid);
            if (seconds > 604800 || !joined.TryGetValue(p.CSteamID.m_SteamID, out at) || (DateTime.UtcNow - at).TotalSeconds < seconds || (DateTime.UtcNow - loaded).TotalSeconds < seconds) return false;
            return !tpa.IsPlayerInCombat(p.CSteamID) && !tpa.IsPlayerInRaid(p.CSteamID);
        }
        private void Update()
        {
            UpdateTradeUi();
            foreach (var s in sessions.Values.Distinct().ToArray())
                if (!s.Committing && (DateTime.UtcNow >= s.ExpiresUtc || !Allowed(Online(s.A)) || !Allowed(Online(s.B)))) Cancel(s, "Обмен отменён: время истекло или проверка игрока не пройдена.");
        }
        private void Tell(ulong id, string text) { var p = Online(id); if (p != null) MreChat.Say(p, "MRE: " + text); }
        private void Both(TradeSession s, string text) { Tell(s.A, text); Tell(s.B, text); RefreshTradeUi(s); }
        private void Cancel(TradeSession s, string text) { ClearTradeUi(s.A); ClearTradeUi(s.B); sessions.Remove(s.A); sessions.Remove(s.B); Both(s, text); }
        internal void Handle(UnturnedPlayer actor, string[] args)
        {
            if (Thread.CurrentThread.ManagedThreadId != gameThread) throw new InvalidOperationException("Обмен доступен только на игровом потоке.");
            ulong id = actor.CSteamID.m_SteamID;
            if (!Allowed(actor)) { MreChat.Say(actor, "MRE: обмен недоступен: проверка боя, рейда, входа или журнала не пройдена."); return; }
            if (args.Length == 0) { MreChat.Say(actor, "MRE: /trade <имя|SteamID>, accept, list, offer <страница> <x> <y>, remove <страница> <x> <y>, confirm, cancel."); return; }
            string action = args[0].ToLowerInvariant(); TradeSession s;
            if (!sessions.TryGetValue(id, out s)) { Invite(actor, args); return; }
            if (s.Committing) { MreChat.Say(actor, "MRE: обмен обрабатывается."); return; }
            if (!Allowed(Online(s.Other(id))) || DateTime.UtcNow >= s.ExpiresUtc) { Cancel(s, "Обмен отменён: игрок недоступен, в бою или истекло время."); return; }
            if (action == "cancel" && args.Length == 1) { Cancel(s, "Обмен отменён."); return; }
            if (action == "accept" && args.Length == 1) { try { s.Accept(id); Both(s, "Обмен принят. Выберите предметы: /trade list; /trade offer <страница> <x> <y>. Содержимое, патроны и модификации в чате не показаны — проверяйте отдельно."); ShowTradeUi(s.A); ShowTradeUi(s.B); } catch (InvalidOperationException) { Tell(id, "Запрос может принять только получатель один раз."); } return; }
            if (!s.Accepted) { Tell(id, "Ожидание согласия второго игрока: /trade accept."); return; }
            if (action == "list" && args.Length <= 2) { int pageNumber = 1; if (args.Length == 2 && (!Int32.TryParse(args[1], out pageNumber) || pageNumber < 1)) { Tell(id, "Укажите номер страницы: /trade list 1."); return; } List(actor, s, pageNumber); return; }
            if ((action == "offer" || action == "remove") && args.Length == 4) { Modify(actor, s, action, args); return; }
            if (action == "confirm" && args.Length == 1) { Confirm(actor, s); return; }
            Tell(id, "Команды: /trade list, offer <страница> <x> <y>, remove <страница> <x> <y>, confirm, cancel.");
        }
        private void Invite(UnturnedPlayer actor, string[] args)
        {
            string query = String.Join(" ", args); ulong targetId;
            if (!UInt64.TryParse(query, out targetId))
            {
                var found = Provider.clients.Where(p => p != null && (String.Equals(p.playerID.characterName, query, StringComparison.OrdinalIgnoreCase) || String.Equals(p.playerID.playerName, query, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (found.Length != 1) { MreChat.Say(actor, "MRE: укажите SteamID64 или уникальное точное имя игрока."); return; }
                targetId = found[0].playerID.steamID.m_SteamID;
            }
            var target = Online(targetId); ulong own = actor.CSteamID.m_SteamID;
            if (targetId == own || !Allowed(target) || sessions.ContainsKey(targetId)) { MreChat.Say(actor, "MRE: игрок недоступен для обмена."); return; }
            var s = new TradeSession(own, targetId, DateTime.UtcNow.AddMinutes(5)); sessions[own] = s; sessions[targetId] = s;
            Tell(own, "Запрос отправлен. Ожидайте принятия."); Tell(targetId, "Игрок " + actor.DisplayName + " предлагает обмен. /trade accept или /trade cancel; 5 минут.");
        }
        private void List(UnturnedPlayer actor, TradeSession s, int number)
        {
            ulong id = actor.CSteamID.m_SteamID; var inv = actor.Player.inventory;
            var rows = new List<string>();
            foreach (var page in Enumerable.Range(PlayerInventory.SLOTS, PlayerInventory.STORAGE - PlayerInventory.SLOTS))
                if (inv.items[page] != null) foreach (var jar in inv.items[page].items) rows.Add(page + " " + jar.x + " " + jar.y + " — " + ItemName(jar.item.id) + " [" + jar.item.id + "] ×" + jar.item.amount + ", качество " + jar.item.quality + "%");
            int pages = Math.Max(1, (rows.Count + 7) / 8);
            if (number > pages) { Tell(id, "Такой страницы нет. Доступно: " + pages + "."); return; }
            Tell(id, "Ваш инвентарь, страница " + number + "/" + pages + ":");
            foreach (string row in rows.Skip((number - 1) * 8).Take(8)) Tell(id, row);
            Tell(id, "Ваше предложение: " + Describe(s.Own(id)) + "; предложение партнёра: " + Describe(s.Own(s.Other(id))) + ".");
        }
        private static string ItemName(ushort id) { var asset = Assets.find(EAssetType.ITEM, id) as ItemAsset; return asset == null ? "Предмет" : asset.itemName; }
        private static string Describe(List<TradeOffer> offers) { return offers.Count == 0 ? "пусто" : String.Join(", ", offers.Select(o => ItemName(o.Id) + " [" + o.Id + "] ×" + o.Amount + ", качество " + o.Quality + "%").ToArray()); }
        private void Modify(UnturnedPlayer actor, TradeSession s, string action, string[] args)
        {
            byte page, x, y; ulong id = actor.CSteamID.m_SteamID;
            if (!Byte.TryParse(args[1], out page) || !Byte.TryParse(args[2], out x) || !Byte.TryParse(args[3], out y) || page < PlayerInventory.SLOTS || page >= PlayerInventory.STORAGE) { Tell(id, "Укажите предметную страницу и координаты из /trade list."); return; }
            var offers = s.Own(id); var existing = offers.FirstOrDefault(o => o.Page == page && o.X == x && o.Y == y);
            if (action == "remove") { if (existing == null) { Tell(id, "В предложении нет такого предмета."); return; } s.Changed(); offers.Remove(existing); Both(s, "Предложение изменено; подтвердите заново."); return; }
            if (existing != null || offers.Count >= 10 || actor.Player.inventory.items[page] == null) { Tell(id, "Предмет уже выбран или достигнут лимит 10."); return; }
            byte index = actor.Player.inventory.items[page].getIndex(x, y);
            if (index == byte.MaxValue) { Tell(id, "Предмет не найден."); return; }
            var jar = actor.Player.inventory.items[page].getItem(index); var item = jar == null ? null : jar.item;
            if (item == null || item.state == null || item.state.Length > 255 || item.GetAsset() == null || actor.Player.equipment.checkSelection(page, x, y)) { Tell(id, "Предмет нельзя предложить; уберите его из рук."); return; }
            s.Changed(); offers.Add(new TradeOffer { Page = page, X = x, Y = y, Jar = jar, Item = item, Id = item.id, Amount = item.amount, Quality = item.quality, State = (byte[])item.state.Clone() });
            Both(s, "Предложение изменено; подтвердите заново. /trade list");
        }
        private void Confirm(UnturnedPlayer actor, TradeSession s)
        {
            ulong id = actor.CSteamID.m_SteamID; var a = Online(s.A); var b = Online(s.B);
            if (IsEditorOpen(s.A) || IsEditorOpen(s.B)) { Tell(id, "Оба игрока должны закрыть инвентарь перед подтверждением."); return; }
            if (s.OffersA.Count == 0 && s.OffersB.Count == 0) { Tell(id, "Пустой обмен не подтверждается."); return; }
            if (!Allowed(a) || !Allowed(b) || !NotEquipped(a, s.OffersA) || !NotEquipped(b, s.OffersB) || !TradeDelivery.Valid(a.Player.inventory, s.OffersA) || !TradeDelivery.Valid(b.Player.inventory, s.OffersB)) { s.Changed(); Both(s, "Предметы или состояние игроков изменились; проверьте предложение."); return; }
            if (!s.Confirm(id)) { Both(s, "Одна сторона подтвердила обмен; ожидается вторая."); return; }
            try
            {
                var ia = a.Player.inventory; var ib = b.Player.inventory;
                var toA = TradeDelivery.Plan(ia, s.OffersA, s.OffersB); var toB = TradeDelivery.Plan(ib, s.OffersB, s.OffersA);
                if (toA == null || toB == null) { s.Changed(); Both(s, "Не хватает места после обмена; освободите инвентарь."); return; }
                s.Committing = true;
                string operation = journal.Prepare(s.A, s.B, ia, ib, toA, toB);
                // События инвентаря могут вызывать посторонний код; при ошибке ничего не повторяем автоматически.
                if (!TradeDelivery.Valid(ia, s.OffersA) || !TradeDelivery.Valid(ib, s.OffersB) || !AllowedForCommit(a) || !AllowedForCommit(b)) throw new InvalidOperationException("Изменение состояния перед обменом.");
                using (var guard = new TradeMutationGuard(s.OffersA.Concat(s.OffersB)))
                {
                    System.Action verify = TradeTransaction.Apply(ia, ib, s.OffersA, s.OffersB, toA, toB,
                        () => { if (guard.Interference || !AllowedForCommit(a) || !AllowedForCommit(b) || !NotEquipped(a, s.OffersA) || !NotEquipped(b, s.OffersB)) throw new InvalidOperationException("Состояние игрока изменилось во время обмена."); });
                    verify(); ia.save(); verify(); ib.save(); verify();
                }
                journal.Complete(operation, s.A, s.B);
                Cancel(s, "Обмен завершён.");
            }
            catch (Exception error)
            {
                Logger.LogException(error, "[MRE TRADE] Неопределённый результат обмена; нужна сверка журнала.");
                sessions.Remove(s.A); sessions.Remove(s.B); Both(s, "Обмен прерван. Не повторяйте его; обратитесь к администрации для проверки.");
            }
        }
        private static bool NotEquipped(UnturnedPlayer p, IList<TradeOffer> offers) { return p != null && p.Player != null && offers.All(o => !p.Player.equipment.checkSelection(o.Page, o.X, o.Y)); }
        // Во время commit собственные lock-файлы уже существуют.
        private bool AllowedForCommit(UnturnedPlayer p)
        {
            var tpa = TeleportationPlugin.Instance;
            var current = p == null ? null : Online(p.CSteamID.m_SteamID);
            return p != null && p.Player != null && !p.Player.life.isDead && !p.Player.inventory.isStoring && p.HasPermission("mre.trade") && current != null && current.Player == p.Player && tpa != null && tpa.CombatPlayers != null && tpa.RaidPlayers != null && tpa.Configuration != null && tpa.Configuration.Instance != null && !tpa.Configuration.Instance.AllowCombat && !tpa.Configuration.Instance.AllowRaid && !tpa.IsPlayerInCombat(p.CSteamID) && !tpa.IsPlayerInRaid(p.CSteamID);
        }
    }
}
