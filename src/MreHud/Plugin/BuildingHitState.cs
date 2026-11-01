using System;

namespace MreHud
{
    // Снимок привязан к одному игроку и одному подтвержденному событию удара.
    public sealed class BuildingHitState
    {
        public string Name;
        public int Maximum;
        public Func<int> ReadHealth;
        public DateTime ExpiresUtc;

        public static bool IsEligible(bool isPunch, bool alive, bool emptyHand)
        {
            return isPunch && alive && emptyHand;
        }

        public static bool IsValidatedInput(float distanceSquared, bool buildingType)
        {
            return buildingType && distanceSquared >= 0 && distanceSquared <= 36;
        }

        public bool IsVisible(DateTime now, bool alive)
        {
            return alive && ReadHealth != null && Maximum > 0 && now < ExpiresUtc;
        }
    }
}
