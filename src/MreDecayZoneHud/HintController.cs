using System;
using System.Collections.Generic;

namespace MreDecayZoneHud
{
    public interface IHintSink
    {
        void Show(ulong playerId, string text);
        void Hide(ulong playerId);
    }

    // Вызывается только на игровом потоке. Состояние поставщика зон и
    // работа decay должны подтверждаться вызывающим адаптером, не конфигом игрока.
    public sealed class HintController
    {
        private readonly IHintSink sink;
        private readonly Dictionary<ulong, string> visible = new Dictionary<ulong, string>();
        public HintController(IHintSink sink) { this.sink = sink ?? throw new ArgumentNullException(nameof(sink)); }

        public void Update(ulong playerId, bool playerAvailable, bool operational, HeldItem item, bool? acceleratedZone)
        {
            if (playerId == 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            Hint hint = operational ? HintPolicy.Evaluate(playerAvailable, item, acceleratedZone) : new Hint(false, string.Empty);
            string previous;
            bool hadHint = visible.TryGetValue(playerId, out previous);
            if (!hint.Visible)
            {
                if (hadHint) { sink.Hide(playerId); visible.Remove(playerId); }
                return;
            }
            // После исключения Show эффект мог быть отправлен частично. Сначала
            // очистить неопределённое состояние, затем безопасно создать заново.
            if (hadHint && previous == null)
            {
                sink.Hide(playerId);
                visible.Remove(playerId);
                hadHint = false;
            }
            if (!hadHint || !String.Equals(previous, hint.Text, StringComparison.Ordinal))
            {
                // Записать возможное наличие эффекта до операции с неизвестным исходом.
                visible[playerId] = null;
                sink.Show(playerId, hint.Text);
                visible[playerId] = hint.Text;
            }
        }

        public void Remove(ulong playerId)
        {
            if (visible.ContainsKey(playerId)) { sink.Hide(playerId); visible.Remove(playerId); }
        }

        public void Clear()
        {
            var errors = new List<Exception>();
            foreach (ulong id in new List<ulong>(visible.Keys))
                try { Remove(id); }
                catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Не удалось очистить часть подсказок.", errors);
        }
    }
}
