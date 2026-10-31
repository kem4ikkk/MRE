using System;

namespace MreInterface
{
    public static class AdminPolicy
    {
        public static readonly string[] Actions = { "players", "tp", "bring", "heal", "kick", "save", "day", "night", "fly", "freecam", "spectate", "god", "vanish", "remove", "owner", "workzone", "stats", "door", "storage", "freeze", "kill" };
        public static bool KnownAction(string action) { return Array.IndexOf(Actions, action) >= 0; }
        public static bool CanExecute(string action, Func<string, bool> hasPermission)
        {
            if (action == "freecam") action = "fly";
            return KnownAction(action) && hasPermission != null && hasPermission("mre.admin") && hasPermission("mre.admin." + action);
        }
    }
}
