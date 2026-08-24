using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.Physics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Creature;

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
        private readonly RainWorldInstallation installation;
        private readonly SlugcatVariant startVariant;
        private readonly SlugcatSkin startSkin;
        private readonly Timer renderTimer;
        private readonly NotifyIcon trayIcon;
        private readonly Icon applicationIcon;
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
        private readonly Dictionary<string, double> displayRefreshRates =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly DesktopCollisionWorld collisionWorld =
            new DesktopCollisionWorld(new WindowEnumerator());
        private readonly Stopwatch surfaceRefreshClock = Stopwatch.StartNew();
        private DirectCompositionHost compositionHost;
        private GameLoop gameLoop;
        private GameLoop grabbedGameLoop;
        private SettingsWindow settingsWindow;
        private SkinEditorWindow skinEditor;
        private Rectangle virtualDesktopBounds;
        private bool mouseCaptured;
        private bool leftButtonDown;
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
                parameters.ExStyle |= NativeMethods.WS_EX_NOREDIRECTIONBITMAP |
                                      NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
                                      NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST |
                                      NativeMethods.WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ConfigureVirtualDesktop();
            compositionHost = new DirectCompositionHost(Handle, virtualDesktopBounds);
            collisionWorld.Refresh(Handle);
            surfaceRefreshClock.Restart();
            AddSlugcat(startVariant, startSkin);
            RefreshSkinAvailability();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            renderingEnabled = false;
            renderTimer.Stop();
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
                SlugcatPose[] poses = new SlugcatPose[gameLoops.Count];
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    gameLoops[i].Advance(Handle);
                    poses[i] = gameLoops[i].BuildPose();
                }
                UpdateRenderCadence(poses);
                for (int i = 0; i < gameLoops.Count; i++)
                {
                    GameLoop loop = gameLoops[i];
                    bool debug = loop.DebugEnabled && ReferenceEquals(loop, gameLoop);
                    Rectangle surfaceBounds = CalculateRenderBounds(poses[i], debug);
                    DirectCompositionHost.CompositionSurface surface =
                        compositionHost.PrepareSurface(i, surfaceBounds);
                    System.Drawing.Graphics graphics = surface.Graphics;
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                    loop.Renderer.Render(graphics, poses[i], new RenderSpace(surfaceBounds), debug,
                        loop.World, loop.Slugcat, loop.AI, loop.AssetStatus, loop.Appearance);
                    compositionHost.Present(i);
                }
                compositionHost.Commit(gameLoops.Count);
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
                trayIcon.ShowBalloonTip(5000, "Slugcat rendering paused",
                    exception.Message + " Use Retry rendering from the tray menu.", ToolTipIcon.Error);
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
                trayIcon.ShowBalloonTip(5000, "Slugcat retry failed", exception.Message, ToolTipIcon.Error);
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
            if (surfaceRefreshClock.Elapsed.TotalSeconds <
                SimulationConstants.WindowRefreshSeconds) return;

            collisionWorld.Refresh(Handle);
            for (int i = 0; i < gameLoops.Count; i++)
                gameLoops[i].ApplyMovingSurfaceDelta();
            surfaceRefreshClock.Restart();
        }

        private void UpdateRenderCadence(SlugcatPose[] poses)
        {
            double targetRefreshRate = 0.0;
            for (int i = 0; i < poses.Length; i++)
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
            bool currentlyDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
            if (currentlyDown && !leftButtonDown && gameLoop != null)
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
                    }
                }
            }
            else if (!currentlyDown && leftButtonDown && mouseCaptured)
            {
                mouseCaptured = false;
                if (grabbedGameLoop != null) grabbedGameLoop.EndGrab();
                grabbedGameLoop = null;
            }
            leftButtonDown = currentlyDown;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_DISPLAYCHANGE || message.Msg == NativeMethods.WM_DPICHANGED)
            {
                try
                {
                    ConfigureVirtualDesktop();
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

        private GameLoop FindSlugcatAt(Vec2 point)
        {
            for (int i = gameLoops.Count - 1; i >= 0; i--)
                if (gameLoops[i].HitTest(point)) return gameLoops[i];
            return null;
        }

        private void AddSlugcat(SlugcatVariant variant, SlugcatSkin skin)
        {
            if (gameLoops.Count >= MaximumSlugcats) return;
            GameLoop added = new GameLoop(Handle, installation, variant, skin,
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

    }
}
