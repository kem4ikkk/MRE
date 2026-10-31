using System;
using System.Linq;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreInterface
{
    public sealed class RemovalPreview
    {
        public string Token { get; internal set; }
        public string Kind { get; internal set; }
        public ushort AssetId { get; internal set; }
        public uint InstanceId { get; internal set; }
        public ulong OwnerId { get; internal set; }
        public ulong GroupId { get; internal set; }
        public DateTime ExpiresUtc { get; internal set; }
    }

    public sealed class OwnershipInfo
    {
        public string Kind { get; internal set; }
        public ushort AssetId { get; internal set; }
        public uint InstanceId { get; internal set; }
        public ulong OwnerId { get; internal set; }
        public ulong GroupId { get; internal set; }
        public bool IsBed { get; internal set; }
        public ulong BedClaimantId { get; internal set; }
    }

    public sealed partial class MreInterfacePlugin
    {
        private sealed class RemovalTarget
        {
            public Transform Model;
            public Vector3 Position;
            public BarricadeDrop Barricade;
            public StructureDrop Structure;
            public RemovalPreview Preview;
        }
        private readonly ConfirmationStore<RemovalTarget> removals = new ConfirmationStore<RemovalTarget>();

        public OwnershipInfo InspectOwnership(UnturnedPlayer actor)
        {
            RequireGameThread();
            if (!CanExecuteAdmin(actor, "owner")) throw new UnauthorizedAccessException("Нет права mre.admin.owner.");
            var target = FindAimedRemoval(actor);
            if (target == null) return null;
            var bed = target.Barricade == null ? null : target.Barricade.interactable as InteractableBed;
            var result = new OwnershipInfo { Kind=target.Preview.Kind, AssetId=target.Preview.AssetId,
                InstanceId=target.Preview.InstanceId, OwnerId=target.Preview.OwnerId, GroupId=target.Preview.GroupId,
                IsBed=bed != null, BedClaimantId=bed != null ? bed.owner.m_SteamID : 0 };
            Logger.Log("[MRE ADMIN OWNER] actor=" + actor.CSteamID.m_SteamID + " kind=" + result.Kind + " instance=" + result.InstanceId);
            return result;
        }

        private static string DescribeOwnership(OwnershipInfo info)
        {
            if (info == null) return "Наведи прицел на наземную постройку в пределах 6 метров.";
            return "Объект " + info.Kind + " item=" + info.AssetId + " instance=" + info.InstanceId +
                " owner=" + info.OwnerId + " group=" + info.GroupId + (info.IsBed ? " bed_claimant=" + info.BedClaimantId : "");
        }

        private static RemovalTarget FindAimedRemoval(UnturnedPlayer actor)
        {
            if (actor.Player.life.isDead) return null;
            var aim = actor.Player.look.aim;
            // Includes occluding terrain, world objects, vehicles and other players.
            var hit = DamageTool.raycast(new Ray(aim.position, aim.forward), 6f,
                RayMasks.DAMAGE_SERVER | RayMasks.VEHICLE | RayMasks.PLAYER | RayMasks.ENEMY, actor.Player);
            if (hit == null || hit.transform == null) return null;
            var barricade = BarricadeManager.FindBarricadeByRootTransform(hit.transform);
            if (barricade != null)
            {
                // Vehicle attachments are excluded from this first safe removal tool.
                if (barricade.IsChildOfVehicle) return null;
                var data = barricade.GetServersideData();
                if (data == null) return null;
                return new RemovalTarget { Model = barricade.model, Position = barricade.model.position, Barricade = barricade,
                    Preview = new RemovalPreview { Kind = "barricade", AssetId = barricade.asset.id, InstanceId = barricade.instanceID, OwnerId = data.owner, GroupId = data.group } };
            }
            var structure = StructureManager.FindStructureByRootTransform(hit.transform);
            if (structure == null) return null;
            var structureData = structure.GetServersideData();
            if (structureData == null) return null;
            return new RemovalTarget { Model = structure.model, Position = structure.model.position, Structure = structure,
                Preview = new RemovalPreview { Kind = "structure", AssetId = structure.asset.id, InstanceId = structure.instanceID, OwnerId = structureData.owner, GroupId = structureData.group } };
        }

        public RemovalPreview PreviewRemoval(UnturnedPlayer actor)
        {
            RequireGameThread();
            if (!CanExecuteAdmin(actor, "remove")) throw new UnauthorizedAccessException("Нет права mre.admin.remove.");
            removals.Cancel(actor.CSteamID.m_SteamID);
            var target = FindAimedRemoval(actor);
            if (target == null) return null;
            DateTime now = DateTime.UtcNow;
            target.Preview.Token = removals.Issue(actor.CSteamID.m_SteamID, target, now);
            target.Preview.ExpiresUtc = now.AddSeconds(15);
            return target.Preview;
        }

        public string ConfirmRemoval(UnturnedPlayer actor, string token)
        {
            RequireGameThread();
            if (!CanExecuteAdmin(actor, "remove"))
            {
                if (actor != null) removals.Cancel(actor.CSteamID.m_SteamID);
                return "Нет права на удаление.";
            }
            RemovalTarget target;
            if (!removals.TryConsume(actor.CSteamID.m_SteamID, token, DateTime.UtcNow, out target)) return "Подтверждение истекло или неверно. Сначала /admin remove.";
            RemovalTarget current = FindAimedRemoval(actor);
            if (current == null || target.Model == null || !ReferenceEquals(current.Model, target.Model) ||
                !ReferenceEquals(current.Barricade, target.Barricade) || !ReferenceEquals(current.Structure, target.Structure) ||
                current.Preview.InstanceId != target.Preview.InstanceId || current.Preview.OwnerId != target.Preview.OwnerId ||
                current.Preview.GroupId != target.Preview.GroupId || (current.Position - target.Position).sqrMagnitude > 0.01f)
                return "Цель изменилась: снова наведи прицел и выполни /admin remove.";
            byte x, y;
            if (target.Barricade != null)
            {
                ushort plant; BarricadeRegion region;
                if (!BarricadeManager.tryGetRegion(target.Model, out x, out y, out plant, out region) || plant != ushort.MaxValue ||
                    !region.drops.Any(drop => ReferenceEquals(drop, target.Barricade))) return "Постройка больше не находится в исходном регионе.";
                BarricadeManager.destroyBarricade(target.Barricade, x, y, plant);
            }
            else
            {
                StructureRegion region;
                if (!StructureManager.tryGetRegion(target.Model, out x, out y, out region) ||
                    !region.drops.Any(drop => ReferenceEquals(drop, target.Structure))) return "Постройка больше не находится в исходном регионе.";
                StructureManager.destroyStructure(target.Structure, x, y, Vector3.zero, true);
            }
            Logger.Log("[MRE ADMIN REMOVE] actor=" + actor.CSteamID.m_SteamID + " kind=" + target.Preview.Kind + " asset=" + target.Preview.AssetId + " instance=" + target.Preview.InstanceId + " owner=" + target.Preview.OwnerId + " group=" + target.Preview.GroupId + " position=" + target.Position);
            return "Постройка удалена. Возврат предмета администратору не выполняется.";
        }
    }
}
