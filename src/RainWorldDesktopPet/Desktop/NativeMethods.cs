using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RainWorldDesktopPet.Desktop
{
    internal static class NativeMethods
    {
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_TOPMOST = 0x00000008;
        internal const int WS_EX_TRANSPARENT = 0x00000020;
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        internal const int WH_MOUSE_LL = 14;
        internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        internal const int SWP_NOSIZE = 0x0001;
        internal const int SWP_NOMOVE = 0x0002;
        internal const int SWP_NOACTIVATE = 0x0010;
        internal const int SWP_NOOWNERZORDER = 0x0200;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_CANCELMODE = 0x001F;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int WM_LBUTTONDBLCLK = 0x0203;
        internal const int WM_CAPTURECHANGED = 0x0215;
        internal const int WM_DISPLAYCHANGE = 0x007E;
        internal const int WM_DPICHANGED = 0x02E0;
        internal const int WM_GETTEXT = 0x000D;
        internal const int WM_POWERBROADCAST = 0x0218;
        internal const int WM_QUIT = 0x0012;
        internal const int PBT_APMRESUMECRITICAL = 0x0006;
        internal const int PBT_APMRESUMESUSPEND = 0x0007;
        internal const int PBT_APMSUSPEND = 0x0004;
        internal const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        internal const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
        internal const uint SMTO_BLOCK = 0x0001;
        internal const uint SMTO_ABORTIFHUNG = 0x0002;
        internal const uint PM_NOREMOVE = 0x0000;
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
        internal const int ProcessPowerThrottling = 4;
        internal const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        internal const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        internal const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x2;
        internal const uint DesiredTimerResolutionMilliseconds = 1;

        internal delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
        internal delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);
        internal delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr handle,
            int objectId, int childId, uint eventThread, uint eventTime);

        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        internal const int WHDR_DONE = 0x00000001;
        internal const int WHDR_PREPARED = 0x00000002;
        internal const int WHDR_BEGINLOOP = 0x00000004;
        internal const int WHDR_ENDLOOP = 0x00000008;
        internal const int WAVE_FORMAT_PCM = 1;
        internal const int CALLBACK_EVENT = 0x00050000;

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
        internal struct LowLevelMouseHookData
        {
            internal Point Point;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Message
        {
            internal IntPtr Window;
            internal uint Value;
            internal UIntPtr WParam;
            internal IntPtr LParam;
            internal uint Time;
            internal Point Point;
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

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessPowerThrottlingState
        {
            internal uint Version;
            internal uint ControlMask;
            internal uint StateMask;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SendMessageTimeout(IntPtr handle, uint message,
            UIntPtr wParam, StringBuilder lParam, uint flags, uint timeout,
            out UIntPtr result);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr handle, uint flags);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out Rect value, int valueSize);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int valueSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int hookId,
            LowLevelMouseProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code,
            IntPtr message, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetMessage(out Message message, IntPtr window,
            uint minimumMessage, uint maximumMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PeekMessage(out Message message, IntPtr window,
            uint minimumMessage, uint maximumMessage, uint removeMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll")]
        internal static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostThreadMessage(uint threadId, uint message,
            UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
            IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter,
            int x, int y, int width, int height, int flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr handle, int message,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        internal static extern IntPtr SetCapture(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetCapture();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr RegisterSuspendResumeNotification(
            IntPtr recipient, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterSuspendResumeNotification(
            IntPtr registrationHandle);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr handle, IntPtr deviceContext);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateDC(string driver, string device,
            string output, IntPtr initializationData);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(IntPtr deviceContext);

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
        internal static extern int waveOutSetVolume(IntPtr hWaveOut, uint dwVolume);

        [DllImport("winmm.dll")]
        internal static extern int waveOutUnprepareHeader(
            IntPtr hWaveOut,
            IntPtr lpWaveHdr,
            int uSize);

        [DllImport("winmm.dll")]
        internal static extern int waveOutReset(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        internal static extern int waveOutClose(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint period);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessInformation(IntPtr process,
            int processInformationClass, ref ProcessPowerThrottlingState processInformation,
            int processInformationSize);

        [DllImport("gdi32.dll")]
        internal static extern int GetDeviceCaps(IntPtr deviceContext, int index);

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

        internal static ProcessPowerThrottlingState CreateInteractivePowerThrottlingState()
        {
            ProcessPowerThrottlingState state = new ProcessPowerThrottlingState();
            state.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
            state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED |
                PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION;
            // Taking control with a cleared StateMask disables EcoQoS and makes
            // Windows honor this process's timer-resolution request.
            state.StateMask = 0;
            return state;
        }

        internal static bool ConfigureInteractiveProcessPowerPolicy()
        {
            try
            {
                ProcessPowerThrottlingState state =
                    CreateInteractivePowerThrottlingState();
                return SetProcessInformation(GetCurrentProcess(),
                    ProcessPowerThrottling, ref state,
                    Marshal.SizeOf(typeof(ProcessPowerThrottlingState)));
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        internal static bool BeginHighResolutionTimer()
        {
            return timeBeginPeriod(DesiredTimerResolutionMilliseconds) == 0;
        }

        internal static void EndHighResolutionTimer()
        {
            timeEndPeriod(DesiredTimerResolutionMilliseconds);
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

        internal static double GetDisplayRefreshRate(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return GetPrimaryDisplayRefreshRate();
            IntPtr deviceContext = CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (deviceContext == IntPtr.Zero) return 0.0;
            try
            {
                int refresh = GetDeviceCaps(deviceContext, VREFRESH);
                return refresh > 1 ? refresh : 0.0;
            }
            finally
            {
                DeleteDC(deviceContext);
            }
        }
    }
}
