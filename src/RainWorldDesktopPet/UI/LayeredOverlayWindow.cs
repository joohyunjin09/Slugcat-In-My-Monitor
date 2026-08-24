using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Creature;

namespace RainWorldDesktopPet.UI
{
    public sealed class LayeredOverlayWindow : Form
    {
        private const int HotKeyId = 0x5343;
        private const int SkinEditorHotKeyId = 0x5344;
        private const int SpawnHotKeyId = 0x5345;
        private const int SelectNextHotKeyId = 0x5346;
        private const int MaximumSlugcats = 8;
        private readonly RainWorldInstallation installation;
        private readonly SlugcatVariant startVariant;
        private readonly SlugcatSkin startSkin;
        private readonly Timer renderTimer;
        private readonly NotifyIcon trayIcon;
        private readonly ToolStripMenuItem variantMenu;
        private readonly ToolStripMenuItem visualSkinMenu;
        private readonly ToolStripMenuItem debugItem;
        private readonly ToolStripMenuItem retryRenderItem;
        private readonly ToolStripMenuItem skinEditorItem;
        private readonly ToolStripMenuItem pauseItem;
        private readonly ToolStripMenuItem slugcatsMenu;
        private readonly ToolStripMenuItem spawnItem;
        private readonly ToolStripMenuItem removeItem;
        private readonly List<GameLoop> gameLoops = new List<GameLoop>();
        private LayeredBackBuffer backBuffer;
        private GameLoop gameLoop;
        private GameLoop grabbedGameLoop;
        private SkinEditorWindow skinEditor;
        private Rectangle overlayBounds;
        private RenderSpace renderSpace;
        private bool mouseCaptured;
        private int renderErrorCount;
        private bool renderingEnabled;
        private bool renderingFrame;
        private double displayRefreshRate;

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug, SlugcatVariant startVariant)
            : this(installation, startDebug, startVariant, SlugcatSkin.Default)
        {
        }

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatVariant startVariant, SlugcatSkin startSkin)
        {
            this.installation = installation;
            this.startVariant = startVariant;
            this.startSkin = startSkin;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            overlayBounds = MonitorManager.GetVirtualBounds();
            Bounds = overlayBounds;
            renderSpace = new RenderSpace(overlayBounds);
            Text = "SlugcatInMyMonitor";

            renderTimer = new Timer();
            // This timer is only an error-retry/fallback wakeup. Normal frames
            // are paced by DWM composition from Application.Idle.
            renderTimer.Interval = 250;
            renderTimer.Tick += RenderTimerTick;

            ContextMenuStrip menu = new ContextMenuStrip();
            debugItem = new ToolStripMenuItem("Debug overlay (F1)");
            debugItem.CheckOnClick = true;
            debugItem.Checked = startDebug;
            debugItem.CheckedChanged += delegate
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].DebugEnabled = debugItem.Checked;
            };
            pauseItem = new ToolStripMenuItem("Pause all slugcats");
            pauseItem.CheckOnClick = true;
            pauseItem.CheckedChanged += delegate
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].Paused = pauseItem.Checked;
            };
            retryRenderItem = new ToolStripMenuItem("Retry rendering");
            retryRenderItem.Enabled = false;
            retryRenderItem.Click += RetryRendering;
            skinEditorItem = new ToolStripMenuItem("Skin editor (F2)");
            skinEditorItem.Click += ToggleSkinEditor;
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { Close(); };
            variantMenu = new ToolStripMenuItem("Player physics / base colour");
            variantMenu.DropDownItems.Add(CreateVariantItem("Survivor (white)", SlugcatVariant.Survivor, startVariant));
            variantMenu.DropDownItems.Add(CreateVariantItem("Monk (yellow)", SlugcatVariant.Monk, startVariant));
            variantMenu.DropDownItems.Add(CreateVariantItem("Hunter (red)", SlugcatVariant.Hunter, startVariant));
            variantMenu.DropDownItems.Add(CreateVariantItem("Gourmand", SlugcatVariant.Gourmand, startVariant));
            visualSkinMenu = new ToolStripMenuItem("Slugcat skin");
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Default", SlugcatSkin.Default, startSkin));
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Artificer / 기술병", SlugcatSkin.Artificer, startSkin));
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Spearmaster / 창술가", SlugcatSkin.Spearmaster, startSkin));
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Rivulet / 물살이", SlugcatSkin.Rivulet, startSkin));
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Saint / 성자", SlugcatSkin.Saint, startSkin));
            slugcatsMenu = new ToolStripMenuItem("Slugcats");
            spawnItem = new ToolStripMenuItem("Spawn slugcat (F3)");
            spawnItem.Click += SpawnSlugcat;
            ToolStripMenuItem nextItem = new ToolStripMenuItem("Select next slugcat (F4)");
            nextItem.Click += SelectNextSlugcat;
            removeItem = new ToolStripMenuItem("Remove selected slugcat");
            removeItem.Click += RemoveSelectedSlugcat;
            slugcatsMenu.DropDownItems.Add(spawnItem);
            slugcatsMenu.DropDownItems.Add(nextItem);
            slugcatsMenu.DropDownItems.Add(removeItem);
            slugcatsMenu.DropDownItems.Add(new ToolStripSeparator());
            menu.Items.Add(slugcatsMenu);
            menu.Items.Add(variantMenu);
            menu.Items.Add(visualSkinMenu);
            menu.Items.Add(skinEditorItem);
            menu.Items.Add(debugItem);
            menu.Items.Add(pauseItem);
            menu.Items.Add(retryRenderItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Text = "SlugcatInMyMonitor";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = true;

            Shown += delegate
            {
                gameLoop.DebugEnabled = startDebug;
                displayRefreshRate = NativeMethods.GetPrimaryDisplayRefreshRate();
                renderingEnabled = true;
                Application.Idle += ApplicationIdle;
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW |
                                      NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ConfigureVirtualDesktopOverlay(false);
            AddSlugcat(startVariant, startSkin);
            RefreshSkinAvailability();
            NativeMethods.RegisterHotKey(Handle, HotKeyId, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F1);
            NativeMethods.RegisterHotKey(Handle, SkinEditorHotKeyId,
                NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F2);
            NativeMethods.RegisterHotKey(Handle, SpawnHotKeyId,
                NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F3);
            NativeMethods.RegisterHotKey(Handle, SelectNextHotKeyId,
                NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F4);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, HotKeyId);
            NativeMethods.UnregisterHotKey(Handle, SkinEditorHotKeyId);
            NativeMethods.UnregisterHotKey(Handle, SpawnHotKeyId);
            NativeMethods.UnregisterHotKey(Handle, SelectNextHotKeyId);
            renderingEnabled = false;
            Application.Idle -= ApplicationIdle;
            renderTimer.Stop();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.Close();
            for (int i = 0; i < gameLoops.Count; i++) gameLoops[i].Dispose();
            gameLoops.Clear();
            gameLoop = null;
            if (backBuffer != null) backBuffer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            base.OnHandleDestroyed(e);
        }

        private void RenderTimerTick(object sender, EventArgs e)
        {
            renderTimer.Stop();
            renderingEnabled = true;
            RenderFrame();
        }

        private void ApplicationIdle(object sender, EventArgs e)
        {
            while (renderingEnabled && NativeMethods.IsMessageQueueIdle())
            {
                RenderFrame();
                if (!renderingEnabled) break;
                // DwmFlush waits for the next compositor frame, allowing the
                // draw loop to follow 60/120/144/165/240 Hz displays without
                // advancing the fixed 40 Hz simulation at that rate.
                if (NativeMethods.DwmFlush() != 0)
                {
                    renderTimer.Interval = 1;
                    renderTimer.Start();
                    break;
                }
            }
        }

        private void RenderFrame()
        {
            if (!renderingEnabled || renderingFrame) return;
            renderingFrame = true;
            try
            {
                System.Drawing.Graphics graphics = backBuffer.Graphics;
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    GameLoop loop = gameLoops[i];
                    loop.Advance(Handle);
                    SlugcatPose pose = loop.BuildPose();
                    loop.Renderer.Render(graphics, pose, renderSpace,
                        loop.DebugEnabled && ReferenceEquals(loop, gameLoop),
                        loop.World, loop.Slugcat, loop.AI, loop.AssetStatus, loop.Appearance);
                }
                backBuffer.Present(Handle, renderSpace.WorldOrigin);
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].RecordRenderFrame(displayRefreshRate);
                if (renderErrorCount != 0)
                {
                    renderErrorCount = 0;
                    retryRenderItem.Enabled = false;
                }
            }
            catch (Exception exception)
            {
                LayeredPresentationException presentationException = exception as LayeredPresentationException;
                if (presentationException != null)
                {
                    HandlePresentationFailure(presentationException);
                    return;
                }

                // Simulation/atlas/GDI drawing failures are not assumed to be
                // transient. Keep the tray alive and let the user explicitly
                // retry, while recording this failure only once.
                Program.LogException(exception);
                renderingEnabled = false;
                renderTimer.Stop();
                retryRenderItem.Enabled = true;
                trayIcon.ShowBalloonTip(5000, "Slugcat rendering paused",
                    exception.Message + " Use Retry rendering from the tray menu.", ToolTipIcon.Error);
            }
            finally
            {
                renderingFrame = false;
            }
        }

        private void HandlePresentationFailure(LayeredPresentationException exception)
        {
            renderingEnabled = false;
            renderErrorCount++;
            if (renderErrorCount == 1)
            {
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(5000, "Slugcat rendering retry",
                    exception.Message, ToolTipIcon.Warning);
            }

            if (renderErrorCount == 3)
            {
                try
                {
                    ReplaceBackBuffer();
                }
                catch (Exception replacementException)
                {
                    Program.LogException(replacementException);
                    renderTimer.Stop();
                    retryRenderItem.Enabled = true;
                    trayIcon.ShowBalloonTip(5000, "Slugcat rendering paused",
                        replacementException.Message + " Use Retry rendering from the tray menu.", ToolTipIcon.Error);
                    return;
                }
            }

            if (renderErrorCount >= 6)
            {
                Program.LogException(new InvalidOperationException(
                    "Layered presentation failed six consecutive times; automatic retries stopped.", exception));
                renderTimer.Stop();
                retryRenderItem.Enabled = true;
                trayIcon.ShowBalloonTip(5000, "Slugcat rendering paused",
                    "Display presentation kept failing. Use Retry rendering from the tray menu.", ToolTipIcon.Error);
                return;
            }

            int delay = 250 << Math.Min(renderErrorCount - 1, 4);
            renderTimer.Interval = Math.Min(delay, 4000);
            renderTimer.Start();
        }

        private void RetryRendering(object sender, EventArgs e)
        {
            try
            {
                ReplaceBackBuffer();
                renderErrorCount = 0;
                retryRenderItem.Enabled = false;
                displayRefreshRate = NativeMethods.GetPrimaryDisplayRefreshRate();
                renderingEnabled = true;
                RenderFrame();
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                retryRenderItem.Enabled = true;
                trayIcon.ShowBalloonTip(5000, "Slugcat retry failed", exception.Message, ToolTipIcon.Error);
            }
        }

        private void ReplaceBackBuffer()
        {
            LayeredBackBuffer replacement = new LayeredBackBuffer(overlayBounds.Width, overlayBounds.Height);
            LayeredBackBuffer previous = backBuffer;
            backBuffer = replacement;
            if (previous != null) previous.Dispose();
        }

        private void ConfigureVirtualDesktopOverlay(bool replaceExistingBuffer)
        {
            Rectangle virtualBounds = MonitorManager.GetVirtualBounds();
            if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
                throw new InvalidOperationException("Windows reported an empty virtual desktop.");

            bool changed = virtualBounds != overlayBounds;
            overlayBounds = virtualBounds;
            renderSpace = new RenderSpace(overlayBounds);
            Bounds = overlayBounds;
            if (backBuffer == null)
            {
                backBuffer = new LayeredBackBuffer(overlayBounds.Width, overlayBounds.Height);
            }
            else if (replaceExistingBuffer || changed)
            {
                ReplaceBackBuffer();
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_NCHITTEST && gameLoop != null)
            {
                Vec2 point = ScreenPointFromLParam(message.LParam);
                message.Result = new IntPtr(mouseCaptured || FindSlugcatAt(point) != null
                    ? NativeMethods.HTCLIENT : NativeMethods.HTTRANSPARENT);
                return;
            }
            if (message.Msg == NativeMethods.WM_DISPLAYCHANGE || message.Msg == NativeMethods.WM_DPICHANGED)
            {
                try
                {
                    ConfigureVirtualDesktopOverlay(true);
                    displayRefreshRate = NativeMethods.GetPrimaryDisplayRefreshRate();
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
            if (message.Msg == NativeMethods.WM_LBUTTONDOWN && gameLoop != null)
            {
                Vec2 point = CurrentCursorPoint();
                GameLoop hit = FindSlugcatAt(point);
                if (hit != null)
                {
                    SelectSlugcat(hit);
                    if (hit.BeginGrab(point))
                    {
                        grabbedGameLoop = hit;
                        mouseCaptured = true;
                        NativeMethods.SetCapture(Handle);
                    }
                }
                return;
            }
            if (message.Msg == NativeMethods.WM_LBUTTONUP && gameLoop != null)
            {
                if (mouseCaptured)
                {
                    mouseCaptured = false;
                    if (grabbedGameLoop != null) grabbedGameLoop.EndGrab();
                    grabbedGameLoop = null;
                    NativeMethods.ReleaseCapture();
                }
                return;
            }
            if ((message.Msg == NativeMethods.WM_CAPTURECHANGED || message.Msg == NativeMethods.WM_CANCELMODE) &&
                gameLoop != null && mouseCaptured)
            {
                mouseCaptured = false;
                if (grabbedGameLoop != null) grabbedGameLoop.EndGrab();
                grabbedGameLoop = null;
                return;
            }
            if (message.Msg == NativeMethods.WM_HOTKEY && message.WParam.ToInt32() == HotKeyId && gameLoop != null)
            {
                debugItem.Checked = !debugItem.Checked;
                return;
            }
            if (message.Msg == NativeMethods.WM_HOTKEY &&
                message.WParam.ToInt32() == SkinEditorHotKeyId && gameLoop != null)
            {
                ToggleSkinEditor(null, EventArgs.Empty);
                return;
            }
            if (message.Msg == NativeMethods.WM_HOTKEY &&
                message.WParam.ToInt32() == SpawnHotKeyId)
            {
                SpawnSlugcat(null, EventArgs.Empty);
                return;
            }
            if (message.Msg == NativeMethods.WM_HOTKEY &&
                message.WParam.ToInt32() == SelectNextHotKeyId)
            {
                SelectNextSlugcat(null, EventArgs.Empty);
                return;
            }
            base.WndProc(ref message);
        }

        private GameLoop FindSlugcatAt(Vec2 point)
        {
            for (int i = gameLoops.Count - 1; i >= 0; i--)
                if (gameLoops[i].HitTest(point)) return gameLoops[i];
            return null;
        }

        private void AddSlugcat(SlugcatVariant variant, SlugcatSkin skin)
        {
            if (gameLoops.Count >= MaximumSlugcats) return;
            GameLoop added = new GameLoop(Handle, installation, variant, skin, gameLoops.Count);
            added.DebugEnabled = debugItem.Checked;
            added.Paused = pauseItem.Checked;
            gameLoops.Add(added);
            SelectSlugcat(added);
        }

        private void SpawnSlugcat(object sender, EventArgs e)
        {
            if (gameLoops.Count >= MaximumSlugcats)
            {
                trayIcon.ShowBalloonTip(3000, "Slugcat limit",
                    "Up to " + MaximumSlugcats + " slugcats can be active.", ToolTipIcon.Info);
                return;
            }
            SlugcatVariant variant = gameLoop == null ? startVariant : gameLoop.Appearance.Variant;
            SlugcatSkin skin = gameLoop == null ? startSkin : gameLoop.Skin;
            try { AddSlugcat(variant, skin); }
            catch (Exception exception)
            {
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(4000, "Spawn failed", exception.Message, ToolTipIcon.Error);
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
                grabbedGameLoop.EndGrab();
                grabbedGameLoop = null;
                mouseCaptured = false;
                NativeMethods.ReleaseCapture();
            }
            gameLoops.RemoveAt(index);
            removed.Dispose();
            SelectSlugcat(gameLoops[Math.Min(index, gameLoops.Count - 1)]);
        }

        private void SelectSlugcat(GameLoop selected)
        {
            if (selected == null || ReferenceEquals(gameLoop, selected))
            {
                RefreshSlugcatMenu();
                return;
            }
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.Close();
            gameLoop = selected;
            RefreshAppearanceMenus();
            RefreshSlugcatMenu();
        }

        private void RefreshSlugcatMenu()
        {
            while (slugcatsMenu.DropDownItems.Count > 4)
                slugcatsMenu.DropDownItems.RemoveAt(4);
            for (int i = 0; i < gameLoops.Count; i++)
            {
                GameLoop loop = gameLoops[i];
                ToolStripMenuItem item = new ToolStripMenuItem("Slugcat " + (i + 1) + " · " +
                    (loop.Skin == SlugcatSkin.Default
                        ? loop.Appearance.Variant.ToString()
                        : loop.Skin.ToString()));
                item.Tag = loop;
                item.Checked = ReferenceEquals(loop, gameLoop);
                item.Click += delegate(object sender, EventArgs args)
                {
                    ToolStripMenuItem clicked = sender as ToolStripMenuItem;
                    if (clicked != null) SelectSlugcat(clicked.Tag as GameLoop);
                };
                slugcatsMenu.DropDownItems.Add(item);
            }
            slugcatsMenu.Text = "Slugcats (" + gameLoops.Count + ")";
            spawnItem.Enabled = gameLoops.Count < MaximumSlugcats;
            removeItem.Enabled = gameLoops.Count > 1;
            trayIcon.Text = "SlugcatInMyMonitor · " + gameLoops.Count + " active";
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
                skinEditor = new SkinEditorWindow(gameLoop, RefreshAppearanceMenus);
                skinEditor.FormClosed += delegate
                {
                    skinEditor = null;
                };
                skinEditor.Show();
                skinEditor.Activate();
            }
            catch (Exception exception)
            {
                skinEditor = null;
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(5000, "Skin editor failed",
                    exception.Message, ToolTipIcon.Error);
            }
        }

        private static Vec2 CurrentCursorPoint()
        {
            NativeMethods.Point point;
            return NativeMethods.GetCursorPos(out point) ? new Vec2(point.X, point.Y) : Vec2.Zero;
        }

        private ToolStripMenuItem CreateVariantItem(string label, SlugcatVariant variant, SlugcatVariant selected)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Tag = variant;
            item.Checked = variant == selected;
            item.Click += VariantItemClick;
            return item;
        }

        private void VariantItemClick(object sender, EventArgs e)
        {
            ToolStripMenuItem selected = sender as ToolStripMenuItem;
            if (selected == null) return;
            for (int i = 0; i < variantMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = variantMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item != null) item.Checked = ReferenceEquals(item, selected);
            }
            if (gameLoop != null) gameLoop.SetVariant((SlugcatVariant)selected.Tag);
            RefreshSlugcatMenu();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.RefreshFromGame();
        }

        private ToolStripMenuItem CreateSkinItem(string label, SlugcatSkin skin,
            SlugcatSkin selected)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Tag = skin;
            item.Checked = skin == selected;
            item.Click += SkinItemClick;
            return item;
        }

        private void SkinItemClick(object sender, EventArgs e)
        {
            ToolStripMenuItem selected = sender as ToolStripMenuItem;
            if (selected == null || gameLoop == null) return;
            SlugcatSkin skin = (SlugcatSkin)selected.Tag;
            if (!gameLoop.SetSkin(skin))
            {
                string reason;
                gameLoop.CanUseSkin(skin, out reason);
                trayIcon.ShowBalloonTip(4000, "Downpour skin unavailable",
                    reason, ToolTipIcon.Warning);
                return;
            }
            for (int i = 0; i < visualSkinMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = visualSkinMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item != null) item.Checked = ReferenceEquals(item, selected);
            }
            RefreshSlugcatMenu();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.RefreshFromGame();
        }

        private void RefreshAppearanceMenus()
        {
            if (gameLoop == null) return;
            for (int i = 0; i < variantMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = variantMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item != null) item.Checked = (SlugcatVariant)item.Tag == gameLoop.Appearance.Variant;
            }
            RefreshSkinAvailability();
            RefreshSlugcatMenu();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.RefreshFromGame();
        }

        private void RefreshSkinAvailability()
        {
            if (gameLoop == null) return;
            for (int i = 0; i < visualSkinMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = visualSkinMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item == null) continue;
                string reason;
                SlugcatSkin skin = (SlugcatSkin)item.Tag;
                item.Enabled = gameLoop.CanUseSkin(skin, out reason);
                item.ToolTipText = reason ?? "Local Rain World PlayerGraphics assets available";
                item.Checked = skin == gameLoop.Skin;
            }
        }

        private static Vec2 ScreenPointFromLParam(IntPtr value)
        {
            long packed = value.ToInt64();
            int x = (short)(packed & 0xffff);
            int y = (short)((packed >> 16) & 0xffff);
            return new Vec2(x, y);
        }

        private sealed class LayeredPresentationException : InvalidOperationException
        {
            public LayeredPresentationException(string message)
                : base(message)
            {
            }
        }

        private sealed class LayeredBackBuffer : IDisposable
        {
            private readonly int width;
            private readonly int height;
            private readonly IntPtr screenDeviceContext;
            private readonly IntPtr memoryDeviceContext;
            private readonly IntPtr bitmapHandle;
            private readonly IntPtr previousObject;
            private readonly Bitmap bitmap;
            private readonly System.Drawing.Graphics graphics;

            public LayeredBackBuffer(int width, int height)
            {
                this.width = width;
                this.height = height;
                IntPtr acquiredScreen = IntPtr.Zero;
                IntPtr acquiredMemory = IntPtr.Zero;
                IntPtr acquiredBitmap = IntPtr.Zero;
                IntPtr acquiredPrevious = IntPtr.Zero;
                Bitmap acquiredManagedBitmap = null;
                System.Drawing.Graphics acquiredGraphics = null;
                try
                {
                    acquiredScreen = NativeMethods.GetDC(IntPtr.Zero);
                    if (acquiredScreen == IntPtr.Zero) throw new InvalidOperationException("Could not acquire the screen device context.");
                    acquiredMemory = NativeMethods.CreateCompatibleDC(acquiredScreen);
                    if (acquiredMemory == IntPtr.Zero) throw new InvalidOperationException("Could not create the layered window device context.");
                    NativeMethods.BitmapInfo info = new NativeMethods.BitmapInfo();
                    info.Header.Size = (uint)Marshal.SizeOf(typeof(NativeMethods.BitmapInfoHeader));
                    info.Header.Width = width;
                    info.Header.Height = -height; // top-down DIB
                    info.Header.Planes = 1;
                    info.Header.BitCount = 32;
                    info.Header.Compression = 0;
                    IntPtr bits;
                    acquiredBitmap = NativeMethods.CreateDIBSection(acquiredScreen, ref info, 0, out bits, IntPtr.Zero, 0);
                    if (acquiredBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                        throw new InvalidOperationException("Could not allocate the layered window back buffer.");
                    acquiredPrevious = NativeMethods.SelectObject(acquiredMemory, acquiredBitmap);
                    acquiredManagedBitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
                    acquiredGraphics = System.Drawing.Graphics.FromImage(acquiredManagedBitmap);
                }
                catch
                {
                    if (acquiredGraphics != null) acquiredGraphics.Dispose();
                    if (acquiredManagedBitmap != null) acquiredManagedBitmap.Dispose();
                    if (acquiredMemory != IntPtr.Zero && acquiredPrevious != IntPtr.Zero)
                        NativeMethods.SelectObject(acquiredMemory, acquiredPrevious);
                    if (acquiredBitmap != IntPtr.Zero) NativeMethods.DeleteObject(acquiredBitmap);
                    if (acquiredMemory != IntPtr.Zero) NativeMethods.DeleteDC(acquiredMemory);
                    if (acquiredScreen != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, acquiredScreen);
                    throw;
                }

                screenDeviceContext = acquiredScreen;
                memoryDeviceContext = acquiredMemory;
                bitmapHandle = acquiredBitmap;
                previousObject = acquiredPrevious;
                bitmap = acquiredManagedBitmap;
                graphics = acquiredGraphics;
            }

            public System.Drawing.Graphics Graphics { get { return graphics; } }

            public void Present(IntPtr windowHandle, Vec2 origin)
            {
                NativeMethods.Point destination = new NativeMethods.Point((int)origin.X, (int)origin.Y);
                NativeMethods.Size size = new NativeMethods.Size(width, height);
                NativeMethods.Point source = new NativeMethods.Point(0, 0);
                NativeMethods.BlendFunction blend = new NativeMethods.BlendFunction();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;
                if (!NativeMethods.UpdateLayeredWindow(windowHandle, screenDeviceContext, ref destination, ref size,
                    memoryDeviceContext, ref source, 0, ref blend, NativeMethods.ULW_ALPHA))
                {
                    throw new LayeredPresentationException(
                        "UpdateLayeredWindow failed: " + Marshal.GetLastWin32Error());
                }
            }

            public void Dispose()
            {
                graphics.Dispose();
                bitmap.Dispose();
                NativeMethods.SelectObject(memoryDeviceContext, previousObject);
                NativeMethods.DeleteObject(bitmapHandle);
                NativeMethods.DeleteDC(memoryDeviceContext);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDeviceContext);
            }
        }
    }
}
