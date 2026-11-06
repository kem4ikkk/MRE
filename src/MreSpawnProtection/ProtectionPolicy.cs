namespace MreSpawnProtection
{
    public enum AttackItem { Empty, Gun, Melee, Throwable, Detonator, Other }
    public static class ProtectionPolicy
    {
        public static bool IsAttack(AttackItem item, bool primary, bool secondary)
        {
            return (primary && item != AttackItem.Other) || (secondary && (item == AttackItem.Empty || item == AttackItem.Melee || item == AttackItem.Throwable));
        }
        public static bool SameSession(object tracked, object current) { return tracked != null && object.ReferenceEquals(tracked, current); }
    }
}
