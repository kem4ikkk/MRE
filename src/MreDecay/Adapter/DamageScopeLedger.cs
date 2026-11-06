using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MreDecay.Adapter
{
    // Pure call-scope accounting. It is not a hook and does not establish whether an
    // instigator is a real player or whether every health change passes through a hook.
    // One ledger belongs to one game thread. Uncertainty permanently fences it.
    public sealed class DamageScopeLedger
    {
        public enum Result { Unknown, NoPlayerDamage, PlayerDamage }
        public sealed class Scope
        {
            internal object Target;
            internal int Before;
            internal bool? Player;
            internal bool Ambiguous;
        }
        private readonly List<Scope> stack = new List<Scope>();
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private bool faulted;
        public bool Faulted { get { return faulted; } }
        public bool RepairAllowed { get { return !faulted && stack.Count == 0; } }
        public Scope Begin(object target, int health, bool? confirmedPlayer)
        {
            if (faulted || Thread.CurrentThread.ManagedThreadId != thread || target == null || health <= 0 || !confirmedPlayer.HasValue)
            { faulted = true; return null; }
            var scope = new Scope { Target = target, Before = health, Player = confirmedPlayer };
            foreach (var other in stack)
                if (ReferenceEquals(other.Target, target)) { other.Ambiguous = true; scope.Ambiguous = true; }
            stack.Add(scope);
            return scope;
        }
        // Caller must verify that the same object remains live and readable, and invoke
        // this from a finalizer even on exception. Exceptions and destruction are unknown.
        public Result End(Scope scope, object target, int? healthAfter, bool completedNormally, bool sameObjectAlive)
        {
            if (faulted || Thread.CurrentThread.ManagedThreadId != thread || scope == null || stack.Count == 0 ||
                !ReferenceEquals(stack[stack.Count - 1], scope)) { faulted = true; return Result.Unknown; }
            stack.RemoveAt(stack.Count - 1);
            if (!completedNormally || !sameObjectAlive || !ReferenceEquals(scope.Target, target) || scope.Ambiguous ||
                !healthAfter.HasValue || healthAfter.Value < 0 || healthAfter.Value > scope.Before)
            { faulted = true; return Result.Unknown; }
            return healthAfter.Value < scope.Before && scope.Player.Value ? Result.PlayerDamage : Result.NoPlayerDamage;
        }
        // An unobserved operation or an incomplete hook must fence the ledger.
        public void Invalidate() { faulted = true; }
    }
}
