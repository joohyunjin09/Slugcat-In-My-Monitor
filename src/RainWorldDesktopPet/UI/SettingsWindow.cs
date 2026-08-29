using System;
using System.Drawing;
using System.Windows.Forms;
using RainWorldDesktopPet.Core;
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
        private readonly ComboBox sizeSelector;
        private readonly ComboBox languageSelector;
        private readonly CheckBox debugCheck;
        private readonly CheckBox pauseCheck;
        private readonly Button retryButton;
        private readonly Label statusLabel;
        private bool updating;

        public SettingsWindow(LayeredOverlayWindow app)
        {
            if (app == null) throw new ArgumentNullException("app");
            this.app = app;

            Text = T("SlugcatInMyMonitor 설정", "SlugcatInMyMonitor Settings");
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

            GroupBox slugcatsGroup = new GroupBox { Text = T("슬러그캣", "Slugcats"), Dock = DockStyle.Fill };
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
            addButton = ActionButton(T("슬러그캣 추가", "Add Slugcat"), delegate { app.SettingsAddSlugcat(); RefreshFromApp(); });
            nextButton = ActionButton(T("다음 선택", "Select Next"), delegate { app.SettingsSelectNextSlugcat(); RefreshFromApp(); });
            removeButton = ActionButton(T("선택 항목 삭제", "Remove Selected"), delegate { app.SettingsRemoveSelectedSlugcat(); RefreshFromApp(); });
            slugcatActions.Controls.Add(addButton);
            slugcatActions.Controls.Add(nextButton);
            slugcatActions.Controls.Add(removeButton);
            slugcatsLayout.Controls.Add(slugcatActions, 0, 1);
            slugcatsGroup.Controls.Add(slugcatsLayout);
            root.Controls.Add(slugcatsGroup, 0, 0);

            GroupBox appearanceGroup = new GroupBox
            {
                Text = T("선택한 슬러그캣", "Selected Slugcat"),
                Dock = DockStyle.Fill
            };
            TableLayoutPanel appearanceLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 2,
                RowCount = 3
            };
            appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            appearanceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            appearanceLayout.Controls.Add(FieldLabel(T("캐릭터와 능력", "Character and Ability")), 0, 0);
            characterSelector = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            for (int i = 0; i < SlugcatProfiles.All.Count; i++)
                characterSelector.Items.Add(new CharacterChoice(SlugcatProfiles.All[i]));
            characterSelector.SelectedIndexChanged += CharacterChanged;
            appearanceLayout.Controls.Add(characterSelector, 1, 0);
            appearanceLayout.Controls.Add(FieldLabel(T("크기", "Size")), 0, 1);
            sizeSelector = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            sizeSelector.Items.Add(new SizeChoice(SlugcatSize.Small,
                T("작게 (1920×1080 원작 크기)",
                    "Small (original size at 1920x1080)")));
            sizeSelector.Items.Add(new SizeChoice(SlugcatSize.Normal,
                T("보통 (작게와 크게의 중간)",
                    "Normal (midpoint of small and large)")));
            sizeSelector.Items.Add(new SizeChoice(SlugcatSize.Large,
                T("크게 (2.2배, 기존 크기)", "Large (2.2x, previous size)")));
            sizeSelector.SelectedIndexChanged += SlugcatSizeChanged;
            appearanceLayout.Controls.Add(sizeSelector, 1, 1);
            FlowLayoutPanel appearanceActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true
            };
            Button editorButton = ActionButton(T("실험적 스킨 편집기 열기", "Open Experimental Skin Editor"), delegate
            {
                app.SettingsOpenAppearanceEditor();
            });
            appearanceActions.Controls.Add(editorButton);
            appearanceActions.Controls.Add(ActionButton(T("Workshop 새로 고침", "Refresh Workshop"), RefreshWorkshop));
            appearanceLayout.SetColumnSpan(appearanceActions, 2);
            appearanceLayout.Controls.Add(appearanceActions, 0, 2);
            appearanceGroup.Controls.Add(appearanceLayout);
            root.Controls.Add(appearanceGroup, 0, 1);

            GroupBox behaviorGroup = new GroupBox { Text = T("프로그램", "Application"), Dock = DockStyle.Fill };
            FlowLayoutPanel behaviorLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            debugCheck = new CheckBox { Text = T("디버그 오버레이", "Debug Overlay"), AutoSize = true, Margin = new Padding(3, 9, 18, 3) };
            debugCheck.CheckedChanged += delegate
            {
                if (!updating) app.SettingsDebugEnabled = debugCheck.Checked;
            };
            pauseCheck = new CheckBox { Text = T("모든 슬러그캣 일시 정지", "Pause All Slugcats"), AutoSize = true, Margin = new Padding(3, 9, 18, 3) };
            pauseCheck.CheckedChanged += delegate
            {
                if (!updating) app.SettingsPaused = pauseCheck.Checked;
            };
            retryButton = ActionButton(T("렌더링 재시도", "Retry Rendering"), delegate { app.SettingsRetryRendering(); RefreshFromApp(); });
            behaviorLayout.Controls.Add(debugCheck);
            behaviorLayout.Controls.Add(pauseCheck);
            behaviorLayout.Controls.Add(retryButton);
            behaviorLayout.Controls.Add(new Label
            {
                Text = T("언어", "Language"),
                AutoSize = true,
                Margin = new Padding(3, 10, 3, 3)
            });
            languageSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 105,
                Margin = new Padding(3, 6, 3, 3)
            };
            languageSelector.Items.Add(new LanguageChoice(UiLanguage.Korean, "한국어"));
            languageSelector.Items.Add(new LanguageChoice(UiLanguage.English, "English"));
            languageSelector.SelectedIndexChanged += LanguageChanged;
            behaviorLayout.Controls.Add(languageSelector);
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
            footer.Controls.Add(ActionButton(T("닫기", "Close"), delegate { Close(); }));
            footer.Controls.Add(ActionButton(T("프로그램 종료", "Exit Application"), delegate { app.SettingsExitApplication(); }));
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
                for (int i = 0; i < languageSelector.Items.Count; i++)
                {
                    LanguageChoice language = languageSelector.Items[i] as LanguageChoice;
                    if (language != null && language.Id == UiLocalization.Current)
                    {
                        languageSelector.SelectedIndex = i;
                        break;
                    }
                }

                for (int i = 0; i < characterSelector.Items.Count; i++)
                {
                    CharacterChoice choice = characterSelector.Items[i] as CharacterChoice;
                    if (choice != null && choice.Id == app.SettingsSlugcatId)
                    {
                        characterSelector.SelectedIndex = i;
                        break;
                    }
                }
                for (int i = 0; i < sizeSelector.Items.Count; i++)
                {
                    SizeChoice choice = sizeSelector.Items[i] as SizeChoice;
                    if (choice != null && choice.Id == app.SettingsSlugcatSize)
                    {
                        sizeSelector.SelectedIndex = i;
                        break;
                    }
                }
                statusLabel.Text = T("실행 중인 슬러그캣: " + names.Length +
                        "마리 · 트레이 아이콘을 왼쪽 클릭하면 이 창을 다시 열 수 있습니다.",
                    names.Length + " active Slugcat" + (names.Length == 1 ? string.Empty : "s") +
                        ". Left-click the tray icon to reopen this window.");
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

        private void SlugcatSizeChanged(object sender, EventArgs e)
        {
            if (updating) return;
            SizeChoice choice = sizeSelector.SelectedItem as SizeChoice;
            if (choice == null) return;
            app.SettingsSetSlugcatSize(choice.Id);
            RefreshFromApp();
        }

        private void LanguageChanged(object sender, EventArgs e)
        {
            if (updating) return;
            LanguageChoice choice = languageSelector.SelectedItem as LanguageChoice;
            if (choice == null || choice.Id == UiLocalization.Current) return;
            app.SettingsSetLanguage(choice.Id);
            statusLabel.Text = T(
                "언어 설정을 저장했습니다. 프로그램을 다시 시작하면 모든 UI에 적용됩니다.",
                "Language saved. Restart the application to apply it to the entire UI.");
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
                statusLabel.Text = T("Workshop 새로 고침 실패: ",
                    "Workshop refresh failed: ") + exception.Message;
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

        private static string T(string korean, string english)
        { return UiLocalization.Text(korean, english); }

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

        private sealed class LanguageChoice
        {
            public LanguageChoice(UiLanguage id, string name)
            { Id = id; Name = name; }
            public readonly UiLanguage Id;
            public readonly string Name;
            public override string ToString() { return Name; }
        }

        private sealed class SizeChoice
        {
            public SizeChoice(SlugcatSize id, string name)
            { Id = id; Name = name; }
            public readonly SlugcatSize Id;
            public readonly string Name;
            public override string ToString() { return Name; }
        }

    }
}
