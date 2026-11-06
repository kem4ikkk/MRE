using System;
using MreSpawnProtection;
class Program
{
    static void Main()
    {
        var p = new ProtectionTimer();
        Check(p.Remaining(1, 10) == 0); p.Start(1, 10, 15); p.Start(2, 12, 15);
        Check(p.Remaining(1, 10) == 15); Check(p.Remaining(1, 24.5) == 0.5); Check(p.Remaining(1, 25) == 0);
        Check(p.Remaining(2, 25) == 2); Check(p.Remove(2)); Check(p.Remaining(2, 25) == 0);
        p.Start(1, 30, 15); Check(p.Remaining(1, 31) == 14); p.Clear(); Check(p.Remaining(1, 31) == 0);
        p.Start(0, 0, 15); Check(p.Remaining(0, 1) == 0);
        Check(ProtectionPolicy.IsAttack(AttackItem.Gun, true, false)); Check(!ProtectionPolicy.IsAttack(AttackItem.Gun, false, true));
        Check(ProtectionPolicy.IsAttack(AttackItem.Melee, false, true)); Check(ProtectionPolicy.IsAttack(AttackItem.Empty, false, true));
        Check(ProtectionPolicy.IsAttack(AttackItem.Throwable, true, false)); Check(ProtectionPolicy.IsAttack(AttackItem.Detonator, true, false));
        Check(!ProtectionPolicy.IsAttack(AttackItem.Other, true, true)); Check(!ProtectionPolicy.IsAttack(AttackItem.Gun, false, false));
        var session = new object(); Check(ProtectionPolicy.SameSession(session, session)); Check(!ProtectionPolicy.SameSession(session, new object())); Check(!ProtectionPolicy.SameSession(null, null));
        Console.WriteLine("Проверки таймера и политики пройдены.");
    }
    static void Check(bool ok) { if (!ok) throw new Exception("Проверка таймера не пройдена."); }
}
