using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace RainWorldDesktopPet.Desktop
{
    // WinEvent can fire much faster than the 40 Hz simulation while a window
    // is dragged. Keep only a fixed, de-duplicated set of HWNDs so the hook
    // never allocates or grows memory in response to event volume.
    internal sealed class WindowLocationChangeTracker
    {
        internal const int Capacity = 8;
        private readonly object sync = new object();
        private readonly IntPtr[] handles = new IntPtr[Capacity];
        private int count;
        private int replacementIndex;

        internal void Record(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            lock (sync)
            {
                for (int i = 0; i < count; i++)
                    if (handles[i] == handle) return;

                if (count < handles.Length)
                {
                    handles[count++] = handle;
                    return;
                }

                handles[replacementIndex] = handle;
                replacementIndex = (replacementIndex + 1) % handles.Length;
            }
        }

        internal int Drain(IntPtr[] destination)
        {
            if (destination == null || destination.Length < handles.Length)
                throw new ArgumentException("A fixed-capacity destination is required.",
                    "destination");

            lock (sync)
            {
                int drained = count;
                for (int i = 0; i < drained; i++)
                {
                    destination[i] = handles[i];
                    handles[i] = IntPtr.Zero;
                }
                count = 0;
                replacementIndex = 0;
                return drained;
            }
        }

        internal void Clear()
        {
            lock (sync)
            {
                for (int i = 0; i < count; i++) handles[i] = IntPtr.Zero;
                count = 0;
                replacementIndex = 0;
            }
        }
    }

    public sealed class DesktopWindowSnapshot
    {
        public IntPtr Handle;
        public Rectangle Bounds;
        public string Title;
        public string ClassName;
    }

    public sealed class WindowEnumerator
    {
        private const uint WindowTextTimeoutMilliseconds = 50;
        private readonly uint currentProcessId = (uint)Process.GetCurrentProcess().Id;

        public bool LastEnumerationSucceeded { get; private set; }

        public IList<DesktopWindowSnapshot> Enumerate(IntPtr overlayHandle)
        {
            List<DesktopWindowSnapshot> result = new List<DesktopWindowSnapshot>(64);
            LastEnumerationSucceeded = NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                DesktopWindowSnapshot snapshot;
                if (TryGetWindow(handle, overlayHandle, out snapshot))
                {
                    result.Add(snapshot);
                }

                return true;
            }, IntPtr.Zero);
            if (!LastEnumerationSucceeded) result.Clear();
            return result;
        }

        public bool IsWindowAlive(IntPtr handle)
        {
            if (!NativeMethods.IsWindow(handle) || !NativeMethods.IsWindowVisible(handle) ||
                NativeMethods.IsIconic(handle)) return false;
            int cloaked;
            return NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWA_CLOAKED,
                out cloaked, sizeof(int)) != 0 || cloaked == 0;
        }

        // Location-change events use this path. It intentionally avoids class
        // names, titles, EnumWindows, and SendMessageTimeout; only one already
        // known top-level HWND is validated and measured.
        internal bool TryGetWindowBounds(IntPtr handle, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle) ||
                !NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle) ||
                NativeMethods.GetAncestor(handle, NativeMethods.GA_ROOT) != handle)
                return false;

            uint processId;
            NativeMethods.GetWindowThreadProcessId(handle, out processId);
            if (processId == currentProcessId) return false;

            int cloaked;
            if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWA_CLOAKED,
                    out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            NativeMethods.Rect rect;
            int rectSize = Marshal.SizeOf(typeof(NativeMethods.Rect));
            if (NativeMethods.DwmGetWindowAttribute(handle,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, rectSize) != 0 &&
                !NativeMethods.GetWindowRect(handle, out rect))
                return false;

            if (rect.Width < 80 || rect.Height < 40 ||
                rect.Left <= -30000 || rect.Top <= -30000)
                return false;

            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return true;
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

            Rectangle bounds;
            if (!TryGetWindowBounds(handle, out bounds)) return false;

            snapshot = new DesktopWindowSnapshot();
            snapshot.Handle = handle;
            snapshot.Bounds = bounds;
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
            UIntPtr result;
            NativeMethods.SendMessageTimeout(handle, NativeMethods.WM_GETTEXT,
                new UIntPtr((uint)builder.Capacity), builder,
                NativeMethods.SMTO_BLOCK | NativeMethods.SMTO_ABORTIFHUNG,
                WindowTextTimeoutMilliseconds, out result);
            return builder.ToString();
        }
    }
}
