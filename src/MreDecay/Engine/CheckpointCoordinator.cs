using System;
using System.Threading;

namespace MreDecay.Engine
{
    // Orchestrates a proposed transaction, but depends on an external backend which
    // must actually provide durable world/state/anchor semantics. Never use synthetic
    // or incomplete proofs to construct such a backend for production.
    public interface ICheckpointBackend
    {
        bool VerifyCurrent(long generation);
        bool WriteIntent(long generation, byte[] proposedState);
        bool ApplyWorld();
        bool SaveAndVerifyWorld();
        bool PublishStateAndAnchor(long generation, byte[] proposedState);
    }
    public sealed class CheckpointCoordinator
    {
        private readonly ICheckpointBackend backend;
        private long generation;
        private bool started, faulted, pending, entering;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        public bool Faulted { get { return faulted; } }
        public bool Ready { get { return Thread.CurrentThread.ManagedThreadId == ownerThread && started && !faulted && !pending && !entering; } }
        public CheckpointCoordinator(ICheckpointBackend backend) { this.backend = backend ?? throw new ArgumentNullException("backend"); }
        public bool Start(long expectedGeneration)
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread || started || faulted || entering || expectedGeneration < 0) return Fail();
            entering = true;
            try {
                bool verified = backend.VerifyCurrent(expectedGeneration);
                if (!verified || faulted) return Fail();
                generation = expectedGeneration; started = true; return true;
            } catch (Exception) { return Fail(); }
            finally { entering = false; }
        }
        public bool Execute(byte[] proposedState)
        {
            if (!Ready || generation == long.MaxValue || proposedState == null || proposedState.Length == 0) return Fail();
            pending = true; entering = true;
            try {
                byte[] immutable = (byte[])proposedState.Clone();
                long next = generation + 1;
                bool ok = backend.VerifyCurrent(generation); if (!ok || faulted) return Fail();
                ok = backend.WriteIntent(next, (byte[])immutable.Clone()); if (!ok || faulted) return Fail();
                ok = backend.ApplyWorld(); if (!ok || faulted) return Fail();
                ok = backend.SaveAndVerifyWorld(); if (!ok || faulted) return Fail();
                ok = backend.PublishStateAndAnchor(next, (byte[])immutable.Clone()); if (!ok || faulted) return Fail();
                ok = backend.VerifyCurrent(next); if (!ok || faulted) return Fail();
                generation = next; pending = false; return true;
            } catch (Exception) { return Fail(); }
            finally { entering = false; }
        }
        private bool Fail() { faulted = true; return false; }
    }
}
