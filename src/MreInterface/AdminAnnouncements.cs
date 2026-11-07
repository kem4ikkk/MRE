using System;
using System.Globalization;
using System.Linq;
using Rocket.Unturned.Player;
using MreShared;

namespace MreInterface
{
    public sealed partial class MreInterfacePlugin
    {
        // Typed entry point for the admin effect. The caller is supplied by its
        // server-side session resolver, never by client text.
        public string PublishAdminAnnouncement(UnturnedPlayer actor, string kind, string value)
        {
            RequireGameThread();
            if (!CanExecuteAdmin(actor, "announce")) return "Нет права mre.admin.announce.";
            if (kind == "event")
            {
                string text = (value ?? String.Empty).Trim();
                if (text.Length == 0 || text.Length > 180 || text.Any(Char.IsControl))
                    return "Текст события должен содержать от 1 до 180 символов в одной строке.";
                return BroadcastAnnouncement(actor, kind, "Событие: " + text);
            }
            if (kind == "restart")
            {
                int minutes;
                if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes < 1 || minutes > 1440)
                    return "Укажите время от 1 до 1440 минут. Объявление не запускает перезапуск.";
                return BroadcastAnnouncement(actor, kind, "Планируемый перезапуск сервера через " + minutes + " мин. Сохранитесь и завершите активность.");
            }
            return "Выберите объявление события или перезапуска.";
        }

        private string BroadcastAnnouncement(UnturnedPlayer actor, string kind, string message)
        {
            MreChat.SayServer("Объявление администрации: " + message);
            Rocket.Core.Logging.Logger.Log("[MRE ADMIN] actor=" + actor.CSteamID.m_SteamID + " action=announce." + kind + " message=" + message);
            return "Объявление отправлено всем игрокам.";
        }
    }
}
