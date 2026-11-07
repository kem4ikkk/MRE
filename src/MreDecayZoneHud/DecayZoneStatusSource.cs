namespace MreDecayZoneHud
{
    // Runtime implementation may return true only after decay and zone geometry
    // have both been confirmed. A DRAFT zone set must report unavailable.
    public interface IDecayZoneStatusSource
    {
        bool IsOperational { get; }
        bool ZonesConfirmed { get; }
        bool TryGetAcceleratedZone(double x, double z, out bool accelerated);
    }

    // Current MRE decay runtime is unavailable and draft zones are not usable.
    // Until a reviewed production source exists, every target query fails closed.
    public sealed class UnavailableDecayZoneStatusSource : IDecayZoneStatusSource
    {
        public bool IsOperational => false;
        public bool ZonesConfirmed => false;
        public bool TryGetAcceleratedZone(double x, double z, out bool accelerated)
        {
            accelerated = false;
            return false;
        }
    }
}
