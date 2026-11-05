using SDG.Unturned;

namespace MreDecayZoneHud
{
    // Runtime-интеграция ещё не подключена: классифицируются реальные ItemAsset API.
    public static class HeldItemClassifier
    {
        public static HeldItem Classify(ItemAsset asset)
        {
            if (asset == null) return HeldItem.Unknown;
            if (asset.useableType != null && typeof(UseableHousingPlanner).IsAssignableFrom(asset.useableType))
                return HeldItem.HousingPlanner;
            if (asset is ItemStructureAsset) return HeldItem.BuildingPart;
            var barricade = asset as ItemBarricadeAsset;
            if (barricade == null) return HeldItem.Other;
            switch (barricade.build)
            {
                case EBuild.FORTIFICATION:
                case EBuild.BARRICADE:
                case EBuild.BARRICADE_WALL:
                case EBuild.DOOR:
                case EBuild.GATE:
                case EBuild.LADDER:
                case EBuild.SHUTTER:
                case EBuild.GLASS:
                case EBuild.HATCH:
                    return HeldItem.BuildingPart;
                default:
                    return HeldItem.Other;
            }
        }
    }
}
