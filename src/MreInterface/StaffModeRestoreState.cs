namespace MreInterface
{
    // Снимок исходных режимов учитывает отзыв нашего отдельного god/vanish grant
    // во время наблюдения, чтобы завершение не возвращало уже отозванный режим.
    internal sealed class StaffModeRestoreState
    {
        public bool God { get; private set; }
        public bool Vanish { get; private set; }
        public StaffModeRestoreState(bool god, bool vanish) { God = god; Vanish = vanish; }
        public void RevokeOwnGrant(string mode)
        {
            if (mode == "god") God = false;
            if (mode == "vanish") Vanish = false;
        }
    }
}
