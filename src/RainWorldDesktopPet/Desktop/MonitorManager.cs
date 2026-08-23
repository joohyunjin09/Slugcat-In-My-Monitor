using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RainWorldDesktopPet.Desktop
{
    public sealed class MonitorInfo
    {
        public MonitorInfo(string name, Rectangle bounds, Rectangle workArea, bool primary)
        {
            Name = name;
            Bounds = bounds;
            WorkArea = workArea;
            Primary = primary;
        }

        public readonly string Name;
        public readonly Rectangle Bounds;
        public readonly Rectangle WorkArea;
        public readonly bool Primary;
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
    }
}
