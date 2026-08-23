using System;
using System.Collections.Generic;
using System.Drawing;
using RainWorldDesktopPet.Desktop;

namespace RainWorldDesktopPet.Core
{
    public static class DesktopRecovery
    {
        public const int OffscreenGraceTicks = 40;
        private const int VisibleMarginPixels = 36;
        private const int HardEscapeMarginPixels = 260;
        private const int HorizontalFloorMarginPixels = 80;

        public static bool IsNearAnyMonitor(Vec2 simulationPosition,
            IList<MonitorInfo> monitors)
        {
            Vec2 desktop = DesktopWorldTransform.ToDesktop(simulationPosition);
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle bounds = Rectangle.Inflate(monitors[i].Bounds,
                    VisibleMarginPixels, VisibleMarginPixels);
                if (desktop.X >= bounds.Left && desktop.X < bounds.Right &&
                    desktop.Y >= bounds.Top && desktop.Y < bounds.Bottom) return true;
            }
            return false;
        }

        public static bool IsFarOutsideVirtualDesktop(Vec2 simulationPosition,
            Rectangle virtualBounds)
        {
            Vec2 desktop = DesktopWorldTransform.ToDesktop(simulationPosition);
            Rectangle hardBounds = Rectangle.Inflate(virtualBounds,
                HardEscapeMarginPixels, HardEscapeMarginPixels);
            return desktop.X < hardBounds.Left || desktop.X >= hardBounds.Right ||
                desktop.Y < hardBounds.Top || desktop.Y >= hardBounds.Bottom;
        }

        public static bool IsAboveMonitorCeiling(Vec2 simulationPosition,
            IList<MonitorInfo> monitors)
        {
            if (monitors == null) return false;
            Vec2 desktop = DesktopWorldTransform.ToDesktop(simulationPosition);
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle bounds = monitors[i].Bounds;
                if (desktop.X >= bounds.Left - VisibleMarginPixels &&
                    desktop.X < bounds.Right + VisibleMarginPixels &&
                    desktop.Y < bounds.Top) return true;
            }
            return false;
        }

        public static Vec2 FindSafeHipsPosition(Vec2 preferredSimulationPosition,
            IList<MonitorInfo> monitors, double hipsRadius)
        {
            if (monitors == null || monitors.Count == 0) return preferredSimulationPosition;
            Vec2 desktop = DesktopWorldTransform.ToDesktop(preferredSimulationPosition);
            Point point = new Point((int)Math.Round(desktop.X), (int)Math.Round(desktop.Y));
            MonitorInfo monitor = MonitorManager.FindNearest(monitors, point);
            int left = monitor.WorkArea.Left + HorizontalFloorMarginPixels;
            int right = monitor.WorkArea.Right - HorizontalFloorMarginPixels;
            double x = right > left
                ? MathUtil.Clamp(desktop.X, left, right)
                : monitor.WorkArea.Left + monitor.WorkArea.Width * 0.5;
            double y = monitor.FloorY - DesktopWorldTransform.ToDesktopLength(hipsRadius + 2.0);
            return DesktopWorldTransform.ToSimulation(new Vec2(x, y));
        }
    }
}
