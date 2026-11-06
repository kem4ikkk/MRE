namespace MreDecay.Adapter
{
    // Explicit product scope. Unknown/new types are excluded, never inferred from name.
    public static class PlacementPolicy
    {
        public static bool Included(string kind, string build)
        {
            if (kind == "structure") return true;
            if (kind != "barricade") return false;
            switch (build) {
                case "FORTIFICATION": case "BARRICADE": case "BARRICADE_WALL":
                case "DOOR": case "GATE": case "SHUTTER": case "HATCH":
                case "STORAGE": case "STORAGE_WALL": case "GENERATOR": return true;
                default: return false;
            }
        }
    }
}
