using System.Collections.Generic;
using System.Collections.ObjectModel;
using RainWorldDesktopPet.Desktop;

namespace RainWorldDesktopPet.Physics
{
    // Immutable terrain view captured at a window-enumeration boundary. One
    // instance is shared by every BodyChunk collision in a simulation tick.
    public sealed class DesktopCollisionSnapshot
    {
        internal DesktopCollisionSnapshot(long version, IList<DesktopSurface> surfaces,
            IList<MonitorInfo> monitors)
        {
            Version = version;
            Surfaces = new ReadOnlyCollection<DesktopSurface>(new List<DesktopSurface>(surfaces));
            Monitors = new ReadOnlyCollection<MonitorInfo>(new List<MonitorInfo>(monitors));
        }

        public readonly long Version;
        public readonly IList<DesktopSurface> Surfaces;
        public readonly IList<MonitorInfo> Monitors;
    }
}
