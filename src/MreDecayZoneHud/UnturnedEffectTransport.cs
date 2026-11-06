using System;
using SDG.Unturned;

namespace MreDecayZoneHud
{
    // Требует отдельный эффект и проверенный разрешатель текущего соединения.
    // Все вызовы выполняет будущий адаптер на главном игровом потоке.
    public sealed class UnturnedEffectTransport : IHintEffectTransport
    {
        private readonly Func<ulong, Player> playerFor;
        private readonly Guid effectGuid;
        private readonly ushort effectId;
        private readonly short effectKey;

        public UnturnedEffectTransport(Func<ulong, Player> playerFor, ushort effectId, Guid effectGuid, short effectKey)
        {
            this.playerFor = playerFor ?? throw new ArgumentNullException(nameof(playerFor));
            if (effectId == 0) throw new ArgumentOutOfRangeException(nameof(effectId));
            if (effectGuid == Guid.Empty) throw new ArgumentException("Effect GUID is required.", nameof(effectGuid));
            if (effectKey == 0) throw new ArgumentOutOfRangeException(nameof(effectKey));
            this.effectId = effectId;
            this.effectGuid = effectGuid;
            this.effectKey = effectKey;
        }

        private Player PlayerFor(ulong id)
        {
            if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
            Player player = playerFor(id);
            if (player == null || player.channel == null || player.channel.GetOwnerTransportConnection() == null)
                throw new InvalidOperationException("Player connection unavailable.");
            if (Provider.clients == null) throw new InvalidOperationException("Player list unavailable.");
            bool current = false;
            foreach (var client in Provider.clients)
            {
                if (client != null && client.player == player && client.playerID.steamID.m_SteamID == id)
                {
                    current = true;
                    break;
                }
            }
            if (!current) throw new InvalidOperationException("Player does not match a current connection.");
            return player;
        }

        public void Create(ulong playerId)
        {
            var asset = Assets.find(effectGuid) as EffectAsset;
            if (asset == null || asset.id != effectId) throw new InvalidOperationException("Effect ID/GUID mismatch.");
            Player player = PlayerFor(playerId);
            EffectManager.SendUIEffect(asset, effectKey, player.channel.GetOwnerTransportConnection(), true);
        }
        public void SetText(ulong playerId, string element, string text)
        {
            Player player = PlayerFor(playerId);
            EffectManager.sendUIEffectText(effectKey, player.channel.GetOwnerTransportConnection(), true, element, text);
        }
        public void SetVisible(ulong playerId, string element, bool visible)
        {
            Player player = PlayerFor(playerId);
            EffectManager.sendUIEffectVisibility(effectKey, player.channel.GetOwnerTransportConnection(), true, element, visible);
        }
        public void Clear(ulong playerId)
        {
            Player player = PlayerFor(playerId);
            EffectManager.ClearEffectByGuid(effectGuid, player.channel.GetOwnerTransportConnection());
        }
    }
}
