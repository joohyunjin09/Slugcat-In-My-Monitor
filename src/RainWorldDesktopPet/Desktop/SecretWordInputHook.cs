using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace RainWorldDesktopPet.Desktop
{
    internal sealed class SecretWordMatcher
    {
        private const string Secret = "SOFANTHIEL";
        private int matchedCharacters;

        internal bool AcceptVirtualKey(uint virtualKey)
        {
            char key = virtualKey >= 'A' && virtualKey <= 'Z'
                ? (char)virtualKey : '\0';
            if (key == Secret[matchedCharacters])
            {
                matchedCharacters++;
                if (matchedCharacters == Secret.Length)
                {
                    matchedCharacters = 0;
                    return true;
                }
                return false;
            }

            matchedCharacters = key == Secret[0] ? 1 : 0;
            return false;
        }
    }

    // A dedicated message-pump thread keeps the global keyboard hook out of
    // the render/UI path. It observes only the current secret-word prefix,
    // never stores typed text, and never suppresses keyboard input.
    internal sealed class SecretWordInputHook : IDisposable
    {
        private readonly Action matched;
        private readonly SecretWordMatcher matcher = new SecretWordMatcher();
        private readonly NativeMethods.LowLevelKeyboardProc callback;
        private readonly ManualResetEvent started = new ManualResetEvent(false);
        private readonly object stateLock = new object();
        private Thread thread;
        private IntPtr hook;
        private uint threadId;
        private Exception startupError;
        private bool disposed;

        internal SecretWordInputHook(Action matched)
        {
            if (matched == null) throw new ArgumentNullException("matched");
            this.matched = matched;
            callback = HookCallback;
        }

        internal void Start()
        {
            lock (stateLock)
            {
                if (disposed) throw new ObjectDisposedException(GetType().Name);
                if (thread != null) return;
                thread = new Thread(ThreadMain);
                thread.IsBackground = true;
                thread.Name = "Slugcat secret word input hook";
                thread.Start();
            }

            if (!started.WaitOne(5000))
                throw new TimeoutException("Timed out while starting the secret word input hook.");
            if (startupError != null)
                throw new InvalidOperationException(
                    "Unable to start the secret word input hook.", startupError);
        }

        private void ThreadMain()
        {
            try
            {
                threadId = NativeMethods.GetCurrentThreadId();
                NativeMethods.Message unused;
                NativeMethods.PeekMessage(out unused, IntPtr.Zero, 0, 0,
                    NativeMethods.PM_NOREMOVE);
                hook = NativeMethods.SetWindowsKeyboardHookEx(
                    NativeMethods.WH_KEYBOARD_LL, callback,
                    NativeMethods.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Unable to install the secret word input hook.");
                started.Set();

                NativeMethods.Message message;
                int result;
                while ((result = NativeMethods.GetMessage(out message,
                    IntPtr.Zero, 0, 0)) > 0)
                {
                    NativeMethods.TranslateMessage(ref message);
                    NativeMethods.DispatchMessage(ref message);
                }
                if (result < 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "The secret word input pump failed.");
            }
            catch (Exception exception)
            {
                startupError = exception;
                started.Set();
                Program.LogException(exception);
            }
            finally
            {
                IntPtr installed = hook;
                hook = IntPtr.Zero;
                if (installed != IntPtr.Zero)
                    NativeMethods.UnhookWindowsHookEx(installed);
                threadId = 0;
            }
        }

        private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int keyboardMessage = unchecked((int)message.ToInt64());
                if (keyboardMessage == NativeMethods.WM_KEYDOWN ||
                    keyboardMessage == NativeMethods.WM_SYSKEYDOWN)
                {
                    try
                    {
                        NativeMethods.LowLevelKeyboardHookData hookData =
                            (NativeMethods.LowLevelKeyboardHookData)Marshal.PtrToStructure(
                                data, typeof(NativeMethods.LowLevelKeyboardHookData));
                        if (matcher.AcceptVirtualKey(hookData.VirtualKey)) matched();
                    }
                    catch (Exception exception)
                    {
                        Program.LogException(exception);
                    }
                }
            }
            return NativeMethods.CallNextHookEx(hook, code, message, data);
        }

        public void Dispose()
        {
            Thread ownedThread;
            uint ownedThreadId;
            lock (stateLock)
            {
                if (disposed) return;
                disposed = true;
                ownedThread = thread;
                ownedThreadId = threadId;
                thread = null;
            }

            if (ownedThreadId != 0)
                NativeMethods.PostThreadMessage(ownedThreadId,
                    NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            if (ownedThread != null && ownedThread.IsAlive &&
                !ownedThread.Join(2000))
            {
                Program.LogException(new TimeoutException(
                    "Timed out while stopping the secret word input hook."));
            }
            started.Dispose();
        }
    }
}
