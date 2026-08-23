using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace RainWorldDesktopPet.Desktop
{
    public sealed class DesktopWindowSnapshot
    {
        public IntPtr Handle;
        public Rectangle Bounds;
        public string Title;
        public string ClassName;
    }

    public sealed class WindowEnumerator
    {
        private readonly uint currentProcessId = (uint)Process.GetCurrentProcess().Id;

        public IList<DesktopWindowSnapshot> Enumerate(IntPtr overlayHandle)
        {
            List<DesktopWindowSnapshot> result = new List<DesktopWindowSnapshot>(64);
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                DesktopWindowSnapshot snapshot;
                if (TryGetWindow(handle, overlayHandle, out snapshot))
                {
                    result.Add(snapshot);
                }

                return true;
            }, IntPtr.Zero);
            return result;
        }

        private bool TryGetWindow(IntPtr handle, IntPtr overlayHandle, out DesktopWindowSnapshot snapshot)
        {
            snapshot = null;
            if (handle == overlayHandle || !NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle))
            {
                return false;
            }

            if (NativeMethods.GetAncestor(handle, NativeMethods.GA_ROOT) != handle)
            {
                return false;
            }

            uint processId;
            NativeMethods.GetWindowThreadProcessId(handle, out processId);
            if (processId == currentProcessId)
            {
                return false;
            }

            int cloaked;
            if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return false;
            }

            string className = ReadClassName(handle);
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd")
            {
                return false;
            }

            NativeMethods.Rect rect;
            int rectSize = Marshal.SizeOf(typeof(NativeMethods.Rect));
            if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, rectSize) != 0)
            {
                if (!NativeMethods.GetWindowRect(handle, out rect))
                {
                    return false;
                }
            }

            if (rect.Width < 80 || rect.Height < 40 || rect.Left <= -30000 || rect.Top <= -30000)
            {
                return false;
            }

            snapshot = new DesktopWindowSnapshot();
            snapshot.Handle = handle;
            snapshot.Bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            snapshot.ClassName = className;
            snapshot.Title = ReadWindowText(handle);
            return true;
        }

        private static string ReadClassName(IntPtr handle)
        {
            StringBuilder builder = new StringBuilder(256);
            NativeMethods.GetClassName(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private static string ReadWindowText(IntPtr handle)
        {
            StringBuilder builder = new StringBuilder(512);
            NativeMethods.GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }
    }
}
