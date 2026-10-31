using System;

namespace MreInterface
{
    internal sealed class DamageCapture
    {
        private readonly byte before;
        private bool observed;
        public byte Damage { get; private set; }
        public DamageCapture(byte before) { this.before = before; }
        public void Observe(byte after)
        {
            if (observed) return;
            observed = true;
            Damage = (byte)Math.Max(0, before - after);
        }
    }

    internal static class SafeEventDispatch
    {
        internal static void Publish<T>(Action<T> handlers, T value, Action<Exception> onError)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(value); } catch (Exception error) { if (onError != null) onError(error); }
        }
    }
}
