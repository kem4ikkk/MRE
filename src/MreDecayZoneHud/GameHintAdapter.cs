using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace MreDecayZoneHud
{
    // Не самостоятельный плагин: будущий владелец вызывает Tick на Unity-потоке.
    // Доступность decay и подтверждённость зон обязательны в provider.
    public sealed class GameHintAdapter
    {
        private readonly Dictionary<ulong, SteamPlayer> active = new Dictionary<ulong, SteamPlayer>();
        private readonly HintSessions sessions;
        private readonly Action<Exception> onError;

        public GameHintAdapter(IDecayZoneStatusSource zoneStatus, ushort effectId, Guid effectGuid, short effectKey, Action<Exception> onError)
        {
            this.onError = onError ?? throw new ArgumentNullException(nameof(onError));
            var transport = new UnturnedEffectTransport(ResolvePlayer, effectId, effectGuid, effectKey);
            sessions = new HintSessions(new EffectHintSink(transport), zoneStatus ?? new UnavailableDecayZoneStatusSource());
        }

        private Player ResolvePlayer(ulong id)
        {
            SteamPlayer client;
            return active.TryGetValue(id, out client) ? client.player : null;
        }

        public void Tick()
        {
            if (Provider.clients == null) { FailClosed(); throw new InvalidOperationException("Player list unavailable."); }
            var current = new Dictionary<ulong, SteamPlayer>();
            var listed = new HashSet<SteamPlayer>();
            foreach (var client in Provider.clients)
            {
                if (client == null) continue;
                listed.Add(client);
                if (client.player == null || client.playerID == null) continue;
                ulong id = client.playerID.steamID.m_SteamID;
                if (id == 0 || current.ContainsKey(id)) { FailClosed(); throw new InvalidOperationException("Invalid or duplicate player session."); }
                current.Add(id, client);
            }
            foreach (var old in new List<KeyValuePair<ulong, SteamPlayer>>(active))
            {
                SteamPlayer fresh;
                if (!current.TryGetValue(old.Key, out fresh) || !System.Object.ReferenceEquals(old.Value, fresh))
                {
                    if (listed.Contains(old.Value))
                    {
                        try { sessions.Suspend(old.Key, old.Value); }
                        catch (Exception error) { onError(error); }
                        continue;
                    }
                    // Provider.clients больше не содержит старый объект сессии.
                    // Ошибку не скрывать: сначала удалить состояние контроллера.
                    sessions.Disconnected(old.Key, old.Value);
                    active.Remove(old.Key);
                }
            }
            foreach (var entry in current)
            {
                SteamPlayer previous;
                if (active.TryGetValue(entry.Key, out previous) && !System.Object.ReferenceEquals(previous, entry.Value))
                {
                    onError(new InvalidOperationException("Previous player session is still listed."));
                    continue;
                }
                active[entry.Key] = entry.Value;
                sessions.Register(entry.Key, entry.Value);
                try
                {
                    var player = entry.Value.player;
                    var item = player.equipment == null ? HeldItem.Unknown : HeldItemClassifier.Classify(player.equipment.asset);
                    if (item != HeldItem.HousingPlanner && item != HeldItem.BuildingPart)
                    {
                        sessions.Update(entry.Key, entry.Value, item, 0, 0);
                        continue;
                    }
                    Vector3 point;
                    if (!AimPointQuery.TryGetTargetPoint(player, out point))
                    {
                        sessions.Suspend(entry.Key, entry.Value);
                        continue;
                    }
                    sessions.Update(entry.Key, entry.Value, item, point.x, point.z);
                }
                catch (Exception error)
                {
                    try { sessions.Suspend(entry.Key, entry.Value); }
                    catch (Exception cleanupError) { onError(cleanupError); }
                    onError(error);
                }
            }
        }

        private void FailClosed()
        {
            try { sessions.Clear(); }
            catch (Exception error) { onError(error); }
        }

        public void Clear() { sessions.Clear(); }
    }
}
