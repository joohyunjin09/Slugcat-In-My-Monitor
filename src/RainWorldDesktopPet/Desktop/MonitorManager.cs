using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RainWorldDesktopPet.Desktop
{
    public enum DesktopTaskbarEdge
    {
        None,
        Left,
        Top,
        Right,
        Bottom
    }

    public sealed class MonitorInfo
    {
        public MonitorInfo(string name, Rectangle bounds, Rectangle workArea, bool primary)
        {
            Name = name ?? string.Empty;
            Bounds = bounds;
            WorkArea = workArea;
            Primary = primary;
            TerrainId = StableTerrainId(Name, bounds);

            int leftInset = Math.Max(0, workArea.Left - bounds.Left);
            int topInset = Math.Max(0, workArea.Top - bounds.Top);
            int rightInset = Math.Max(0, bounds.Right - workArea.Right);
            int bottomInset = Math.Max(0, bounds.Bottom - workArea.Bottom);
            int largestInset = Math.Max(Math.Max(leftInset, rightInset), Math.Max(topInset, bottomInset));
            if (largestInset == 0)
            {
                TaskbarEdge = DesktopTaskbarEdge.None;
                TaskbarBounds = Rectangle.Empty;
            }
            else if (bottomInset == largestInset)
            {
                TaskbarEdge = DesktopTaskbarEdge.Bottom;
                TaskbarBounds = Rectangle.FromLTRB(bounds.Left, workArea.Bottom,
                    bounds.Right, bounds.Bottom);
            }
            else if (topInset == largestInset)
            {
                TaskbarEdge = DesktopTaskbarEdge.Top;
                TaskbarBounds = Rectangle.FromLTRB(bounds.Left, bounds.Top,
                    bounds.Right, workArea.Top);
            }
            else if (leftInset == largestInset)
            {
                TaskbarEdge = DesktopTaskbarEdge.Left;
                TaskbarBounds = Rectangle.FromLTRB(bounds.Left, bounds.Top,
                    workArea.Left, bounds.Bottom);
            }
            else
            {
                TaskbarEdge = DesktopTaskbarEdge.Right;
                TaskbarBounds = Rectangle.FromLTRB(workArea.Right, bounds.Top,
                    bounds.Right, bounds.Bottom);
            }

            // A bottom taskbar/appbar is real terrain at WorkArea.Bottom. For
            // every other layout, the monitor's lower screen boundary is the
            // continuous desktop floor. This prevents a side/top taskbar from
            // creating a floor gap below the usable area.
            FloorY = TaskbarEdge == DesktopTaskbarEdge.Bottom
                ? workArea.Bottom
                : bounds.Bottom;
        }

        public readonly string Name;
        public readonly Rectangle Bounds;
        public readonly Rectangle WorkArea;
        public readonly bool Primary;
        public readonly long TerrainId;
        public readonly Rectangle TaskbarBounds;
        public readonly DesktopTaskbarEdge TaskbarEdge;
        public readonly int FloorY;

        private static long StableTerrainId(string name, Rectangle bounds)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offset;
                string identity = name.ToUpperInvariant() + "|" +
                    bounds.Left + "," + bounds.Top + "," + bounds.Width + "," + bounds.Height;
                for (int i = 0; i < identity.Length; i++)
                {
                    hash ^= identity[i];
                    hash *= prime;
                }
                long result = -(long)(hash & 0x3FFFFFFFFFFFFFFFUL) - 10000L;
                return result == 0 ? -10000L : result;
            }
        }
    }

    public static class MonitorManager
    {
        public static IList<MonitorInfo> GetMonitors()
        {
            List<MonitorInfo> result = new List<MonitorInfo>();
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                result.Add(new MonitorInfo(screens[i].DeviceName, screens[i].Bounds, screens[i].WorkingArea, screens[i].Primary));
            }

            return result;
        }

        public static Rectangle GetVirtualBounds()
        {
            return SystemInformation.VirtualScreen;
        }

        public static MonitorInfo FindNearest(Point point)
        {
            Screen screen = Screen.FromPoint(point);
            return new MonitorInfo(screen.DeviceName, screen.Bounds, screen.WorkingArea, screen.Primary);
        }

        public static MonitorInfo FindNearest(IList<MonitorInfo> monitors, Point point)
        {
            if (monitors == null || monitors.Count == 0) return FindNearest(point);
            MonitorInfo best = monitors[0];
            long bestDistance = DistanceSquared(best.Bounds, point);
            for (int i = 0; i < monitors.Count; i++)
            {
                if (monitors[i].Bounds.Contains(point)) return monitors[i];
                long distance = DistanceSquared(monitors[i].Bounds, point);
                if (distance < bestDistance)
                {
                    best = monitors[i];
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static long DistanceSquared(Rectangle rectangle, Point point)
        {
            int x = point.X < rectangle.Left ? rectangle.Left :
                (point.X >= rectangle.Right ? rectangle.Right - 1 : point.X);
            int y = point.Y < rectangle.Top ? rectangle.Top :
                (point.Y >= rectangle.Bottom ? rectangle.Bottom - 1 : point.Y);
            long dx = (long)point.X - x;
            long dy = (long)point.Y - y;
            return dx * dx + dy * dy;
        }
    }
}
