using System;

namespace MreAirdrop
{
    internal static class DryDropSearch
    {
        // Квадрат 100×100 м; дискретная сетка снижает риск, но не гарантирует посадку.
        internal const int Radius = 50;
        internal const int Step = 10;
        internal const int Attempts = 32;
        internal static bool IsDry(Func<int, int, bool> sample)
        {
            if (sample == null) throw new ArgumentNullException("sample");
            for (int x = -Radius; x <= Radius; x += Step)
                for (int z = -Radius; z <= Radius; z += Step)
                    if (!sample(x, z)) return false;
            return true;
        }
    }
}
