using System;
using System.Drawing;
using System.Windows.Forms;
using RainWorldDesktopPet.Creature;

namespace RainWorldDesktopPet.UI
{
    internal sealed class SettingsWindow : Form
    {
        private readonly LayeredOverlayWindow app;
        private readonly ListBox slugcatList;
        private readonly Button addButton;
        private readonly Button nextButton;
        private readonly Button removeButton;
        private readonly ComboBox characterSelector;
        private readonly CheckBox debugCheck;
        private readonly CheckBox pauseCheck;
        private readonly Button retryButton;
        private readonly Label statusLabel;
        private bool updating;

        public SettingsWindow(LayeredOverlayWindow app)
        {
            if (app == null) throw new ArgumentNullException("app");
            this.app = app;

            Text = "SlugcatInMyMonitor Settings";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimumSize = new Size(560, 500);
            ClientSize = new Size(640, 560);
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(root);

            GroupBox slugcatsGroup = new GroupBox { Text = "Slugcats", Dock = DockStyle.Fill };
            TableLayoutPanel slugcatsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 2
            };
            slugcatsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            slugcatsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            slugcatList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            slugcatList.SelectedIndexChanged += SlugcatSelectionChanged;
            slugcatsLayout.Controls.Add(slugcatList, 0, 0);
            FlowLayoutPanel slugcatActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0)
            };
            addButton = ActionButton("Add Slugcat", delegate { app.SettingsAddSlugcat(); RefreshFromApp(); });
            nextButton = ActionButton("Select Next", delegate { app.SettingsSelectNextSlugcat(); RefreshFromApp(); });
            removeButton = ActionButton("Remove Selected", delegate { app.SettingsRemoveSelectedSlugcat(); RefreshFromApp(); });
            slugcatActions.Controls.Add(addButton);
            slugcatActions.Controls.Add(nextButton);
            slugcatActions.Controls.Add(removeButton);
            slugcatsLayout.Controls.Add(slugcatActions, 0, 1);
            slugcatsGroup.Controls.Add(slugcatsLayout);
            root.Controls.Add(slugcatsGroup, 0, 0);

            GroupBox appearanceGroup = new GroupBox
            {
                Text = "Selected Slugcat Character",
                Dock = DockStyle.Fill
            };
            TableLayoutPanel appearanceLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 2,
                RowCount = 2
            };
            appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            appearanceLayout.Controls.Add(FieldLabel("Character and Ability"), 0, 0);
            characterSelector = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            for (int i = 0; i < SlugcatProfiles.All.Count; i++)
                characterSelector.Items.Add(new CharacterChoice(SlugcatProfiles.All[i]));
            characterSelector.SelectedIndexChanged += CharacterChanged;
            appearanceLayout.Controls.Add(characterSelector, 1, 0);
            FlowLayoutPanel appearanceActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true
            };
            Button editorButton = ActionButton("Open Experimental Skin Editor", delegate
            {
                app.SettingsOpenAppearanceEditor();
            });
            appearanceActions.Controls.Add(editorButton);
            appearanceActions.Controls.Add(ActionButton("Refresh Workshop", RefreshWorkshop));
            appearanceLayout.SetColumnSpan(appearanceActions, 2);
            appearanceLayout.Controls.Add(appearanceActions, 0, 1);
            appearanceGroup.Controls.Add(appearanceLayout);
            root.Controls.Add(appearanceGroup, 0, 1);

            GroupBox behaviorGroup = new GroupBox { Text = "Application", Dock = DockStyle.Fill };
            FlowLayoutPanel behaviorLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            debugCheck = new CheckBox { Text = "Debug Overlay", AutoSize = true, Margin = new Padding(3, 9, 18, 3) };
            debugCheck.CheckedChanged += delegate
            {
                if (!updating) app.SettingsDebugEnabled = debugCheck.Checked;
            };
            pauseCheck = new CheckBox { Text = "Pause All Slugcats", AutoSize = true, Margin = new Padding(3, 9, 18, 3) };
            pauseCheck.CheckedChanged += delegate
            {
                if (!updating) app.SettingsPaused = pauseCheck.Checked;
            };
            retryButton = ActionButton("Retry Rendering", delegate { app.SettingsRetryRendering(); RefreshFromApp(); });
            behaviorLayout.Controls.Add(debugCheck);
            behaviorLayout.Controls.Add(pauseCheck);
            behaviorLayout.Controls.Add(retryButton);
            behaviorGroup.Controls.Add(behaviorLayout);
            root.Controls.Add(behaviorGroup, 0, 2);

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(statusLabel, 0, 3);

            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };
            footer.Controls.Add(ActionButton("Close", delegate { Close(); }));
            footer.Controls.Add(ActionButton("Exit Application", delegate { app.SettingsExitApplication(); }));
            root.Controls.Add(footer, 0, 4);

            Activated += delegate { RefreshFromApp(); };
            RefreshFromApp();
        }

        internal void RefreshFromApp()
        {
            if (IsDisposed) return;
            updating = true;
            try
            {
                string[] names = app.SettingsSlugcatNames;
                slugcatList.BeginUpdate();
                try
                {
                    slugcatList.Items.Clear();
                    slugcatList.Items.AddRange(names);
                    int selected = app.SettingsSelectedSlugcatIndex;
                    slugcatList.SelectedIndex = selected >= 0 && selected < names.Length ? selected : -1;
                }
                finally { slugcatList.EndUpdate(); }

                addButton.Enabled = app.SettingsCanAddSlugcat;
                nextButton.Enabled = app.SettingsCanSelectNextSlugcat;
                removeButton.Enabled = app.SettingsCanRemoveSlugcat;
                retryButton.Enabled = app.SettingsCanRetryRendering;
                debugCheck.Checked = app.SettingsDebugEnabled;
                pauseCheck.Checked = app.SettingsPaused;

                for (int i = 0; i < characterSelector.Items.Count; i++)
                {
                    CharacterChoice choice = characterSelector.Items[i] as CharacterChoice;
                    if (choice != null && choice.Id == app.SettingsSlugcatId)
                    {
                        characterSelector.SelectedIndex = i;
                        break;
                    }
                }
                statusLabel.Text = names.Length + " active Slugcat" + (names.Length == 1 ? string.Empty : "s") +
                    ". Left-click the tray icon to reopen this window.";
            }
            finally { updating = false; }
        }

        private void SlugcatSelectionChanged(object sender, EventArgs e)
        {
            if (updating || slugcatList.SelectedIndex < 0) return;
            app.SettingsSelectSlugcat(slugcatList.SelectedIndex);
            RefreshFromApp();
        }

        private void CharacterChanged(object sender, EventArgs e)
        {
            if (updating) return;
            CharacterChoice choice = characterSelector.SelectedItem as CharacterChoice;
            if (choice == null) return;
            app.SettingsSetSlugcat(choice.Id);
            RefreshFromApp();
        }

        private void RefreshWorkshop()
        {
            try
            {
                string status = app.SettingsRefreshWorkshop();
                RefreshFromApp();
                statusLabel.Text = status;
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                statusLabel.Text = "Workshop refresh failed: " + exception.Message;
            }
        }

        private static Label FieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button ActionButton(string text, Action click)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(105, 30) };
            button.Click += delegate { click(); };
            return button;
        }

        private sealed class CharacterChoice
        {
            public CharacterChoice(SlugcatProfile profile)
            {
                Id = profile.Id;
                Name = SlugcatProfiles.SelectionLabel(profile.Id);
            }
            public readonly SlugcatId Id;
            public readonly string Name;
            public override string ToString()
            { return Name; }
        }

    }
}
