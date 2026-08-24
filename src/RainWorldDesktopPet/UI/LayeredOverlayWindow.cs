using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.UI
{
    public sealed class LayeredOverlayWindow : Form
    {
        private const int MaximumSlugcats = 8;
        private const int DefaultRenderFramesPerSecond = 60;
        private const int DefaultRenderIntervalMilliseconds =
            (1000 + DefaultRenderFramesPerSecond - 1) / DefaultRenderFramesPerSecond;
        private const int MinimumOverlaySize = 384;
        private const int OverlaySizeQuantum = 128;
        private const int OverlayPadding = 24;
        private const int WmEnsureTopMost = 0x8001;
        private readonly RainWorldInstallation installation;
        private readonly SlugcatId startSlugcat;
        private readonly Timer renderTimer;
        private readonly NotifyIcon trayIcon;
        private readonly Icon applicationIcon;
        private readonly ToolStripMenuItem slugcatMenu;
        private readonly ToolStripMenuItem refreshWorkshopItem;
        private readonly ToolStripMenuItem debugItem;
        private readonly ToolStripMenuItem retryRenderItem;
        private readonly ToolStripMenuItem pauseItem;
        private readonly ToolStripMenuItem activeSlugcatsMenu;
        private readonly ToolStripMenuItem spawnItem;
        private readonly ToolStripMenuItem removeItem;
        private readonly ToolStripMenuItem skinEditorItem;
        private readonly List<GameLoop> gameLoops = new List<GameLoop>();
        private readonly SlugcatPose[] poseBuffer = new SlugcatPose[MaximumSlugcats];
        private readonly DirectCompositionHost.GpuSmokeEffect[] smokeEffectBuffer =
            new DirectCompositionHost.GpuSmokeEffect[256];
        private readonly List<Rectangle> surfaceBoundsBuffer =
            new List<Rectangle>(MaximumSlugcats);
        private readonly CompositionBatchPlanner compositionBatchPlanner =
            new CompositionBatchPlanner();
        private readonly Dictionary<string, double> displayRefreshRates =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly DesktopCollisionWorld collisionWorld =
            new DesktopCollisionWorld(new WindowEnumerator());
        private readonly Stopwatch surfaceRefreshClock = Stopwatch.StartNew();
        private DirectCompositionHost compositionHost;
        private readonly string startDmsSkinId;
        private GameLoop gameLoop;
        private GameLoop grabbedGameLoop;
        private readonly NativeMethods.LowLevelMouseProc mouseHookCallback;
        private readonly NativeMethods.WinEventProc foregroundEventCallback;
        private IntPtr mouseHook;
        private IntPtr foregroundEventHook;
        private SettingsWindow settingsWindow;
        private SkinEditorWindow skinEditor;
        private Rectangle virtualDesktopBounds;
        private bool mouseCaptured;
        private bool leftButtonDown;
        private int renderErrorCount;
        private bool renderingEnabled;
        private bool renderingFrame;
        private double displayRefreshRate;

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatId startSlugcat)
            : this(installation, startDebug, startSlugcat, null)
        {
        }

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatId startSlugcat, string startDmsSkinId)
        {
            this.installation = installation;
            this.startSlugcat = startSlugcat;
            this.startDmsSkinId = startDmsSkinId;
            mouseHookCallback = MouseHookCallback;
            foregroundEventCallback = ForegroundEventCallback;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            virtualDesktopBounds = MonitorManager.GetVirtualBounds();
            Bounds = virtualDesktopBounds;
            Text = "SlugcatInMyMonitor";
            applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (applicationIcon != null) Icon = applicationIcon;

            renderTimer = new Timer();
            // Start conservatively, then follow the refresh rate of the
            // monitor(s) occupied by active Slugcats after the first frame.
            renderTimer.Interval = DefaultRenderIntervalMilliseconds;
            renderTimer.Tick += RenderTimerTick;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem settingsItem = new ToolStripMenuItem(T("설정 열기", "Open Settings"));
            settingsItem.Click += OpenSettings;
            debugItem = new ToolStripMenuItem(T("디버그 오버레이", "Debug Overlay"));
            debugItem.CheckOnClick = true;
            debugItem.Checked = startDebug;
            debugItem.CheckedChanged += delegate
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].DebugEnabled = debugItem.Checked;
                if (compositionHost != null) compositionHost.ResetSurfaces();
                RefreshSettingsWindow();
            };
            pauseItem = new ToolStripMenuItem(T("모든 슬러그캣 일시 정지", "Pause All Slugcats"));
            pauseItem.CheckOnClick = true;
            pauseItem.CheckedChanged += delegate
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].Paused = pauseItem.Checked;
                RefreshSettingsWindow();
            };
            retryRenderItem = new ToolStripMenuItem(T("렌더링 재시도", "Retry Rendering"));
            retryRenderItem.Enabled = false;
            retryRenderItem.Click += RetryRendering;
            skinEditorItem = new ToolStripMenuItem(T("스킨 편집기 (실험적)", "Skin Editor (Experimental)"));
            skinEditorItem.Click += ToggleSkinEditor;
            ToolStripMenuItem exitItem = new ToolStripMenuItem(T("종료", "Exit"));
            exitItem.Click += delegate { Close(); };
            slugcatMenu = new ToolStripMenuItem(T("캐릭터와 능력", "Character and Ability"));
            for (int i = 0; i < SlugcatProfiles.All.Count; i++)
            {
                SlugcatProfile profile = SlugcatProfiles.All[i];
                slugcatMenu.DropDownItems.Add(CreateSlugcatItem(
                    SlugcatProfiles.SelectionLabel(profile.Id), profile.Id, startSlugcat));
            }
            refreshWorkshopItem = new ToolStripMenuItem(T("Workshop 모드 새로 고침", "Refresh Workshop Mods"));
            refreshWorkshopItem.Click += RefreshWorkshopItemClick;
            activeSlugcatsMenu = new ToolStripMenuItem(T("슬러그캣", "Slugcats"));
            spawnItem = new ToolStripMenuItem(T("슬러그캣 추가", "Add Slugcat"));
            spawnItem.Click += SpawnSlugcat;
            ToolStripMenuItem nextItem = new ToolStripMenuItem(T("다음 슬러그캣 선택", "Select Next Slugcat"));
            nextItem.Click += SelectNextSlugcat;
            removeItem = new ToolStripMenuItem(T("선택한 슬러그캣 삭제", "Remove Selected Slugcat"));
            removeItem.Click += RemoveSelectedSlugcat;
            activeSlugcatsMenu.DropDownItems.Add(spawnItem);
            activeSlugcatsMenu.DropDownItems.Add(nextItem);
            activeSlugcatsMenu.DropDownItems.Add(removeItem);
            activeSlugcatsMenu.DropDownItems.Add(new ToolStripSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(activeSlugcatsMenu);
            menu.Items.Add(slugcatMenu);
            menu.Items.Add(skinEditorItem);
            menu.Items.Add(debugItem);
            menu.Items.Add(pauseItem);
            menu.Items.Add(refreshWorkshopItem);
            menu.Items.Add(retryRenderItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            trayIcon = new NotifyIcon();
            trayIcon.Icon = applicationIcon ?? SystemIcons.Application;
            trayIcon.Text = "SlugcatInMyMonitor";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.MouseClick += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left) OpenSettings(sender, EventArgs.Empty);
            };
            trayIcon.Visible = true;

            Shown += delegate
            {
                gameLoop.DebugEnabled = startDebug;
                displayRefreshRate = NativeMethods.GetPrimaryDisplayRefreshRate();
                ApplyRenderCadence(displayRefreshRate);
                renderingEnabled = true;
                renderTimer.Start();
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle = BuildOverlayExtendedStyle(parameters.ExStyle);
                return parameters;
            }
        }

        internal static int BuildOverlayExtendedStyle(int inheritedStyle)
        {
            // The overlay covers the entire virtual desktop. Keeping it fully
            // transparent to input is required for buttons owned by other
            // processes; HTTRANSPARENT alone only reliably walks windows in
            // this UI thread.
            return inheritedStyle |
                   NativeMethods.WS_EX_TRANSPARENT |
                   NativeMethods.WS_EX_NOREDIRECTIONBITMAP |
                   NativeMethods.WS_EX_LAYERED |
                   NativeMethods.WS_EX_TOOLWINDOW |
                   NativeMethods.WS_EX_TOPMOST |
                   NativeMethods.WS_EX_NOACTIVATE;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ConfigureVirtualDesktop();
            InstallMouseHook();
            InstallForegroundEventHook();
            EnsureOverlayTopMost();
            compositionHost = new DirectCompositionHost(Handle, virtualDesktopBounds);
            collisionWorld.Refresh(Handle);
            surfaceRefreshClock.Restart();
            AddSlugcat(startSlugcat);
            if (!string.IsNullOrWhiteSpace(startDmsSkinId))
            {
                string reason;
                if (!gameLoop.SetDmsSkin(startDmsSkinId, out reason))
                    trayIcon.ShowBalloonTip(5000, T("DMS 스킨을 사용할 수 없음", "DMS Skin Unavailable"), reason, ToolTipIcon.Warning);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            renderingEnabled = false;
            renderTimer.Stop();
            UninstallForegroundEventHook();
            UninstallMouseHook();
            ReleaseGrabInput();
            if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.Close();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.Close();
            for (int i = 0; i < gameLoops.Count; i++) gameLoops[i].Dispose();
            gameLoops.Clear();
            gameLoop = null;
            if (compositionHost != null) compositionHost.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (applicationIcon != null) applicationIcon.Dispose();
            base.OnHandleDestroyed(e);
        }

        private void RenderTimerTick(object sender, EventArgs e)
        {
            // A disabled renderer with an active timer is waiting for an
            // automatic presentation retry.
            if (!renderingEnabled) renderingEnabled = true;
            RenderFrame();
        }

        private void RenderFrame()
        {
            if (!renderingEnabled || renderingFrame) return;
            renderingFrame = true;
            try
            {
                PollDragInput();
                RefreshCollisionWorld();
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    gameLoops[i].Advance(Handle);
                    poseBuffer[i] = gameLoops[i].BuildPose();
                }
                UpdateRenderCadence(poseBuffer, gameLoops.Count);
                surfaceBoundsBuffer.Clear();
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    bool debug = gameLoops[i].DebugEnabled &&
                        ReferenceEquals(gameLoops[i], gameLoop);
                    surfaceBoundsBuffer.Add(CalculateRenderBounds(poseBuffer[i], debug));
                }
                IList<CompositionBatch> batches = compositionBatchPlanner.Plan(
                    surfaceBoundsBuffer, OverlaySizeQuantum);
                compositionHost.BeginEffectFrame();
                for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    CompositionBatch batch = batches[batchIndex];
                    DirectCompositionHost.CompositionSurface surface =
                        compositionHost.PrepareSurface(batchIndex, batch.Bounds);
                    System.Drawing.Graphics graphics = surface.Graphics;
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                    RenderSpace renderSpace = new RenderSpace(surface.Bounds);
                    for (int member = 0; member < batch.SurfaceIndices.Count; member++)
                    {
                        int loopIndex = batch.SurfaceIndices[member];
                        GameLoop loop = gameLoops[loopIndex];
                        bool debug = loop.DebugEnabled && ReferenceEquals(loop, gameLoop);
                        loop.Renderer.Render(graphics, poseBuffer[loopIndex], renderSpace, debug,
                            loop.World, loop.Slugcat, loop.AI, loop.AssetStatus,
                            loop.SelectedSlugcat);
                    }
                    compositionHost.Present(batchIndex);

                    RectangleF effectContentBounds = RectangleF.Empty;
                    for (int member = 0; member < batch.SurfaceIndices.Count; member++)
                    {
                        int loopIndex = batch.SurfaceIndices[member];
                        GameLoop loop = gameLoops[loopIndex];
                        RectangleF memberBounds = loop.Renderer.CalculateGpuEffectBounds(
                            loop.Slugcat, poseBuffer[loopIndex]);
                        if (memberBounds.IsEmpty) continue;
                        effectContentBounds = effectContentBounds.IsEmpty ? memberBounds :
                            RectangleF.Union(effectContentBounds, memberBounds);
                    }
                    if (!effectContentBounds.IsEmpty)
                    {
                        Rectangle effectBounds = compositionHost.PrepareEffectBounds(
                            batchIndex, effectContentBounds);
                        RenderSpace effectRenderSpace = new RenderSpace(effectBounds);
                        int smokeEffectCount = 0;
                        for (int member = 0; member < batch.SurfaceIndices.Count; member++)
                        {
                            int loopIndex = batch.SurfaceIndices[member];
                            GameLoop loop = gameLoops[loopIndex];
                            loop.Renderer.CollectGpuSmokeEffects(loop.Slugcat,
                                poseBuffer[loopIndex], effectRenderSpace,
                                smokeEffectBuffer, ref smokeEffectCount);
                        }
                        compositionHost.PresentEffects(batchIndex, smokeEffectBuffer,
                            smokeEffectCount, effectBounds);
                    }
                }
                compositionHost.Commit(batches.Count);
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].RecordRenderFrame(displayRefreshRate);
                if (renderErrorCount != 0)
                {
                    renderErrorCount = 0;
                    retryRenderItem.Enabled = false;
                    RefreshSettingsWindow();
                }
            }
            catch (Exception exception)
            {
                // Simulation/atlas/GDI drawing failures are not assumed to be
                // transient. Keep the tray alive and let the user explicitly
                // retry, while recording this failure only once.
                Program.LogException(exception);
                renderingEnabled = false;
                renderTimer.Stop();
                retryRenderItem.Enabled = true;
                RefreshSettingsWindow();
                trayIcon.ShowBalloonTip(5000,
                    T("슬러그캣 렌더링 일시 정지", "Slugcat Rendering Paused"),
                    exception.Message + T(" 트레이 메뉴에서 렌더링 재시도를 선택하세요.",
                        " Use Retry Rendering from the tray menu."), ToolTipIcon.Error);
            }
            finally
            {
                renderingFrame = false;
            }
        }

        private void RetryRendering(object sender, EventArgs e)
        {
            try
            {
                RecreateCompositionHost();
                renderErrorCount = 0;
                retryRenderItem.Enabled = false;
                displayRefreshRate = NativeMethods.GetPrimaryDisplayRefreshRate();
                renderingEnabled = true;
                ApplyRenderCadence(displayRefreshRate);
                renderTimer.Start();
                RefreshSettingsWindow();
                RenderFrame();
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                retryRenderItem.Enabled = true;
                RefreshSettingsWindow();
                trayIcon.ShowBalloonTip(5000,
                    T("슬러그캣 렌더링 재시도 실패", "Slugcat Rendering Retry Failed"),
                    exception.Message, ToolTipIcon.Error);
            }
        }

        private void RecreateCompositionHost()
        {
            if (compositionHost != null) compositionHost.Dispose();
            compositionHost = null;
            compositionHost = new DirectCompositionHost(Handle, virtualDesktopBounds);
        }

        private void RefreshCollisionWorld()
        {
            if (collisionWorld.TryApplyPendingRefresh())
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].ApplyMovingSurfaceDelta();
            }
            if (surfaceRefreshClock.Elapsed.TotalSeconds <
                SimulationConstants.WindowRefreshSeconds) return;

            collisionWorld.RequestRefresh(Handle);
            surfaceRefreshClock.Restart();
        }

        private void UpdateRenderCadence(SlugcatPose[] poses, int poseCount)
        {
            double targetRefreshRate = 0.0;
            for (int i = 0; i < poseCount; i++)
            {
                string deviceName = poses[i].CurrentMonitorName;
                double refreshRate;
                if (!displayRefreshRates.TryGetValue(deviceName, out refreshRate))
                {
                    refreshRate = NativeMethods.GetDisplayRefreshRate(deviceName);
                    displayRefreshRates[deviceName] = refreshRate;
                }
                if (refreshRate > targetRefreshRate) targetRefreshRate = refreshRate;
            }

            if (targetRefreshRate <= 1.0)
                targetRefreshRate = NativeMethods.GetPrimaryDisplayRefreshRate();
            ApplyRenderCadence(targetRefreshRate);
        }

        private void ApplyRenderCadence(double refreshRate)
        {
            if (refreshRate <= 1.0) refreshRate = DefaultRenderFramesPerSecond;
            displayRefreshRate = refreshRate;
            int interval = Math.Max(1, (int)Math.Round(1000.0 / refreshRate));
            if (renderTimer.Interval != interval) renderTimer.Interval = interval;
        }

        private void ConfigureVirtualDesktop()
        {
            Rectangle virtualBounds = MonitorManager.GetVirtualBounds();
            if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
                throw new InvalidOperationException("Windows reported an empty virtual desktop.");

            virtualDesktopBounds = virtualBounds;
            Bounds = virtualDesktopBounds;
            if (compositionHost != null) compositionHost.SetDesktopBounds(virtualDesktopBounds);
        }

        private Rectangle CalculateRenderBounds(SlugcatPose pose, bool debug)
        {
            if (debug) return virtualDesktopBounds;
            RectangleF content = pose.GraphicsBounds;

            int contentWidth = (int)Math.Ceiling(content.Width) + OverlayPadding * 2;
            int contentHeight = (int)Math.Ceiling(content.Height) + OverlayPadding * 2;
            int width = RoundOverlaySize(Math.Max(MinimumOverlaySize, contentWidth));
            int height = RoundOverlaySize(Math.Max(MinimumOverlaySize, contentHeight));
            int centerX = (int)Math.Round(content.Left + content.Width * 0.5f);
            int centerY = (int)Math.Round(content.Top + content.Height * 0.5f);
            return new Rectangle(centerX - width / 2, centerY - height / 2, width, height);
        }

        private static int RoundOverlaySize(int value)
        {
            return ((value + OverlaySizeQuantum - 1) / OverlaySizeQuantum) * OverlaySizeQuantum;
        }

        private void PollDragInput()
        {
            // A press consumed by WH_MOUSE_LL is intentionally absent from the
            // normal Windows button state. While the hook owns a Slugcat drag,
            // only its matching WM_LBUTTONUP may end that drag.
            if (mouseCaptured) return;
            bool currentlyDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
            leftButtonDown = currentlyDown;
        }

        private void InstallMouseHook()
        {
            if (mouseHook != IntPtr.Zero) return;
            mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL,
                mouseHookCallback, NativeMethods.GetModuleHandle(null), 0);
            if (mouseHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to install the Slugcat mouse input hook.");
        }

        private void UninstallMouseHook()
        {
            IntPtr hook = mouseHook;
            mouseHook = IntPtr.Zero;
            if (hook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(hook);
        }

        private void InstallForegroundEventHook()
        {
            if (foregroundEventHook != IntPtr.Zero) return;
            foregroundEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, foregroundEventCallback, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT |
                NativeMethods.WINEVENT_SKIPOWNPROCESS);
            if (foregroundEventHook == IntPtr.Zero)
                Program.LogException(new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to monitor foreground window changes."));
        }

        private void UninstallForegroundEventHook()
        {
            IntPtr hook = foregroundEventHook;
            foregroundEventHook = IntPtr.Zero;
            if (hook != IntPtr.Zero) NativeMethods.UnhookWinEvent(hook);
        }

        private void ForegroundEventCallback(IntPtr hook, uint eventType, IntPtr handle,
            int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (handle == IntPtr.Zero || !IsHandleCreated || IsDisposed) return;
            NativeMethods.PostMessage(Handle, WmEnsureTopMost, IntPtr.Zero, IntPtr.Zero);
        }

        private void EnsureOverlayTopMost()
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (!NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER))
            {
                Program.LogException(new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to restore the overlay topmost position."));
            }
        }

        private IntPtr MouseHookCallback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int mouseMessage = unchecked((int)message.ToInt64());
                try
                {
                    if (mouseMessage == NativeMethods.WM_LBUTTONDOWN ||
                        mouseMessage == NativeMethods.WM_LBUTTONDBLCLK)
                    {
                        NativeMethods.LowLevelMouseHookData hookData =
                            (NativeMethods.LowLevelMouseHookData)Marshal.PtrToStructure(data,
                                typeof(NativeMethods.LowLevelMouseHookData));
                        Vec2 point = new Vec2(hookData.Point.X, hookData.Point.Y);
                        GameLoop hit = FindSlugcatAt(point);
                        if (hit != null && BeginGrab(hit, point)) return new IntPtr(1);
                    }
                    else if (mouseMessage == NativeMethods.WM_LBUTTONUP && mouseCaptured)
                    {
                        ReleaseGrabInput();
                        leftButtonDown = false;
                        return new IntPtr(1);
                    }
                }
                catch (Exception exception)
                {
                    Program.LogException(exception);
                    ReleaseGrabInput();
                }
            }
            return NativeMethods.CallNextHookEx(mouseHook, code, message, data);
        }

        private bool BeginGrab(GameLoop hit, Vec2 point)
        {
            SelectSlugcat(hit);
            if (!hit.BeginGrab(point)) return false;
            grabbedGameLoop = hit;
            mouseCaptured = true;
            leftButtonDown = true;
            return true;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmEnsureTopMost)
            {
                EnsureOverlayTopMost();
                return;
            }
            if (message.Msg == NativeMethods.WM_LBUTTONUP && mouseCaptured)
            {
                ReleaseGrabInput();
                leftButtonDown = false;
            }
            else if ((message.Msg == NativeMethods.WM_CAPTURECHANGED ||
                message.Msg == NativeMethods.WM_CANCELMODE) && mouseCaptured)
            {
                ReleaseGrabInput();
            }
            if (message.Msg == NativeMethods.WM_DISPLAYCHANGE || message.Msg == NativeMethods.WM_DPICHANGED)
            {
                ReleaseGrabInput();
                try
                {
                    ConfigureVirtualDesktop();
                    if (compositionHost != null) compositionHost.ResetSurfaces();
                    displayRefreshRates.Clear();
                    ApplyRenderCadence(NativeMethods.GetPrimaryDisplayRefreshRate());
                }
                catch (Exception exception)
                {
                    Program.LogException(exception);
                    renderingEnabled = false;
                    renderTimer.Stop();
                    retryRenderItem.Enabled = true;
                }
                return;
            }
            base.WndProc(ref message);
        }

        internal static bool ShouldSuppressLeftButton(int mouseMessage,
            bool draggingSlugcat, bool slugcatUnderPointer)
        {
            if (mouseMessage == NativeMethods.WM_LBUTTONUP) return draggingSlugcat;
            return (mouseMessage == NativeMethods.WM_LBUTTONDOWN ||
                mouseMessage == NativeMethods.WM_LBUTTONDBLCLK) && slugcatUnderPointer;
        }

        private void ReleaseGrabInput()
        {
            GameLoop grabbed = grabbedGameLoop;
            grabbedGameLoop = null;
            mouseCaptured = false;
            if (grabbed != null) grabbed.EndGrab();
        }

        private GameLoop FindSlugcatAt(Vec2 point)
        {
            for (int i = gameLoops.Count - 1; i >= 0; i--)
                if (gameLoops[i].HitTest(point)) return gameLoops[i];
            return null;
        }

        private void AddSlugcat(SlugcatId id)
        {
            if (gameLoops.Count >= MaximumSlugcats) return;
            GameLoop added = new GameLoop(Handle, installation, id,
                gameLoops.Count, collisionWorld);
            added.DebugEnabled = debugItem.Checked;
            added.Paused = pauseItem.Checked;
            gameLoops.Add(added);
            SelectSlugcat(added);
        }

        private void SpawnSlugcat(object sender, EventArgs e)
        {
            if (gameLoops.Count >= MaximumSlugcats)
            {
                trayIcon.ShowBalloonTip(3000, T("슬러그캣 수 제한", "Slugcat Limit"),
                    T("슬러그캣은 최대 " + MaximumSlugcats + "마리까지 실행할 수 있습니다.",
                        "Up to " + MaximumSlugcats + " Slugcats can be active."), ToolTipIcon.Info);
                return;
            }
            try
            {
                AddSlugcat(gameLoop == null ? startSlugcat : gameLoop.SelectedSlugcat.Id);
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(4000, T("슬러그캣 추가 실패", "Failed to Add Slugcat"),
                    exception.Message, ToolTipIcon.Error);
            }
        }

        private void SelectNextSlugcat(object sender, EventArgs e)
        {
            if (gameLoops.Count < 2) return;
            int index = gameLoops.IndexOf(gameLoop);
            SelectSlugcat(gameLoops[(index + 1) % gameLoops.Count]);
        }

        private void RemoveSelectedSlugcat(object sender, EventArgs e)
        {
            if (gameLoop == null || gameLoops.Count <= 1) return;
            GameLoop removed = gameLoop;
            int index = gameLoops.IndexOf(removed);
            if (ReferenceEquals(grabbedGameLoop, removed))
            {
                ReleaseGrabInput();
            }
            gameLoops.RemoveAt(index);
            removed.Dispose();
            if (compositionHost != null) compositionHost.ResetSurfaces();
            SelectSlugcat(gameLoops[Math.Min(index, gameLoops.Count - 1)]);
        }

        private void SelectSlugcat(GameLoop selected)
        {
            if (selected == null) return;
            if (!ReferenceEquals(gameLoop, selected) && skinEditor != null && !skinEditor.IsDisposed)
                skinEditor.Close();
            gameLoop = selected;
            RefreshSlugcatSelectionMenu();
            RefreshActiveSlugcatsMenu();
        }

        private void RefreshSlugcatSelectionMenu()
        {
            if (gameLoop == null) return;
            for (int i = 0; i < slugcatMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = slugcatMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item != null) item.Checked = (SlugcatId)item.Tag == gameLoop.SelectedSlugcat.Id;
            }
        }

        private void RefreshActiveSlugcatsMenu()
        {
            while (activeSlugcatsMenu.DropDownItems.Count > 4)
                activeSlugcatsMenu.DropDownItems.RemoveAt(4);
            for (int i = 0; i < gameLoops.Count; i++)
            {
                GameLoop loop = gameLoops[i];
                ToolStripMenuItem item = new ToolStripMenuItem(
                    T("슬러그캣 ", "Slugcat ") + (i + 1) + " · " +
                    SlugcatProfiles.SelectionLabel(loop.SelectedSlugcat.Id));
                item.Tag = loop;
                item.Checked = ReferenceEquals(loop, gameLoop);
                item.Click += delegate(object itemSender, EventArgs args)
                {
                    ToolStripMenuItem clicked = itemSender as ToolStripMenuItem;
                    if (clicked != null) SelectSlugcat(clicked.Tag as GameLoop);
                };
                activeSlugcatsMenu.DropDownItems.Add(item);
            }
            activeSlugcatsMenu.Text = T("슬러그캣", "Slugcats") + " (" + gameLoops.Count + ")";
            spawnItem.Enabled = gameLoops.Count < MaximumSlugcats;
            removeItem.Enabled = gameLoops.Count > 1;
            trayIcon.Text = T("SlugcatInMyMonitor · 실행 중: " + gameLoops.Count + "마리",
                "SlugcatInMyMonitor · Active: " + gameLoops.Count);
            RefreshSettingsWindow();
        }

        private void OpenSettings(object sender, EventArgs e)
        {
            if (settingsWindow != null && !settingsWindow.IsDisposed)
            {
                settingsWindow.RefreshFromApp();
                settingsWindow.Activate();
                return;
            }

            settingsWindow = new SettingsWindow(this);
            if (applicationIcon != null) settingsWindow.Icon = applicationIcon;
            settingsWindow.FormClosed += delegate { settingsWindow = null; };
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        private void RefreshSettingsWindow()
        {
            if (settingsWindow != null && !settingsWindow.IsDisposed)
                settingsWindow.RefreshFromApp();
        }

        private void ToggleSkinEditor(object sender, EventArgs e)
        {
            if (skinEditor != null && !skinEditor.IsDisposed && skinEditor.Visible)
            {
                skinEditor.Close();
                return;
            }
            try
            {
                skinEditor = new SkinEditorWindow(gameLoop, delegate
                {
                    RefreshSlugcatSelectionMenu();
                    RefreshActiveSlugcatsMenu();
                });
                if (applicationIcon != null) skinEditor.Icon = applicationIcon;
                skinEditor.FormClosed += delegate { skinEditor = null; };
                skinEditor.Show();
                skinEditor.Activate();
            }
            catch (Exception exception)
            {
                skinEditor = null;
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(5000, T("스킨 편집기 실행 실패", "Skin Editor Failed"),
                    exception.Message, ToolTipIcon.Error);
            }
        }

        private static Vec2 CurrentCursorPoint()
        {
            NativeMethods.Point point;
            return NativeMethods.GetCursorPos(out point) ? new Vec2(point.X, point.Y) : Vec2.Zero;
        }

        private ToolStripMenuItem CreateSlugcatItem(string label, SlugcatId id, SlugcatId selected)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Tag = id;
            item.Checked = id == selected;
            item.Click += SlugcatItemClick;
            return item;
        }

        private void SlugcatItemClick(object sender, EventArgs e)
        {
            ToolStripMenuItem selected = sender as ToolStripMenuItem;
            if (selected == null) return;
            for (int i = 0; i < slugcatMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = slugcatMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item != null) item.Checked = ReferenceEquals(item, selected);
            }
            if (gameLoop != null) gameLoop.SetSelectedSlugcat((SlugcatId)selected.Tag);
            RefreshActiveSlugcatsMenu();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.RefreshFromGame();
        }

        internal string[] SettingsSlugcatNames
        {
            get
            {
                string[] names = new string[gameLoops.Count];
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    GameLoop loop = gameLoops[i];
                    names[i] = T("슬러그캣 ", "Slugcat ") + (i + 1) + " · " +
                        SlugcatProfiles.SelectionLabel(loop.SelectedSlugcat.Id);
                }
                return names;
            }
        }

        internal int SettingsSelectedSlugcatIndex
        { get { return gameLoop == null ? -1 : gameLoops.IndexOf(gameLoop); } }

        internal bool SettingsCanAddSlugcat { get { return gameLoops.Count < MaximumSlugcats; } }
        internal bool SettingsCanRemoveSlugcat { get { return gameLoops.Count > 1; } }
        internal bool SettingsCanSelectNextSlugcat { get { return gameLoops.Count > 1; } }
        internal bool SettingsCanRetryRendering { get { return retryRenderItem.Enabled; } }
        internal bool SettingsDebugEnabled
        {
            get { return debugItem.Checked; }
            set { debugItem.Checked = value; }
        }
        internal bool SettingsPaused
        {
            get { return pauseItem.Checked; }
            set { pauseItem.Checked = value; }
        }
        internal SlugcatId SettingsSlugcatId
        { get { return gameLoop == null ? startSlugcat : gameLoop.SelectedSlugcat.Id; } }
        internal void SettingsSelectSlugcat(int index)
        {
            if (index >= 0 && index < gameLoops.Count) SelectSlugcat(gameLoops[index]);
        }

        internal void SettingsAddSlugcat() { SpawnSlugcat(null, EventArgs.Empty); }
        internal void SettingsSelectNextSlugcat() { SelectNextSlugcat(null, EventArgs.Empty); }
        internal void SettingsRemoveSelectedSlugcat() { RemoveSelectedSlugcat(null, EventArgs.Empty); }
        internal void SettingsSetSlugcat(SlugcatId id)
        {
            if (gameLoop == null) return;
            gameLoop.SetSelectedSlugcat(id);
            RefreshSlugcatSelectionMenu();
            RefreshActiveSlugcatsMenu();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.RefreshFromGame();
        }

        internal void SettingsSetLanguage(UiLanguage language)
        { UiLocalization.SetLanguage(language); }

        internal string SettingsRefreshWorkshop()
        {
            RefreshAllWorkshopIntegrations();
            return gameLoop == null
                ? T("선택한 슬러그캣이 없습니다.", "No Slugcat is selected.")
                : T("Dress My Slugcat 스프라이트 시트 " + gameLoop.DmsSkins.Count + "개를 찾았습니다.",
                    gameLoop.DmsSkins.Count + " Dress My Slugcat spritesheets found.");
        }

        internal void SettingsOpenAppearanceEditor()
        {
            if (skinEditor != null && !skinEditor.IsDisposed)
            {
                skinEditor.Activate();
                return;
            }
            ToggleSkinEditor(null, EventArgs.Empty);
        }

        internal void SettingsRetryRendering() { RetryRendering(null, EventArgs.Empty); }
        internal void SettingsExitApplication() { Close(); }

        private void RefreshWorkshopItemClick(object sender, EventArgs e)
        {
            if (gameLoop == null) return;
            try
            {
                string status = SettingsRefreshWorkshop();
                trayIcon.ShowBalloonTip(2500, T("Workshop 새로 고침 완료", "Workshop Refreshed"),
                    status, ToolTipIcon.Info);
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(5000, T("Workshop 새로 고침 실패", "Workshop Refresh Failed"), exception.Message,
                    ToolTipIcon.Warning);
            }
        }

        private void RefreshAllWorkshopIntegrations()
        {
            for (int index = 0; index < gameLoops.Count; index++)
                gameLoops[index].RefreshWorkshopIntegration();
            RefreshSettingsWindow();
        }

        private static Vec2 ScreenPointFromLParam(IntPtr value)
        {
            long packed = value.ToInt64();
            int x = (short)(packed & 0xffff);
            int y = (short)((packed >> 16) & 0xffff);
            return new Vec2(x, y);
        }

        private static string T(string korean, string english)
        { return UiLocalization.Text(korean, english); }
    }
}
