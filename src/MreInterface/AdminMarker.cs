using System;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;

namespace MreInterface
{
    public sealed partial class MreInterfacePlugin
    {
        private string TeleportToMarker(UnturnedPlayer actor)
        {
            // Административный перенос обходит обычные задержки телепортации.
            // Не выдаём эту возможность игрокам только по Rocket permission.
            if (!actor.IsAdmin) return "Телепорт по метке доступен только игровому администратору.";
            if (observations.ContainsKey(actor.CSteamID.m_SteamID)) return "Сначала завершите наблюдение: /admin spectate off.";
            if (actor.Player.life.isDead || actor.Player.movement.getVehicle() != null)
                return "Выйдите из транспорта и убедитесь, что персонаж жив.";
            var quests = actor.Player.quests;
            if (quests == null || !quests.isMarkerPlaced) return "Сначала поставьте личную метку на карте.";
            Vector3 marker = quests.markerPosition;
            if (!MarkerRules.IsInsideMap(marker.x, marker.z, Level.size))
                return "Метка находится вне карты.";

            // Высота метки на карте не является высотой поверхности. Ищем её сверху.
            var origin = new Vector3(marker.x, 1024f, marker.z);
            RaycastHit ground;
            if (!Physics.Raycast(origin, Vector3.down, out ground, 2048f, RayMasks.WAYPOINT, QueryTriggerInteraction.Ignore) || ground.normal.y < 0.7f)
                return "Не удалось найти подходящую поверхность под меткой.";
            RaycastHit water;
            if (Physics.Raycast(origin, Vector3.down, out water, 2048f, RayMasks.WATER, QueryTriggerInteraction.Collide) && water.point.y >= ground.point.y)
                return "Метка находится над водой.";
            var position = ground.point + Vector3.up * 0.15f;
            if (!Level.isPointWithinValidHeight(position.y) || Physics.CheckCapsule(position + Vector3.up * 0.45f, position + Vector3.up * 1.5f, 0.35f, RayMasks.BLOCK_EXIT_FIND_GROUND, QueryTriggerInteraction.Ignore))
                return "Недостаточно свободного места для телепортации.";
            if (!actor.Player.teleportToLocation(position, actor.Player.look.yaw)) return "Телепортация отклонена игровым сервером.";
            Audit(actor, "marker", 0);
            return "Телепортация к личной метке выполнена.";
        }
    }
}
