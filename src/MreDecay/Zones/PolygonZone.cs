using System;
using System.Collections.Generic;

namespace MreDecay.Zones
{
    public struct ZonePoint
    {
        public readonly double X;
        public readonly double Z;
        public ZonePoint(double x, double z) { X = x; Z = z; }
    }

    // Граница включается в зону; геометрия проверяется при создании.
    public sealed class PolygonZone
    {
        // Существенно больше размеров PEI; ограничение исключает переполнение вычислений.
        public const double MaxCoordinate = 1000000;
        private readonly ZonePoint[] points;
        public string Name { get; private set; }
        public PolygonZone(string name, IEnumerable<ZonePoint> vertices)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("Zone name is required.");
            if (vertices == null) throw new ArgumentNullException("vertices");
            Name = name;
            points = new List<ZonePoint>(vertices).ToArray();
            if (points.Length < 3) throw new ArgumentException("At least three vertices are required.");
            for (int i = 0; i < points.Length; i++) {
                if (!ValidCoordinate(points[i].X) || !ValidCoordinate(points[i].Z)) throw new ArgumentException("Vertices must be finite and within supported bounds.");
                for (int j = 0; j < i; j++)
                    if (points[i].X == points[j].X && points[i].Z == points[j].Z) throw new ArgumentException("Duplicate vertices are not allowed.");
            }
            double area = 0;
            for (int i = 0; i < points.Length; i++) {
                ZonePoint a = points[i], b = points[(i + 1) % points.Length];
                ZonePoint previous = points[(i + points.Length - 1) % points.Length];
                // Коллинеарное продолжение допустимо, разворот перекрывает соседнее ребро.
                if (Cross(previous, a, b) == 0 &&
                    (previous.X - a.X) * (b.X - a.X) + (previous.Z - a.Z) * (b.Z - a.Z) > 0)
                    throw new ArgumentException("Adjacent edges overlap.");
                area += a.X * b.Z - b.X * a.Z;
                for (int j = i + 1; j < points.Length; j++) {
                    if (j == i + 1 || (i == 0 && j == points.Length - 1)) continue;
                    if (Intersects(a, b, points[j], points[(j + 1) % points.Length])) throw new ArgumentException("Polygon edges intersect.");
                }
            }
            if (!Finite(area) || area == 0) throw new ArgumentException("Polygon area must be finite and nonzero.");
        }
        public bool Contains(double x, double z)
        {
            if (!ValidCoordinate(x) || !ValidCoordinate(z)) throw new ArgumentOutOfRangeException("x", "Query coordinates must be finite and within supported bounds.");
            var p = new ZonePoint(x, z);
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++) {
                ZonePoint a = points[j], b = points[i];
                if (OnSegment(a, b, p)) return true;
                if ((a.Z > z) != (b.Z > z) && x < (b.X - a.X) * (z - a.Z) / (b.Z - a.Z) + a.X) inside = !inside;
            }
            return inside;
        }
        private static bool Finite(double v) { return !Double.IsNaN(v) && !Double.IsInfinity(v); }
        private static bool ValidCoordinate(double v) { return Finite(v) && Math.Abs(v) <= MaxCoordinate; }
        private static double Cross(ZonePoint a, ZonePoint b, ZonePoint p) { return (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X); }
        private static bool OnSegment(ZonePoint a, ZonePoint b, ZonePoint p) {
            return Cross(a, b, p) == 0 && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Z >= Math.Min(a.Z, b.Z) && p.Z <= Math.Max(a.Z, b.Z);
        }
        private static bool Intersects(ZonePoint a, ZonePoint b, ZonePoint c, ZonePoint d) {
            double abC = Cross(a, b, c), abD = Cross(a, b, d), cdA = Cross(c, d, a), cdB = Cross(c, d, b);
            return (abC == 0 && OnSegment(a, b, c)) || (abD == 0 && OnSegment(a, b, d)) ||
                (cdA == 0 && OnSegment(c, d, a)) || (cdB == 0 && OnSegment(c, d, b)) ||
                ((abC > 0) != (abD > 0) && (cdA > 0) != (cdB > 0));
        }
    }

    public sealed class ZoneSet
    {
        private readonly PolygonZone[] zones;
        public ZoneSet(IEnumerable<PolygonZone> values) {
            if (values == null) throw new ArgumentNullException("values");
            zones = new List<PolygonZone>(values).ToArray();
            if (zones.Length == 0) throw new ArgumentException("At least one configured zone is required; an empty set is unknown, not outside.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (PolygonZone zone in zones)
                if (zone == null || !names.Add(zone.Name)) throw new ArgumentException("Zones must be non-null with unique names.");
            Array.Sort(zones, (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        }
        public string[] MatchingNames(double x, double z) {
            if (Double.IsNaN(x) || Double.IsInfinity(x) || Math.Abs(x) > PolygonZone.MaxCoordinate ||
                Double.IsNaN(z) || Double.IsInfinity(z) || Math.Abs(z) > PolygonZone.MaxCoordinate)
                throw new ArgumentOutOfRangeException("x", "Query coordinates must be finite and within supported bounds.");
            var result = new List<string>();
            foreach (PolygonZone zone in zones) if (zone.Contains(x, z)) result.Add(zone.Name);
            return result.ToArray();
        }
    }
}
