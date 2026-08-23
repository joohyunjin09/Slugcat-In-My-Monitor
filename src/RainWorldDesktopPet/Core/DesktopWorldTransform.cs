using System.Drawing;

namespace RainWorldDesktopPet.Core
{
    // Rain World simulation units stay untouched. Windows APIs and the overlay
    // use desktop pixels, so the 2.2x presentation/travel scale is applied only
    // at this boundary and never inside BodyChunk or procedural-part updates.
    public static class DesktopWorldTransform
    {
        public static Vec2 ToSimulation(Vec2 desktopPoint)
        {
            return desktopPoint / SimulationConstants.DesktopWorldScale;
        }

        public static Vec2 ToSimulation(Point desktopPoint)
        {
            return ToSimulation(Vec2.FromPoint(desktopPoint));
        }

        public static Vec2 ToSimulationDelta(Vec2 desktopDelta)
        {
            return desktopDelta / SimulationConstants.DesktopWorldScale;
        }

        public static double ToSimulationLength(double desktopLength)
        {
            return desktopLength / SimulationConstants.DesktopWorldScale;
        }

        public static Vec2 ToDesktop(Vec2 simulationPoint)
        {
            return simulationPoint * SimulationConstants.DesktopWorldScale;
        }

        public static Vec2 ToDesktopDelta(Vec2 simulationDelta)
        {
            return simulationDelta * SimulationConstants.DesktopWorldScale;
        }

        public static double ToDesktopLength(double simulationLength)
        {
            return simulationLength * SimulationConstants.DesktopWorldScale;
        }
    }
}
