namespace RestoreMonarchy.Teleportation.Utils
{
    internal static class RaidOwnershipRules
    {
        // Нулевой group означает отсутствие группы, а не общую группу всех одиночек.
        public static bool IsOwnerOrGroupMember(ulong owner, ulong group, ulong player, ulong playerGroup)
        {
            return owner == player || (group != 0 && group == playerGroup);
        }
    }
}
