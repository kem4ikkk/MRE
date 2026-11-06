using System;
using System.Collections.Generic;

namespace MreDecayZoneHud
{
    // Транспорт вызывается только на игровом потоке и должен направлять эффект
    // конкретному текущему соединению игрока. Успех вызова не подтверждает доставку.
    public interface IHintEffectTransport
    {
        void Create(ulong playerId);
        void SetText(ulong playerId, string element, string text);
        void SetVisible(ulong playerId, string element, bool visible);
        void Clear(ulong playerId);
    }

    // Отдельный Effect должен иметь уникальные ID/GUID/key и узлы prefab ниже.
    // Жизненным циклом и повторами владеет HintController/будущий адаптер.
    public sealed class EffectHintSink : IHintSink
    {
        public const string Panel = "DecayZonePanel";
        public const string Label = "DecayZoneText";
        private readonly IHintEffectTransport transport;
        private readonly HashSet<ulong> created = new HashSet<ulong>();

        public EffectHintSink(IHintEffectTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }
        public void Show(ulong playerId, string text)
        {
            if (playerId == 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (!created.Contains(playerId))
            {
                // Даже исключение может означать частичную отправку эффекта.
                created.Add(playerId);
                transport.Create(playerId);
            }
            transport.SetText(playerId, Label, text);
            transport.SetVisible(playerId, Panel, true);
        }
        public void Hide(ulong playerId)
        {
            if (playerId == 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            if (!created.Contains(playerId)) return;
            transport.Clear(playerId);
            created.Remove(playerId);
        }

        // Использовать вместе с HintController.ForgetDisconnected только после
        // подтверждённого разрыва конкретной старой клиентской сессии.
        public void ForgetDisconnected(ulong playerId)
        {
            if (playerId == 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            created.Remove(playerId);
        }
    }
}
