using System;
namespace MreDecay.Adapter
{
    // Test-only scheduling: uptime only; never catch up after stalls or downtime.
    public sealed class ProbeTimer
    {
        private double last, accumulated;
        private bool started, faulted;
        public bool Faulted { get { return faulted; } }
        public bool Start(double now) {
            if (started || faulted || !Valid(now)) { faulted = true; return false; }
            last = now; started = true; return true;
        }
        public bool Poll(double now) {
            if (!started || faulted || !Valid(now) || now < last) { faulted = true; return false; }
            double elapsed = now - last; last = now;
            if (elapsed >= 300 - accumulated) { accumulated = 0; return true; }
            accumulated += elapsed; return false;
        }
        private static bool Valid(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n) && n >= 0; }
    }
}
