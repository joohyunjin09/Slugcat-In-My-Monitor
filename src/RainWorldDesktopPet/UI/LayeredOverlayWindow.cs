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
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.UI
{
    public sealed class LayeredOverlayWindow : Form
    {
        private const int MaximumSlugcats = 8;
        private readonly RainWorldInstallation installation;
        private readonly SlugcatVariant startVariant;
        private readonly SlugcatSkin startSkin;
        private readonly Timer renderTimer;
        private readonly NotifyIcon trayIcon;
        private readonly Icon applicationIcon;
        private readonly ToolStripMenuItem variantMenu;
        private readonly ToolStripMenuItem visualSkinMenu;
        private readonly ToolStripMenuItem dmsSkinMenu;
        private readonly ToolStripMenuItem refreshWorkshopItem;
        private readonly ToolStripMenuItem debugItem;
        private readonly ToolStripMenuItem retryRenderItem;
        private readonly ToolStripMenuItem skinEditorItem;
        private readonly ToolStripMenuItem pauseItem;
        private readonly ToolStripMenuItem slugcatsMenu;
        private readonly ToolStripMenuItem spawnItem;
        private readonly ToolStripMenuItem removeItem;
        private readonly List<GameLoop> gameLoops = new List<GameLoop>();
        private readonly string startDmsSkinId;
        private LayeredBackBuffer backBuffer;
        private GameLoop gameLoop;
        private GameLoop grabbedGameLoop;
        private SettingsWindow settingsWindow;
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
            : this(installation, startDebug, startVariant, startSkin, null)
        {
        }

        public LayeredOverlayWindow(RainWorldInstallation installation, bool startDebug,
            SlugcatVariant startVariant, SlugcatSkin startSkin, string startDmsSkinId)
        {
            this.installation = installation;
            this.startVariant = startVariant;
            this.startSkin = startSkin;
            this.startDmsSkinId = startDmsSkinId;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            overlayBounds = MonitorManager.GetVirtualBounds();
            Bounds = overlayBounds;
            renderSpace = new RenderSpace(overlayBounds);
            Text = "SlugcatInMyMonitor";
            applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (applicationIcon != null) Icon = applicationIcon;

            renderTimer = new Timer();
            // This timer is only an error-retry/fallback wakeup. Normal frames
            // are paced by DWM composition from Application.Idle.
            renderTimer.Interval = 250;
            renderTimer.Tick += RenderTimerTick;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem settingsItem = new ToolStripMenuItem("Open Settings");
            settingsItem.Click += OpenSettings;
            debugItem = new ToolStripMenuItem("Debug Overlay");
            debugItem.CheckOnClick = true;
            debugItem.Checked = startDebug;
            debugItem.CheckedChanged += delegate
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].DebugEnabled = debugItem.Checked;
                RefreshSettingsWindow();
            };
            pauseItem = new ToolStripMenuItem("Pause All Slugcats");
            pauseItem.CheckOnClick = true;
            pauseItem.CheckedChanged += delegate
            {
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].Paused = pauseItem.Checked;
                RefreshSettingsWindow();
            };
            retryRenderItem = new ToolStripMenuItem("Retry Rendering");
            retryRenderItem.Enabled = false;
            retryRenderItem.Click += RetryRendering;
            skinEditorItem = new ToolStripMenuItem("Skin Editor (Experimental)");
            skinEditorItem.Click += ToggleSkinEditor;
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { Close(); };
            variantMenu = new ToolStripMenuItem("Character and Base Color");
            variantMenu.DropDownItems.Add(CreateVariantItem("Survivor (White)", SlugcatVariant.Survivor, startVariant));
            variantMenu.DropDownItems.Add(CreateVariantItem("Monk (Yellow)", SlugcatVariant.Monk, startVariant));
            variantMenu.DropDownItems.Add(CreateVariantItem("Hunter (Red)", SlugcatVariant.Hunter, startVariant));
            variantMenu.DropDownItems.Add(CreateVariantItem("Gourmand", SlugcatVariant.Gourmand, startVariant));
            visualSkinMenu = new ToolStripMenuItem("Visual Skin (Experimental)");
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Default", SlugcatSkin.Default, startSkin));
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Artificer", SlugcatSkin.Artificer, startSkin));
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Spearmaster", SlugcatSkin.Spearmaster, startSkin));
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Rivulet", SlugcatSkin.Rivulet, startSkin));
            visualSkinMenu.DropDownItems.Add(CreateSkinItem("Saint", SlugcatSkin.Saint, startSkin));
            visualSkinMenu.DropDownItems.Add(new ToolStripSeparator());
            dmsSkinMenu = new ToolStripMenuItem("Dress My Slugcat skins");
            visualSkinMenu.DropDownItems.Add(dmsSkinMenu);
            visualSkinMenu.DropDownOpening += VisualSkinMenuOpening;
            refreshWorkshopItem = new ToolStripMenuItem("Refresh Workshop mods");
            refreshWorkshopItem.Click += RefreshWorkshopItemClick;
            slugcatsMenu = new ToolStripMenuItem("Slugcats");
            spawnItem = new ToolStripMenuItem("Add Slugcat");
            spawnItem.Click += SpawnSlugcat;
            ToolStripMenuItem nextItem = new ToolStripMenuItem("Select Next Slugcat");
            nextItem.Click += SelectNextSlugcat;
            removeItem = new ToolStripMenuItem("Remove Selected Slugcat");
            removeItem.Click += RemoveSelectedSlugcat;
            slugcatsMenu.DropDownItems.Add(spawnItem);
            slugcatsMenu.DropDownItems.Add(nextItem);
            slugcatsMenu.DropDownItems.Add(removeItem);
            slugcatsMenu.DropDownItems.Add(new ToolStripSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(slugcatsMenu);
            menu.Items.Add(variantMenu);
            menu.Items.Add(visualSkinMenu);
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
            RebuildDmsSkinMenu();
            if (!string.IsNullOrWhiteSpace(startDmsSkinId))
            {
                string reason;
                if (!gameLoop.SetDmsSkin(startDmsSkinId, out reason))
                    trayIcon.ShowBalloonTip(5000, "DMS skin unavailable", reason, ToolTipIcon.Warning);
                RebuildDmsSkinMenu();
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            renderingEnabled = false;
            Application.Idle -= ApplicationIdle;
            renderTimer.Stop();
            if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.Close();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.Close();
            for (int i = 0; i < gameLoops.Count; i++) gameLoops[i].Dispose();
            gameLoops.Clear();
            gameLoop = null;
            if (backBuffer != null) backBuffer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (applicationIcon != null) applicationIcon.Dispose();
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
                    RefreshSettingsWindow();
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
                RefreshSettingsWindow();
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
                    RefreshSettingsWindow();
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
                RefreshSettingsWindow();
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
                RefreshSettingsWindow();
                RenderFrame();
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                retryRenderItem.Enabled = true;
                RefreshSettingsWindow();
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
            trayIcon.Text = "SlugcatInMyMonitor · Active Slugcats: " + gameLoops.Count;
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
                skinEditor = new SkinEditorWindow(gameLoop, RefreshAppearanceMenus);
                if (applicationIcon != null) skinEditor.Icon = applicationIcon;
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
                if (item != null && item.Tag is SlugcatSkin)
                    item.Checked = ReferenceEquals(item, selected);
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
            RebuildDmsSkinMenu();
            RefreshSlugcatMenu();
            if (skinEditor != null && !skinEditor.IsDisposed) skinEditor.RefreshFromGame();
        }

        private void RefreshSkinAvailability()
        {
            if (gameLoop == null) return;
            for (int i = 0; i < visualSkinMenu.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = visualSkinMenu.DropDownItems[i] as ToolStripMenuItem;
                if (item == null || !(item.Tag is SlugcatSkin)) continue;
                string reason;
                SlugcatSkin skin = (SlugcatSkin)item.Tag;
                item.Enabled = gameLoop.CanUseSkin(skin, out reason);
                item.ToolTipText = reason ?? "Local Rain World PlayerGraphics assets available";
                item.Checked = skin == gameLoop.Skin;
            }
        }

        internal string[] SettingsSlugcatNames
        {
            get
            {
                string[] names = new string[gameLoops.Count];
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    GameLoop loop = gameLoops[i];
                    names[i] = "Slugcat " + (i + 1) + " · " +
                        (loop.Skin == SlugcatSkin.Default
                            ? loop.Appearance.Variant.ToString()
                            : loop.Skin.ToString());
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
        internal SlugcatVariant SettingsVariant
        { get { return gameLoop == null ? startVariant : gameLoop.Appearance.Variant; } }
        internal SlugcatSkin SettingsSkin
        { get { return gameLoop == null ? startSkin : gameLoop.Skin; } }
        internal IList<DmsSkinDefinition> SettingsDmsSkins
        { get { return gameLoop == null ? new DmsSkinDefinition[0] : gameLoop.DmsSkins; } }
        internal string SettingsActiveDmsSkinId
        { get { return gameLoop == null || gameLoop.ActiveDmsSkin == null
            ? null : gameLoop.ActiveDmsSkin.Id; } }

        internal void SettingsSelectSlugcat(int index)
        {
            if (index >= 0 && index < gameLoops.Count) SelectSlugcat(gameLoops[index]);
        }

        internal void SettingsAddSlugcat() { SpawnSlugcat(null, EventArgs.Empty); }
        internal void SettingsSelectNextSlugcat() { SelectNextSlugcat(null, EventArgs.Empty); }
        internal void SettingsRemoveSelectedSlugcat() { RemoveSelectedSlugcat(null, EventArgs.Empty); }
        internal void SettingsSetVariant(SlugcatVariant variant)
        {
            if (gameLoop == null) return;
            gameLoop.SetVariant(variant);
            RefreshAppearanceMenus();
        }

        internal bool SettingsTrySetSkin(SlugcatSkin skin, out string reason)
        {
            reason = null;
            if (gameLoop == null) return false;
            if (!gameLoop.SetSkin(skin))
            {
                gameLoop.CanUseSkin(skin, out reason);
                return false;
            }
            RefreshAppearanceMenus();
            return true;
        }

        internal bool SettingsCanUseSkin(SlugcatSkin skin, out string reason)
        {
            if (gameLoop == null)
            {
                reason = "No Slugcat is selected.";
                return false;
            }
            return gameLoop.CanUseSkin(skin, out reason);
        }

        internal bool SettingsTrySetDmsSkin(string id, out string reason)
        {
            reason = null;
            if (gameLoop == null)
            {
                reason = "No Slugcat is selected.";
                return false;
            }
            if (!gameLoop.SetDmsSkin(id, out reason)) return false;
            RebuildDmsSkinMenu();
            RefreshSlugcatMenu();
            return true;
        }

        internal string SettingsRefreshWorkshop()
        {
            RefreshAllWorkshopIntegrations();
            return gameLoop == null
                ? "No Slugcat is selected."
                : gameLoop.DmsSkins.Count + " Dress My Slugcat spritesheets found; Push To Meow " +
                  (gameLoop.PushToMeowAvailable ? "ready." : "unavailable.");
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

        private void VisualSkinMenuOpening(object sender, EventArgs e)
        {
            bool dirty = false;
            for (int index = 0; index < gameLoops.Count; index++)
                dirty |= gameLoops[index].WorkshopCatalog.HasPendingChanges;
            if (!dirty) return;
            try
            {
                RefreshAllWorkshopIntegrations();
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(5000, "Workshop refresh failed", exception.Message,
                    ToolTipIcon.Warning);
            }
        }

        private void RefreshWorkshopItemClick(object sender, EventArgs e)
        {
            if (gameLoop == null) return;
            try
            {
                string status = SettingsRefreshWorkshop();
                trayIcon.ShowBalloonTip(2500, "Workshop refreshed",
                    status, ToolTipIcon.Info);
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                trayIcon.ShowBalloonTip(5000, "Workshop refresh failed", exception.Message,
                    ToolTipIcon.Warning);
            }
        }

        private void RefreshAllWorkshopIntegrations()
        {
            for (int index = 0; index < gameLoops.Count; index++)
                gameLoops[index].RefreshWorkshopIntegration();
            RebuildDmsSkinMenu();
            RefreshSettingsWindow();
        }

        private void RebuildDmsSkinMenu()
        {
            foreach (ToolStripItem existing in dmsSkinMenu.DropDownItems)
                if (existing.Image != null) existing.Image.Dispose();
            dmsSkinMenu.DropDownItems.Clear();

            ToolStripMenuItem none = new ToolStripMenuItem("No DMS overlay");
            none.Tag = null;
            none.Checked = gameLoop == null || gameLoop.ActiveDmsSkin == null;
            none.Click += DmsSkinItemClick;
            dmsSkinMenu.DropDownItems.Add(none);
            if (gameLoop == null || gameLoop.DmsSkins.Count == 0)
            {
                ToolStripMenuItem empty = new ToolStripMenuItem("No compatible spritesheets found");
                empty.Enabled = false;
                dmsSkinMenu.DropDownItems.Add(empty);
                return;
            }

            dmsSkinMenu.DropDownItems.Add(new ToolStripSeparator());
            foreach (DmsSkinDefinition skin in gameLoop.DmsSkins)
            {
                string label = skin.Name + " — " + skin.Author + " (" + skin.ModName + ")";
                if (!skin.IsModActive) label = "[Inactive] " + label;
                ToolStripMenuItem item = new ToolStripMenuItem(label);
                item.Tag = skin.Id;
                item.Enabled = skin.IsModActive;
                item.Checked = gameLoop.ActiveDmsSkin != null &&
                    string.Equals(gameLoop.ActiveDmsSkin.Id, skin.Id,
                        StringComparison.OrdinalIgnoreCase);
                item.ToolTipText = "DMS id: " + skin.Id + Environment.NewLine +
                    "Available parts: " + string.Join(", ", skin.AvailableParts) + Environment.NewLine +
                    "Source: " + skin.DirectoryPath;
                try { item.Image = skin.CreatePreview(32); }
                catch (Exception) { }
                item.Click += DmsSkinItemClick;
                dmsSkinMenu.DropDownItems.Add(item);
            }
        }

        private void DmsSkinItemClick(object sender, EventArgs e)
        {
            ToolStripMenuItem selected = sender as ToolStripMenuItem;
            if (selected == null || gameLoop == null) return;
            string reason;
            if (!gameLoop.SetDmsSkin(selected.Tag as string, out reason))
            {
                trayIcon.ShowBalloonTip(4000, "DMS skin unavailable", reason,
                    ToolTipIcon.Warning);
                return;
            }
            RebuildDmsSkinMenu();
            RefreshSettingsWindow();
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
