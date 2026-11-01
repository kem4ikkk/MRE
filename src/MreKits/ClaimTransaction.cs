using System;

namespace MreKits
{
    public interface IKitDelivery
    {
        bool Preflight();
        bool Deliver();
        bool Rollback();
    }
    public static class ClaimTransaction
    {
        public static string Execute(KitLedger ledger, ulong player, string kit, DateTime now, int cooldown, IKitDelivery delivery)
        {
            if (!ledger.Available(player, kit, now)) return "unavailable";
            if (!delivery.Preflight()) return "inventory_full";
            if (!ledger.Reserve(player, kit, now, cooldown)) return "unavailable";
            try {
                if (!delivery.Deliver()) throw new InvalidOperationException("Выдача kit отклонена инвентарем.");
                ledger.Complete(player, kit);
                return "completed";
            } catch {
                // При неполном откате pending сохраняется и повторная выдача запрещена.
                if (delivery.Rollback()) ledger.Cancel(player, kit);
                throw;
            }
        }
    }
}
