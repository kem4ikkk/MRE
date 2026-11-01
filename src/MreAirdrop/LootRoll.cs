using System;
using System.Collections.Generic;

namespace MreAirdrop
{
    // Чистая логика вероятностей: не выбирает состав и не запускает события.
    internal static class LootRoll
    {
        internal static bool Percent(double percent, double random)
        {
            if (Double.IsNaN(percent) || Double.IsInfinity(percent) || percent < 0 || percent > 100) throw new ArgumentOutOfRangeException("percent");
            if (Double.IsNaN(random) || random < 0 || random >= 1) throw new ArgumentOutOfRangeException("random");
            return random * 100 < percent;
        }
        internal static int WeightedIndex(IList<double> weights, double random)
        {
            if (weights == null || weights.Count == 0) throw new ArgumentException("weights");
            if (Double.IsNaN(random) || random < 0 || random >= 1) throw new ArgumentOutOfRangeException("random");
            double total = 0;
            foreach (double w in weights)
            {
                if (Double.IsNaN(w) || Double.IsInfinity(w) || w < 0) throw new ArgumentOutOfRangeException("weights");
                total += w;
            }
            if (total <= 0 || Double.IsInfinity(total)) throw new ArgumentException("Нужен положительный конечный общий вес.");
            double target = random * total, current = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                current += weights[i];
                if (target < current) return i;
            }
            // Возможное округление double: возвращать последнюю ненулевую запись.
            for (int i = weights.Count - 1; i >= 0; i--) if (weights[i] > 0) return i;
            throw new InvalidOperationException();
        }
    }
}
