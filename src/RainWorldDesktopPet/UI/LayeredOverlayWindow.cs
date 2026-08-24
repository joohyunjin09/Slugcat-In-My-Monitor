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
        private readonly ToolStripMenuItem soundItem;
        private readonly ToolStripMenuItem activeSlugcatsMenu;
        private readonly ToolStripMenuItem spawnItem;
        private readonly ToolStripMenuItem removeItem;
        private readonly ToolStripMenuItem skinEditorItem;
        private readonly List<GameLoop> gameLoops = new List<GameLoop>();
        private readonly Dictionary<string, double> displayRefreshRates =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly DesktopCollisionWorld collisionWorld =
            new DesktopCollisionWorld(new WindowEnumerator());
        private readonly Stopwatch surfaceRefreshClock = Stopwatch.StartNew();
        private DirectCompositionHost compositionHost;
        private readonly AppSettings appSettings;
        private readonly string startDmsSkinId;
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
            appSettings = AppSettings.Load();
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
            soundItem = new ToolStripMenuItem("Sound (ON)");
            soundItem.CheckOnClick = true;
            soundItem.Checked = appSettings.SoundEnabled;
            soundItem.Text = soundItem.Checked ? "Sound (ON)" : "Sound (OFF)";
            soundItem.CheckedChanged += delegate
            {
                appSettings.SoundEnabled = soundItem.Checked;
                soundItem.Text = soundItem.Checked ? "Sound (ON)" : "Sound (OFF)";
                for (int i = 0; i < gameLoops.Count; i++)
                    gameLoops[i].SoundEnabled = soundItem.Checked;
                try { appSettings.Save(); }
                catch (Exception exception) { Program.LogException(exception); }
                RefreshSettingsWindow();
            };
            retryRenderItem = new ToolStripMenuItem("Retry Rendering");
            retryRenderItem.Enabled = false;
            retryRenderItem.Click += RetryRendering;
            skinEditorItem = new ToolStripMenuItem("Skin Editor (Experimental)");
            skinEditorItem.Click += ToggleSkinEditor;
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { Close(); };
            slugcatMenu = new ToolStripMenuItem("Character and Ability");
            for (int i = 0; i < SlugcatProfiles.All.Count; i++)
            {
                SlugcatProfile profile = SlugcatProfiles.All[i];
                slugcatMenu.DropDownItems.Add(CreateSlugcatItem(
                    SlugcatProfiles.SelectionLabel(profile.Id), profile.Id, startSlugcat));
            }
            refreshWorkshopItem = new ToolStripMenuItem("Refresh Workshop mods");
            refreshWorkshopItem.Click += RefreshWorkshopItemClick;
            activeSlugcatsMenu = new ToolStripMenuItem("Slugcats");
            spawnItem = new ToolStripMenuItem("Add Slugcat");
            spawnItem.Click += SpawnSlugcat;
            ToolStripMenuItem nextItem = new ToolStripMenuItem("Select Next Slugcat");
            nextItem.Click += SelectNextSlugcat;
            removeItem = new ToolStripMenuItem("Remove Selected Slugcat");
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
            menu.Items.Add(soundItem);
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
            AddSlugcat(startSlugcat);
            if (!string.IsNullOrWhiteSpace(startDmsSkinId))
            {
                string reason;
                if (!gameLoop.SetDmsSkin(startDmsSkinId, out reason))
                    trayIcon.ShowBalloonTip(5000, "DMS skin unavailable", reason, ToolTipIcon.Warning);
            }
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
                        loop.World, loop.Slugcat, loop.AI, loop.AssetStatus, loop.SelectedSlugcat);
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

        private void AddSlugcat(SlugcatId id)
        {
            if (gameLoops.Count >= MaximumSlugcats) return;
            GameLoop added = new GameLoop(Handle, installation, id,
                gameLoops.Count, collisionWorld);
            added.DebugEnabled = debugItem.Checked;
            added.Paused = pauseItem.Checked;
            added.SoundEnabled = soundItem.Checked;
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
            try
            {
                AddSlugcat(gameLoop == null ? startSlugcat : gameLoop.SelectedSlugcat.Id);
            }
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
                    "Slugcat " + (i + 1) + " · " + loop.SelectedSlugcat.DisplayName);
                item.Tag = loop;
                item.Checked = ReferenceEquals(loop, gameLoop);
                item.Click += delegate(object itemSender, EventArgs args)
                {
                    ToolStripMenuItem clicked = itemSender as ToolStripMenuItem;
                    if (clicked != null) SelectSlugcat(clicked.Tag as GameLoop);
                };
                activeSlugcatsMenu.DropDownItems.Add(item);
            }
            activeSlugcatsMenu.Text = "Slugcats (" + gameLoops.Count + ")";
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
                trayIcon.ShowBalloonTip(5000, "Skin editor failed",
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
                    names[i] = "Slugcat " + (i + 1) + " · " +
                        loop.SelectedSlugcat.DisplayName;
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
        internal bool SettingsSoundEnabled
        {
            get { return soundItem.Checked; }
            set { soundItem.Checked = value; }
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
            RefreshSettingsWindow();
        }

        private static Vec2 ScreenPointFromLParam(IntPtr value)
        {
            long packed = value.ToInt64();
            int x = (short)(packed & 0xffff);
            int y = (short)((packed >> 16) & 0xffff);
            return new Vec2(x, y);
        }
    }
}
