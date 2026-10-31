using System;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using Logger = Rocket.Core.Logging.Logger;

namespace MreInterface
{
    public sealed partial class MreInterfacePlugin
    {
        private static string DescribeRemoval(RemovalPreview preview)
        {
            if (preview == null) return "Наведи прицел на наземную постройку в пределах 6 метров. Постройки на транспорте исключены.";
            return "Цель " + preview.Kind + " item=" + preview.AssetId + " instance=" + preview.InstanceId + " owner=" + preview.OwnerId + " group=" + preview.GroupId + ". Удаление необратимо; содержимое хранилища может выпасть. За 15 секунд: /admin remove confirm " + preview.Token;
        }
        private void HandleStaffCommand(UnturnedPlayer actor, string action, string[] args)
        {
            try
            {
                string result;
                if (action == "remove")
                {
                    if (args.Length == 1) result = DescribeRemoval(PreviewRemoval(actor));
                    else if (args.Length == 3 && args[1].Equals("confirm", StringComparison.OrdinalIgnoreCase)) result = ConfirmRemoval(actor, args[2]);
                    else result = "/admin remove — проверить цель; /admin remove confirm <токен> — удалить.";
                }
                else if (action == "kill") result = ConfirmKill(actor, args[2]);
                else if (action == "spectate") result = StopObservation(actor);
                else if (action == "fly" && args.Length == 1) result = RunBodyFlight(actor, null);
                else
                {
                    bool enabled;
                    if (args.Length == 1) enabled = action == "freecam" || action == "workzone" || action == "stats" || !(action == "god" ? actor.GodMode : actor.VanishMode);
                    else if (args.Length == 2 && (args[1].Equals("on", StringComparison.OrdinalIgnoreCase) || args[1].Equals("off", StringComparison.OrdinalIgnoreCase))) enabled = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    else { UnturnedChat.Say(actor, "MRE: /admin " + action + " [on|off]"); return; }
                    result = action == "fly" ? RunBodyFlight(actor, enabled) : action == "freecam" ? SetFreecamPermission(actor, enabled) : action == "workzone" ? SetWorkzonePermission(actor, enabled) : action == "stats" ? SetStatsPermission(actor, enabled) : SetStaffMode(actor, action, enabled);
                }
                UnturnedChat.Say(actor, "MRE: " + result);
            }
            catch (UnauthorizedAccessException) { UnturnedChat.Say(actor, "MRE: нет прав на это действие."); }
            catch (Exception error) { Logger.LogException(error, "[MRE] Ошибка дополнительного действия администратора."); UnturnedChat.Say(actor, "MRE: действие не выполнено, ошибка записана в журнал."); }
        }
    }
}
