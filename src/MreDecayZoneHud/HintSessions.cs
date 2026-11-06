using System;
using System.Collections.Generic;

namespace MreDecayZoneHud
{
    public delegate bool TryAcceleratedZone(double x, double z, out bool accelerated);

    // Единственный Unity-поток. Идентификатор session — объект соединения;
    // для выхода вызывающая сторона должна подтвердить, что оно отключено.
    public sealed class HintSessions
    {
        private readonly Dictionary<ulong, object> sessions = new Dictionary<ulong, object>();
        private readonly EffectHintSink sink;
        private readonly HintController controller;
        private readonly TryAcceleratedZone zone;

        public HintSessions(EffectHintSink sink, TryAcceleratedZone zone)
        {
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.zone = zone ?? throw new ArgumentNullException(nameof(zone));
            controller = new HintController(sink);
        }

        public void Register(ulong playerId, object session)
        {
            if (playerId == 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            if (session == null) throw new ArgumentNullException(nameof(session));
            object previous;
            if (sessions.TryGetValue(playerId, out previous))
            {
                if (!Object.ReferenceEquals(previous, session))
                    throw new InvalidOperationException("Previous session has not been confirmed disconnected.");
            }
            else sessions.Add(playerId, session);
        }

        public void Update(ulong playerId, object session, HeldItem item, double x, double z)
        {
            Register(playerId, session);
            bool accelerated = false;
            bool ready;
            try { ready = zone(x, z, out accelerated); }
            catch { ready = false; }
            controller.Update(playerId, true, ready, item, ready ? (bool?)accelerated : null);
        }

        public void Suspend(ulong playerId, object session)
        {
            Register(playerId, session);
            controller.Update(playerId, true, false, HeldItem.Unknown, null);
        }

        public void Disconnected(ulong playerId, object session)
        {
            object previous;
            if (!sessions.TryGetValue(playerId, out previous) || !Object.ReferenceEquals(previous, session))
                throw new InvalidOperationException("Disconnect does not match the tracked session.");
            controller.ForgetDisconnected(playerId);
            sink.ForgetDisconnected(playerId);
            sessions.Remove(playerId);
        }

        public void Clear() { controller.Clear(); }
    }
}
