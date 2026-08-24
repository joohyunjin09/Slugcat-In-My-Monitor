using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RainWorldDesktopPet.Desktop
{
    internal static class NativeMethods
    {
        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int WS_EX_TRANSPARENT = 0x00000020;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_TOPMOST = 0x00000008;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int ULW_ALPHA = 0x00000002;
        internal const byte AC_SRC_OVER = 0x00;
        internal const byte AC_SRC_ALPHA = 0x01;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_CANCELMODE = 0x001F;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int WM_CAPTURECHANGED = 0x0215;
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_DISPLAYCHANGE = 0x007E;
        internal const int WM_DPICHANGED = 0x02E0;
        internal const int HTTRANSPARENT = -1;
        internal const int HTCLIENT = 1;
        internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        internal const int DWMWA_CLOAKED = 14;
        internal const uint GA_ROOT = 2;
        internal const uint MONITOR_DEFAULTTONEAREST = 2;
        internal const int VK_LBUTTON = 0x01;
        internal const int VK_RBUTTON = 0x02;
        internal const int VK_MBUTTON = 0x04;
        internal const int VREFRESH = 116;

        internal delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

        internal const int WHDR_DONE = 0x00000001;
        internal const int WHDR_PREPARED = 0x00000002;
        internal const int WHDR_BEGINLOOP = 0x00000004;
        internal const int WHDR_ENDLOOP = 0x00000008;
        internal const int WAVE_FORMAT_PCM = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct WAVEFORMATEX
        {
            internal ushort wFormatTag;
            internal ushort nChannels;
            internal uint nSamplesPerSec;
            internal uint nAvgBytesPerSec;
            internal ushort nBlockAlign;
            internal ushort wBitsPerSample;
            internal ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WAVEHDR
        {
            internal IntPtr lpData;
            internal int dwBufferLength;
            internal int dwBytesRecorded;
            internal IntPtr dwUser;
            internal int dwFlags;
            internal int dwLoops;
            internal IntPtr lpNext;
            internal IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X;
            internal int Y;

            internal Point(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Size
        {
            internal int Width;
            internal int Height;

            internal Size(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;

            internal int Width { get { return Right - Left; } }
            internal int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct BlendFunction
        {
            internal byte BlendOp;
            internal byte BlendFlags;
            internal byte SourceConstantAlpha;
            internal byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfoHeader
        {
            internal uint Size;
            internal int Width;
            internal int Height;
            internal ushort Planes;
            internal ushort BitCount;
            internal uint Compression;
            internal uint SizeImage;
            internal int XPelsPerMeter;
            internal int YPelsPerMeter;
            internal uint ColorsUsed;
            internal uint ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfo
        {
            internal BitmapInfoHeader Header;
            internal uint Colors;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeMessage
        {
            internal IntPtr Handle;
            internal uint Message;
            internal IntPtr WParam;
            internal IntPtr LParam;
            internal uint Time;
            internal Point Point;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr handle,
            uint minimumFilter, uint maximumFilter, uint removeMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr handle, out Rect rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr handle, StringBuilder className, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr handle, uint flags);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out Rect value, int valueSize);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int valueSize);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmFlush();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr handle, IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr objectHandle);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateDIBSection(
            IntPtr deviceContext,
            ref BitmapInfo bitmapInfo,
            uint usage,
            out IntPtr bits,
            IntPtr section,
            uint offset);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(IntPtr objectHandle);

        [DllImport("winmm.dll")]
        internal static extern int waveOutOpen(
            out IntPtr hWaveOut,
            IntPtr uDeviceID,
            ref WAVEFORMATEX lpFormat,
            IntPtr dwCallback,
            IntPtr dwInstance,
            int fdwOpen);

        [DllImport("winmm.dll")]
        internal static extern int waveOutPrepareHeader(
            IntPtr hWaveOut,
            IntPtr lpWaveHdr,
            int uSize);

        [DllImport("winmm.dll")]
        internal static extern int waveOutWrite(
            IntPtr hWaveOut,
            IntPtr lpWaveHdr,
            int uSize);

        [DllImport("winmm.dll")]
        internal static extern int waveOutUnprepareHeader(
            IntPtr hWaveOut,
            IntPtr lpWaveHdr,
            int uSize);

        [DllImport("winmm.dll")]
        internal static extern int waveOutReset(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        internal static extern int waveOutClose(IntPtr hWaveOut);

        [DllImport("gdi32.dll")]
        internal static extern int GetDeviceCaps(IntPtr deviceContext, int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateLayeredWindow(
            IntPtr handle,
            IntPtr destinationDeviceContext,
            ref Point destination,
            ref Size size,
            IntPtr sourceDeviceContext,
            ref Point source,
            int colorKey,
            ref BlendFunction blend,
            int flags);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetCapture(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        internal static void EnableDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
                {
                    return;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }

            SetProcessDPIAware();
        }

        internal static bool IsMessageQueueIdle()
        {
            NativeMessage message;
            return !PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
        }

        internal static double GetPrimaryDisplayRefreshRate()
        {
            IntPtr deviceContext = GetDC(IntPtr.Zero);
            if (deviceContext == IntPtr.Zero) return 0.0;
            try
            {
                int refresh = GetDeviceCaps(deviceContext, VREFRESH);
                return refresh > 1 ? refresh : 0.0;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, deviceContext);
            }
        }
    }
}
