using System;
namespace MreInterface
{
    public static class MarkerRules
    {
        public static bool IsInsideMap(float x, float z, float size)
        {
            return size > 0 && !float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(z) && !float.IsInfinity(z)
                && Math.Abs(x) < size / 2f && Math.Abs(z) < size / 2f;
        }
    }
}
