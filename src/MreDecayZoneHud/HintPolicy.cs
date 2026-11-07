namespace MreDecayZoneHud
{
    // Классификацию игрового предмета и зоны обязан подтвердить вызывающий адаптер.
    public enum HeldItem { Unknown, Other, HousingPlanner, BuildingPart }

    public readonly struct Hint
    {
        public readonly bool Visible;
        public readonly string Text;
        public Hint(bool visible, string text) { Visible = visible; Text = text; }
    }

    public static class HintPolicy
    {
        public static Hint Evaluate(bool playerAvailable, HeldItem item, bool? acceleratedZone)
        {
            if (!playerAvailable || (item != HeldItem.HousingPlanner && item != HeldItem.BuildingPart))
                return new Hint(false, string.Empty);
            if (!acceleratedZone.HasValue) return new Hint(false, string.Empty);
            return acceleratedZone.Value
                ? new Hint(true, "Постройка гниёт быстрее — 2% / 5 мин")
                : new Hint(true, "Постройка гниёт с обычной скоростью — 1% / 5 мин");
        }
    }
}
