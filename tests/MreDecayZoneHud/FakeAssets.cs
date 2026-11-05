using System;
namespace SDG.Unturned
{
    public enum EBuild { FORTIFICATION, BARRICADE, BARRICADE_WALL, DOOR, GATE, LADDER, SHUTTER, GLASS, HATCH, GENERATOR, STORAGE, FARM, FREEFORM }
    public class UseableHousingPlanner { }
    public class CustomHousingPlanner : UseableHousingPlanner { }
    public class UseableTool { }
    public class ItemAsset { public Type useableType; }
    public class ItemStructureAsset : ItemAsset { }
    public class ItemBarricadeAsset : ItemAsset { public EBuild build; }
}
