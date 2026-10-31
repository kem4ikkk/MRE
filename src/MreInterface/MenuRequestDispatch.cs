using System;

namespace MreInterface
{
    internal static class MenuRequestDispatch
    {
        public static bool TryOpen<T>(Func<T, bool> listeners, T actor, Action<Exception> reportError)
        {
            if (listeners == null) return false;
            foreach (Func<T, bool> listener in listeners.GetInvocationList())
            {
                try { if (listener(actor)) return true; }
                catch (Exception error) { if (reportError != null) reportError(error); }
            }
            return false;
        }
    }
}
