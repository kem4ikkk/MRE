using System;
using MreImmunity;
class Program
{
    static void Check(bool value) { if (!value) throw new Exception("SyncGate check failed"); }
    static void Main()
    {
        var gate = new SyncGate<string>();
        Check(gate.ShouldSend("a", 0, 2));
        Check(!gate.ShouldSend("a", 1.99, 2));
        Check(gate.ShouldSend("b", 1.99, 2));
        Check(gate.ShouldSend("a", 2, 2));
        Check(gate.ShouldSend("a", 1, 2)); // reset on monotonic clock rollover
        gate.Remove("a"); Check(gate.ShouldSend("a", 1, 2));
        gate.Clear(); Check(gate.ShouldSend("b", 2, 2));
        Console.WriteLine("7 SyncGate checks passed");
    }
}
