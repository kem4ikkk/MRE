using SDG.Unturned;
using UnityEngine;

namespace MreDecayZoneHud
{
    // Candidate query for the crosshair target. No hit or missing look data means
    // there is no reliable target point, so the caller must hide the status.
    public static class AimPointQuery
    {
        // The game API does not expose HousingConnections' placement limit as a public constant.
        public const float MaxAimDistance = 16f;
        private static readonly int TargetMask = ~(RayMasks.PLAYER | RayMasks.VIEWMODEL | RayMasks.IGNORE_RAYCAST | RayMasks.UI);

        public static bool TryGetTargetPoint(Player player, out Vector3 point)
        {
            point = default(Vector3);
            if (player == null || player.look == null || player.look.aim == null) return false;
            Transform aim = player.look.aim;
            Vector3 direction = aim.forward;
            if (direction.sqrMagnitude < 0.0001f) return false;
            RaycastHit hit;
            if (!Physics.Raycast(aim.position, direction, out hit, MaxAimDistance,
                TargetMask, QueryTriggerInteraction.Ignore)) return false;
            point = hit.point;
            return true;
        }
    }
}
