using System;
using MreAirdrop;

internal static class Program
{
    private static void Main()
    {
        int count = 0;
        if (!DryDropSearch.IsDry((x, z) => { count++; return true; }) || count != 121)
            throw new Exception("Неверная сетка поиска.");
        foreach (var wet in new[] { (-50, -50), (0, 0), (50, 50) })
            if (DryDropSearch.IsDry((x, z) => x != wet.Item1 || z != wet.Item2))
                throw new Exception("Влажная точка принята.");
        Console.WriteLine("Проверка сетки суши пройдена.");
    }
}
