using System;
using MreDecay.Zones;
class Program {
    static ZonePoint P(double x, double z) { return new ZonePoint(x,z); }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(ZonePoint[] points) { try { new PolygonZone("bad", points); } catch (ArgumentException) { return; } throw new Exception("invalid accepted"); }
    static int Main() {
        var square = new PolygonZone("z", new[] { P(0,0), P(10,0), P(10,10), P(0,10) });
        Check(square.Contains(5,5) && !square.Contains(11,5), "inside/outside");
        Check(square.Contains(0,0) && square.Contains(10,10) && square.Contains(5,0) && square.Contains(0,5), "edges/corners");
        Check(!square.Contains(-0.001,5), "outside");
        try { square.Contains(double.NaN,0); throw new Exception("NaN query accepted"); } catch (ArgumentOutOfRangeException) { }
        try { square.Contains(0,double.PositiveInfinity); throw new Exception("infinite query accepted"); } catch (ArgumentOutOfRangeException) { }
        try { square.Contains(double.MaxValue,0); throw new Exception("extreme query accepted"); } catch (ArgumentOutOfRangeException) { }
        var concave = new PolygonZone("a", new[] { P(0,0), P(4,0), P(4,1), P(1,1), P(1,4), P(0,4) });
        Check(concave.Contains(.5,3) && !concave.Contains(2,2) && concave.Contains(1,1), "concave");
        var set = new ZoneSet(new[] { square, concave });
        Check(String.Join(",", set.MatchingNames(.5,.5)) == "a,z", "overlap order");
        Check(set.MatchingNames(30,30).Length == 0, "no match");
        try { set.MatchingNames(0,double.NaN); throw new Exception("invalid set query accepted"); } catch (ArgumentOutOfRangeException) { }
        try { new ZoneSet(new PolygonZone[0]); throw new Exception("empty set accepted"); } catch (ArgumentException) { }
        Reject(new[] { P(0,0), P(1,1) });
        Reject(new[] { P(0,0), P(1,1), P(2,2) });
        Reject(new[] { P(0,0), P(2,2), P(0,2), P(2,0) });
        Reject(new[] { P(0,0), P(2,0), P(2,2), P(0,0) });
        Reject(new[] { P(0,0), P(double.NaN,1), P(2,0) });
        Reject(new[] { P(0,0), P(double.MaxValue,1), P(2,0) });
        Reject(new[] { P(0,0), P(4,0), P(2,0), P(2,2), P(0,2) });
        var collinear = new PolygonZone("collinear", new[] { P(0,0), P(2,0), P(4,0), P(4,2), P(0,2) });
        Check(collinear.Contains(3,0) && collinear.Contains(2,1), "collinear continuation");
        try { new ZoneSet(new[] { square, square }); throw new Exception("duplicate accepted"); } catch (ArgumentException) { }
        Console.WriteLine("Zone geometry checks passed."); return 0;
    }
}
