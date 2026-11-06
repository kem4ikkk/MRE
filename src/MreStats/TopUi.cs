using System;
using System.Collections.Generic;
using System.Linq;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace MreStats
{
    // Не подключать до появления общего арбитра Modal у всех окон.
    internal sealed class TopUi
    {
        private const short Key = 14824;
        private const ushort Id = 48624;
        private static readonly Guid AssetGuid = new Guid("a3484011ca53436e92008ee933211fb5");
        private readonly MreStatsPlugin plugin;
        private readonly Dictionary<ulong, Player> viewers = new Dictionary<ulong, Player>();
        public TopUi(MreStatsPlugin plugin) { this.plugin = plugin; EffectManager.onEffectButtonClicked += Button; }
        public string Open(UnturnedPlayer actor)
        {
            if (actor == null || actor.Player == null) return "Игрок недоступен.";
            Player p = actor.Player; ulong id = actor.CSteamID.m_SteamID;
            if (viewers.ContainsKey(id)) return "Рейтинг уже открыт.";
            if (p.isPluginWidgetFlagActive(EPluginWidgetFlags.Modal)) return "Сначала закройте открытое окно.";
            var asset = Assets.find(AssetGuid) as EffectAsset;
            if (asset == null || asset.id != Id) return "Окно рейтинга пока недоступно.";
            try {
                viewers.Add(id, p); p.enablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                EffectManager.SendUIEffect(asset, Key, p.channel.GetOwnerTransportConnection(), true);
                Render(p, TopCategory.Kills); return null;
            } catch (Exception e) { Logger.LogException(e, "[MRE Stats] Ошибка окна рейтинга."); Close(id); return "Не удалось открыть рейтинг."; }
        }
        public void Disconnect(CSteamID id) { viewers.Remove(id.m_SteamID); }
        public void Stop()
        {
            EffectManager.onEffectButtonClicked -= Button;
            foreach (ulong id in viewers.Keys.ToArray()) Close(id);
        }
        private void Close(ulong id)
        {
            Player p; if (!viewers.TryGetValue(id, out p)) return; viewers.Remove(id);
            try { if (p != null && p.channel != null) EffectManager.ClearEffectByGuid(AssetGuid, p.channel.GetOwnerTransportConnection()); }
            catch (Exception e) { Logger.LogException(e, "[MRE Stats] Не удалось убрать окно рейтинга."); }
            try { if (p != null) p.disablePluginWidgetFlag(EPluginWidgetFlags.Modal); }
            catch (Exception e) { Logger.LogException(e, "[MRE Stats] Не удалось закрыть режим окна рейтинга."); }
        }
        private void Button(Player player, string name)
        {
            if (player == null || player.channel == null || player.channel.owner == null) return;
            ulong id = player.channel.owner.playerID.steamID.m_SteamID; Player expected;
            if (!viewers.TryGetValue(id, out expected) || expected != player) return;
            if (name == "TopClose") { Close(id); return; }
            if (name == null || !name.StartsWith("TopCategory", StringComparison.Ordinal)) return;
            TopCategory category; string suffix = name.Substring("TopCategory".Length);
            if (!Enum.TryParse(suffix, false, out category) || !Enum.IsDefined(typeof(TopCategory), category) || category.ToString() != suffix) return;
            try { Render(player, category); }
            catch (Exception e) { Logger.LogException(e, "[MRE Stats] Ошибка обновления рейтинга."); Close(id); }
        }
        private void Render(Player p, TopCategory category)
        {
            var connection = p.channel.GetOwnerTransportConnection();
            var rows = plugin.GetTop(category, 10);
            EffectManager.sendUIEffectText(Key, connection, true, "TopTitle", "MRE / РЕЙТИНГ");
            EffectManager.sendUIEffectText(Key, connection, true, "TopPeriod", "Текущий сезон");
            EffectManager.sendUIEffectText(Key, connection, true, "TopStatus", rows.Length == 0 ? "В этом сезоне пока нет данных." : "Данные с момента включения статистики.");
            foreach (TopCategory c in Enum.GetValues(typeof(TopCategory))) EffectManager.sendUIEffectVisibility(Key, connection, true, "TopSelected" + c, c == category);
            for (int i = 0; i < 10; i++) {
                string n = i.ToString("00"); bool show = i < rows.Length;
                EffectManager.sendUIEffectVisibility(Key, connection, true, "TopRow" + n, show);
                if (!show) continue;
                var row = rows[i]; string display = row.Name;
                EffectManager.sendUIEffectText(Key, connection, true, "TopRank" + n, (i + 1).ToString());
                EffectManager.sendUIEffectText(Key, connection, true, "TopName" + n, TopModel.SafeName(display, row.SteamId));
                EffectManager.sendUIEffectText(Key, connection, true, "TopValue" + n, TopModel.Format(row, category));
            }
        }
    }
}
