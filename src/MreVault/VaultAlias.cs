using System;

namespace MreVault
{
    public static class VaultAlias
    {
        public static bool IsMatch(string text)
        {
            return text != null && String.Equals(text.Trim(), ".ы", StringComparison.OrdinalIgnoreCase);
        }
    }
}
