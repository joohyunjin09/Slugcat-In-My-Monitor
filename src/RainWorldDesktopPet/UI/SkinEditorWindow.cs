using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.Graphics;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.UI
{
    public sealed class SkinEditorWindow : Form
    {
        private static readonly string[] PartNames = DmsSpriteGroups.SelectableParts;

        private static readonly CharacterChoice[] Characters = BuildCharacterChoices();

        private readonly GameLoop gameLoop;
        private readonly Action stateChanged;
        private readonly Dictionary<string, ComboBox> partSelectors =
            new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> colorButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> partSelections =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ListBox characterList;
        private readonly Panel previewPanel;
        private readonly Label assetLabel;
        private readonly ToolStripStatusLabel statusLabel;
        private bool updatingControls;

        public SkinEditorWindow(GameLoop gameLoop, Action stateChanged)
        {
            if (gameLoop == null) throw new ArgumentNullException("gameLoop");
            this.gameLoop = gameLoop;
            this.stateChanged = stateChanged;
            for (int i = 0; i < PartNames.Length; i++)
                partSelections[PartNames[i]] = gameLoop.GetDmsPartSelection(PartNames[i]) ?? "default";

            Text = T("슬러그캣 스킨 편집기", "Slugcat Skin Editor");
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimumSize = new Size(920, 720);
            ClientSize = new Size(1120, 820);
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10),
                ColumnCount = 3, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(root);

            Label experimentalNotice = new Label
            {
                Text = T(
                    "각 파츠는 하나의 스프라이트 출처를 사용합니다. 불완전한 DMS 파츠는 현재 슬러그캣의 기본 외형으로 표시됩니다.",
                    "Each part has one explicit source. Incomplete DMS parts fall back to the current Vanilla Slugcat."),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(255, 244, 204),
                ForeColor = Color.FromArgb(95, 69, 0),
                Padding = new Padding(10, 0, 10, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.SetColumnSpan(experimentalNotice, 3);
            root.Controls.Add(experimentalNotice, 0, 0);

            GroupBox characterGroup = new GroupBox { Text = T("슬러그캣", "Slugcat"), Dock = DockStyle.Fill };
            characterList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            for (int i = 0; i < Characters.Length; i++) characterList.Items.Add(Characters[i]);
            characterList.SelectedIndexChanged += CharacterChanged;
            characterGroup.Controls.Add(characterList);
            root.Controls.Add(characterGroup, 0, 1);

            GroupBox partsGroup = new GroupBox { Text = T("외형 파츠", "Appearance Parts"), Dock = DockStyle.Fill };
            TableLayoutPanel parts = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8),
                ColumnCount = 3, RowCount = PartNames.Length };
            parts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            parts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            parts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            for (int i = 0; i < PartNames.Length; i++)
            {
                string part = PartNames[i];
                parts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                Label label = new Label { Text = PartDisplayName(part), Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft };
                ComboBox selector = new ComboBox { Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList, Tag = part };
                selector.SelectedIndexChanged += PartSelectionChanged;
                Button color = new Button { Text = T("색상...", "Color..."), Dock = DockStyle.Fill, Tag = part,
                    UseVisualStyleBackColor = false };
                color.Click += CustomizeColor;
                partSelectors[part] = selector;
                colorButtons[part] = color;
                parts.Controls.Add(label, 0, i);
                parts.Controls.Add(selector, 1, i);
                parts.Controls.Add(color, 2, i);
            }
            partsGroup.Controls.Add(parts);
            root.Controls.Add(partsGroup, 1, 1);

            GroupBox previewGroup = new GroupBox { Text = T("미리보기", "Preview"), Dock = DockStyle.Fill };
            TableLayoutPanel previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill,
                RowCount = 2, ColumnCount = 1 };
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 32, 36) };
            previewPanel.Paint += DrawPreview;
            assetLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true, Padding = new Padding(6) };
            previewLayout.Controls.Add(previewPanel, 0, 0);
            previewLayout.Controls.Add(assetLabel, 0, 1);
            previewGroup.Controls.Add(previewLayout);
            root.Controls.Add(previewGroup, 2, 1);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Padding = new Padding(0, 7, 0, 0) };
            actions.Controls.Add(ActionButton(T("닫기", "Close"), delegate { Close(); }));
            actions.Controls.Add(ActionButton(T("스프라이트 새로 고침", "Reload Sprites"), ReloadCatalog));
            actions.Controls.Add(ActionButton(T("프리셋 불러오기...", "Load Preset..."), LoadPreset));
            actions.Controls.Add(ActionButton(T("프리셋 저장...", "Save Preset..."), SavePreset));
            actions.Controls.Add(ActionButton(T("붙여넣기", "Paste"), PasteSetup));
            actions.Controls.Add(ActionButton(T("복사", "Copy"), CopySetup));
            actions.Controls.Add(ActionButton(T("초기화", "Reset"), ResetAll));
            root.SetColumnSpan(actions, 3);
            root.Controls.Add(actions, 0, 2);

            StatusStrip status = new StatusStrip();
            statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.Add(statusLabel);
            Controls.Add(status);

            PopulateSpriteSelectors();
            RefreshFromGame();
            SetStatus(T("DMS 스프라이트 시트 " + gameLoop.DmsSkins.Count +
                    "개를 불러왔습니다. 파츠 선택에는 설치된 DMS 규칙이 적용됩니다.",
                gameLoop.DmsSkins.Count +
                    " DMS spritesheets loaded. Part choices follow the installed DMS rules."));
        }

        public void RefreshFromGame()
        {
            updatingControls = true;
            try
            {
                for (int i = 0; i < Characters.Length; i++)
                    if (Characters[i].Id == gameLoop.SelectedSlugcat.Id)
                        characterList.SelectedIndex = i;
                for (int i = 0; i < PartNames.Length; i++)
                {
                    string part = PartNames[i];
                    partSelections[part] = gameLoop.GetDmsPartSelection(part) ?? "default";
                    ComboBox selector = partSelectors[part];
                    int selected = FindChoice(selector, partSelections[part]);
                    selector.SelectedIndex = selected < 0 ? 0 : selected;
                    Color color = gameLoop.GetPartColor(part);
                    colorButtons[part].BackColor = color;
                    colorButtons[part].ForeColor = ReadableText(color);
                }
                assetLabel.Text = gameLoop.AssetStatus;
            }
            finally { updatingControls = false; }
            previewPanel.Invalidate();
        }

        private void PopulateSpriteSelectors()
        {
            updatingControls = true;
            try
            {
                for (int i = 0; i < PartNames.Length; i++)
                {
                    string part = PartNames[i];
                    ComboBox selector = partSelectors[part];
                    string selectedId = partSelections[part];
                    selector.Items.Clear();
                    selector.Items.Add(new SpriteChoice(T("기본 ", "Vanilla ") +
                        SlugcatProfiles.SelectionLabel(gameLoop.SelectedSlugcat.Id), "default", null));
                    for (int j = 0; j < gameLoop.DmsSkins.Count; j++)
                    {
                        DmsSkinDefinition set = gameLoop.DmsSkins[j];
                        if (set.IsModActive && set.HasPart(part))
                            selector.Items.Add(new SpriteChoice(set.Name + " — " +
                                PartDisplayName(part), set.Id, set));
                    }
                    selector.SelectedIndex = FindChoice(selector, selectedId);
                    if (selector.SelectedIndex < 0) selector.SelectedIndex = 0;
                }
            }
            finally { updatingControls = false; }
        }

        private void CharacterChanged(object sender, EventArgs e)
        {
            if (updatingControls) return;
            CharacterChoice choice = characterList.SelectedItem as CharacterChoice;
            if (choice == null) return;
            gameLoop.SetSelectedSlugcat(choice.Id);
            PopulateSpriteSelectors();
            Changed(T(choice.Name + "을(를) 적용했습니다.", choice.Name + " applied."));
        }

        private void PartSelectionChanged(object sender, EventArgs e)
        {
            if (updatingControls) return;
            ComboBox selector = sender as ComboBox;
            SpriteChoice choice = selector == null ? null : selector.SelectedItem as SpriteChoice;
            string part = selector == null ? null : selector.Tag as string;
            if (choice == null || part == null) return;
            ApplySpriteChoice(part, choice.Set);
            Changed(T(PartDisplayName(part) + " 스프라이트를 " + choice.Name + "(으)로 변경했습니다.",
                part + " sprite changed to " + choice.Name + "."));
        }

        private void ApplySpriteChoice(string part, DmsSkinDefinition set)
        {
            if (set == null)
            {
                string clearReason;
                gameLoop.SetDmsPart(part, null, out clearReason);
                partSelections[part] = "default";
                return;
            }
            string reason;
            if (!gameLoop.SetDmsPart(part, set.Id, out reason))
            { SetStatus(T(set.Name + " 선택 실패: ", "Could not select " + set.Name + ": ") + reason); return; }
            partSelections[part] = set.Id;
        }

        private void CustomizeColor(object sender, EventArgs e)
        {
            Button button = sender as Button;
            string part = button == null ? null : button.Tag as string;
            if (part == null) return;
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = gameLoop.GetPartColor(part);
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                gameLoop.SetPartColor(part, dialog.Color);
                button.BackColor = dialog.Color;
                button.ForeColor = ReadableText(dialog.Color);
                Changed(T(PartDisplayName(part) + " 색상을 변경했습니다.",
                    part + " color changed."));
            }
        }

        private void ReloadCatalog(object sender, EventArgs e)
        {
            gameLoop.RefreshWorkshopIntegration();
            for (int i = 0; i < PartNames.Length; i++)
                partSelections[PartNames[i]] = gameLoop.GetDmsPartSelection(PartNames[i]) ?? "default";
            PopulateSpriteSelectors();
            SetStatus(T("DMS 스프라이트 시트 " + gameLoop.DmsSkins.Count + "개를 다시 불러왔습니다.",
                gameLoop.DmsSkins.Count + " DMS spritesheets reloaded."));
        }

        private void ResetAll(object sender, EventArgs e)
        {
            gameLoop.SetSelectedSlugcat(SlugcatId.White);
            gameLoop.ClearPartColors();
            gameLoop.ClearDmsParts();
            for (int i = 0; i < PartNames.Length; i++) partSelections[PartNames[i]] = "default";
            PopulateSpriteSelectors();
            RefreshFromGame();
            Changed(T("외형을 초기화했습니다.", "Appearance reset."));
        }

        private void CopySetup(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(BuildAppearanceData());
                SetStatus(T("외형 설정을 클립보드에 복사했습니다.",
                    "Appearance copied to the clipboard."));
            }
            catch (Exception exception) { SetStatus(T("복사 실패: ", "Copy failed: ") + exception.Message); }
        }

        private void PasteSetup(object sender, EventArgs e)
        {
            try
            {
                ApplyAppearanceData(Clipboard.GetText(), T(
                    "클립보드의 외형 설정을 적용했습니다.",
                    "Appearance pasted from the clipboard."));
            }
            catch (Exception exception) { SetStatus(T("붙여넣기 실패: ", "Paste failed: ") + exception.Message); }
        }

        private void SavePreset(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(PresetDirectory);
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = T("슬러그캣 외형 프리셋 저장", "Save Slugcat Appearance Preset");
                    dialog.InitialDirectory = PresetDirectory;
                    dialog.Filter = T("슬러그캣 외형 프리셋 (*.simmskin)|*.simmskin|모든 파일 (*.*)|*.*",
                        "Slugcat appearance preset (*.simmskin)|*.simmskin|All files (*.*)|*.*");
                    dialog.DefaultExt = "simmskin";
                    dialog.AddExtension = true;
                    dialog.OverwritePrompt = true;
                    dialog.FileName = CurrentCharacterName() + ".simmskin";
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(dialog.FileName, BuildAppearanceData(), Encoding.UTF8);
                    SetStatus(T("프리셋 저장 완료: ", "Preset saved: ") + Path.GetFileName(dialog.FileName));
                }
            }
            catch (Exception exception) { SetStatus(T("프리셋 저장 실패: ", "Preset save failed: ") + exception.Message); }
        }

        private void LoadPreset(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(PresetDirectory);
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = T("슬러그캣 외형 프리셋 불러오기", "Load Slugcat Appearance Preset");
                    dialog.InitialDirectory = PresetDirectory;
                    dialog.Filter = T("슬러그캣 외형 프리셋 (*.simmskin)|*.simmskin|모든 파일 (*.*)|*.*",
                        "Slugcat appearance preset (*.simmskin)|*.simmskin|All files (*.*)|*.*");
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    ApplyAppearanceData(File.ReadAllText(dialog.FileName, Encoding.UTF8),
                        T("프리셋 불러오기 완료: ", "Preset loaded: ") + Path.GetFileName(dialog.FileName));
                }
            }
            catch (Exception exception) { SetStatus(T("프리셋 불러오기 실패: ", "Preset load failed: ") + exception.Message); }
        }

        private string BuildAppearanceData()
        {
            StringBuilder value = new StringBuilder("SIMM_SKIN_V5|");
            value.Append(gameLoop.SelectedSlugcat.Id);
            for (int i = 0; i < PartNames.Length; i++)
            {
                string part = PartNames[i];
                value.Append('|').Append(part).Append('=').Append(partSelections[part]);
                value.Append(',').Append(gameLoop.GetPartColor(part).ToArgb().ToString("X8"));
                value.Append(',').Append(gameLoop.HasCustomPartColor(part) ? '1' : '0');
            }
            return value.ToString();
        }

        private void ApplyAppearanceData(string value, string successMessage)
        {
            string[] fields = (value ?? string.Empty).Trim().Split('|');
            SlugcatId character;
            int firstPart;
            bool hasExplicitColorFlags = fields.Length >= 2 && fields[0] == "SIMM_SKIN_V5";
            if (fields.Length >= 2 && (hasExplicitColorFlags || fields[0] == "SIMM_SKIN_V4" ||
                fields[0] == "SIMM_SKIN_V3"))
            {
                if (!SlugcatProfiles.TryParse(fields[1], out character))
                    throw new InvalidOperationException(T("프리셋의 캐릭터 정보가 올바르지 않습니다.",
                        "The preset character data is invalid."));
                firstPart = 2;
            }
            else if (fields.Length >= 3 && fields[0] == "SIMM_SKIN_V2")
            {
                SlugcatVariant variant;
                SlugcatSkin skin;
                if (!Enum.TryParse(fields[1], true, out variant) ||
                    !Enum.TryParse(fields[2], true, out skin))
                    throw new InvalidOperationException(T("프리셋의 캐릭터 정보가 올바르지 않습니다.",
                        "The preset character data is invalid."));
                character = LegacyCharacterId(variant, skin);
                firstPart = 3;
            }
            else
                throw new InvalidOperationException(T("슬러그캣 외형 프리셋 파일이 아닙니다.",
                    "The file is not a Slugcat appearance preset."));

            Dictionary<string, DmsSkinDefinition> sets =
                new Dictionary<string, DmsSkinDefinition>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Color> colors =
                new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> customizedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = firstPart; i < fields.Length; i++)
            {
                string serializedPart;
                string id;
                int colorStart;
                if (!TrySplitPresetPart(fields[i], out serializedPart, out id, out colorStart)) continue;
                string part = NormalizePresetPart(serializedPart);
                if (!partSelectors.ContainsKey(part)) continue;
                DmsSkinDefinition set = FindSet(id);
                if (!string.Equals(id, "default", StringComparison.OrdinalIgnoreCase))
                {
                    if (set == null)
                        throw new InvalidOperationException(T("필요한 스프라이트 세트가 설치되어 있지 않습니다: ",
                            "Required sprite set is not installed: ") + id);
                    if (!set.HasPart(part))
                        throw new InvalidOperationException(T(set.Name + "에 " + PartDisplayName(part) + " 파츠가 없습니다.",
                            set.Name + " does not contain " + part + "."));
                }
                sets[part] = set;
                uint argb;
                int colorEnd = fields[i].IndexOf(',', colorStart + 1);
                string colorText = colorEnd < 0
                    ? fields[i].Substring(colorStart + 1)
                    : fields[i].Substring(colorStart + 1, colorEnd - colorStart - 1);
                if (!uint.TryParse(colorText,
                    System.Globalization.NumberStyles.HexNumber, null, out argb))
                    throw new InvalidOperationException(T(PartDisplayName(part) + " 색상 정보가 올바르지 않습니다.",
                        "Invalid color for " + part + "."));
                colors[part] = Color.FromArgb(unchecked((int)argb));
                // V2-V4 stored every displayed colour but not whether it was
                // explicitly chosen. Preserve their rendered result. V5 stores
                // that distinction, letting an authored DMS PNG remain un-tinted.
                if (!hasExplicitColorFlags || colorEnd >= 0 &&
                    fields[i].Substring(colorEnd + 1).Trim() == "1")
                    customizedColors.Add(part);
            }

            // Loading a preset replaces the complete appearance state. V3 did
            // not contain the four Downpour special groups, so those must
            // explicitly become Vanilla rather than leaking from the current
            // V4/editor selection.
            gameLoop.SetSelectedSlugcat(character);
            gameLoop.ClearDmsParts();
            gameLoop.ClearPartColors();
            for (int i = 0; i < PartNames.Length; i++)
            {
                string part = PartNames[i];
                DmsSkinDefinition set;
                if (sets.TryGetValue(part, out set)) ApplySpriteChoice(part, set);
                Color color;
                if (customizedColors.Contains(part) && colors.TryGetValue(part, out color))
                    gameLoop.SetPartColor(part, color);
            }
            PopulateSpriteSelectors();
            RefreshFromGame();
            Changed(successMessage);
        }

        // V5 adds a second comma after the ARGB value. The skin ID always
        // ends at the first comma following '='; using the last comma turns
        // e.g. "homeobox.raincoatriv,FF91CCF0,0" into a nonexistent ID.
        internal static bool TrySplitPresetPart(string field, out string part, out string id,
            out int colorStart)
        {
            part = null;
            id = null;
            colorStart = -1;
            if (string.IsNullOrEmpty(field)) return false;
            int equals = field.IndexOf('=');
            colorStart = field.IndexOf(',', equals + 1);
            if (equals <= 0 || colorStart <= equals) return false;
            part = field.Substring(0, equals);
            id = field.Substring(equals + 1, colorStart - equals - 1);
            return true;
        }

        private static string PresetDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                    "SlugcatInMyMonitor", "presets");
            }
        }

        private void DrawPreview(object sender, PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            List<PreviewSprite> sprites = new List<PreviewSprite>();
            // Draw the tail first and extend it behind the body instead of
            // letting the preview texture project in front of the Slugcat.
            AddPreviewSprite(sprites, "Tail", -14.0f, 15.0f, 32.0f, 0.5, 0.5, 1.0f, 1.0f);
            AddPreviewSprite(sprites, "Legs", 0.0f, 20.0f, 0.0f, 0.5, 0.25, 1.0f, 1.0f);
            AddPreviewSprite(sprites, "Body", 0.0f, 0.0f, 0.0f, 0.5, 0.7894737, 1.0f, 1.0f);
            AddPreviewSprite(sprites, "Hips", 0.0f, 11.3f, 0.0f, 0.5, 0.5, 1.0f, 1.0f);
            // The runtime hides retracted arms like PlayerGraphics. The editor
            // deliberately previews both arm sprites so an Arms skin remains
            // inspectable even while the live slugcat is idle.
            AddPreviewSprite(sprites, "Arms", -22.0f, 2.0f, 76.0f, 0.9, 0.5, 1.0f, 1.0f);
            AddPreviewSprite(sprites, "Arms", 22.0f, 2.0f, -76.0f, 0.9, 0.5, 1.0f, -1.0f);
            AddPreviewSprite(sprites, "Head", 0.0f, -15.5f, 0.0f, 0.5, 0.5, 1.0f, 1.0f);
            AddPreviewSprite(sprites, "Face", 0.0f, -15.5f, 0.0f, 0.5, 0.5, 1.0f, 1.0f);

            if (sprites.Count == 0) return;
            RectangleF logicalBounds = GetPreviewBounds(sprites);
            const float padding = 18.0f;
            float availableWidth = Math.Max(1.0f, previewPanel.ClientSize.Width - padding * 2.0f);
            float availableHeight = Math.Max(1.0f, previewPanel.ClientSize.Height - padding * 2.0f);
            float commonScale = Math.Min(availableWidth / Math.Max(1.0f, logicalBounds.Width),
                availableHeight / Math.Max(1.0f, logicalBounds.Height));
            PointF origin = new PointF(
                previewPanel.ClientSize.Width * 0.5f -
                    (logicalBounds.Left + logicalBounds.Width * 0.5f) * commonScale,
                previewPanel.ClientSize.Height * 0.5f -
                    (logicalBounds.Top + logicalBounds.Height * 0.5f) * commonScale);

            for (int i = 0; i < sprites.Count; i++)
                DrawAtlasSprite(e.Graphics, sprites[i], origin, commonScale);
        }

        private void AddPreviewSprite(ICollection<PreviewSprite> sprites, string part,
            float x, float y, float rotation, double anchorX, double anchorY,
            float scaleX, float scaleY)
        {
            AtlasSprite sprite;
            if (!gameLoop.TryGetDmsPartPreview(part, out sprite)) return;
            sprites.Add(new PreviewSprite
            {
                Part = part,
                Sprite = sprite,
                Position = new PointF(x, y),
                Rotation = rotation,
                AnchorX = anchorX,
                AnchorY = anchorY,
                ScaleX = scaleX,
                ScaleY = scaleY
            });
        }

        private static RectangleF GetPreviewBounds(IList<PreviewSprite> sprites)
        {
            RectangleF result = RectangleF.Empty;
            for (int i = 0; i < sprites.Count; i++)
            {
                PreviewSprite preview = sprites[i];
                RectangleF local = preview.Sprite.Element.GetLocalRectangle(
                    preview.AnchorX, preview.AnchorY);
                double radians = preview.Rotation * Math.PI / 180.0;
                double cosine = Math.Cos(radians);
                double sine = Math.Sin(radians);
                PointF[] corners =
                {
                    new PointF(local.Left, local.Top), new PointF(local.Right, local.Top),
                    new PointF(local.Right, local.Bottom), new PointF(local.Left, local.Bottom)
                };
                for (int corner = 0; corner < corners.Length; corner++)
                {
                    double x = corners[corner].X * preview.ScaleX;
                    double y = corners[corner].Y * preview.ScaleY;
                    PointF point = new PointF((float)(preview.Position.X + x * cosine - y * sine),
                        (float)(preview.Position.Y + x * sine + y * cosine));
                    if (result.IsEmpty) result = new RectangleF(point.X, point.Y, 0.001f, 0.001f);
                    else result = RectangleF.Union(result, new RectangleF(point.X, point.Y, 0.001f, 0.001f));
                }
            }
            return result;
        }

        private void DrawAtlasSprite(System.Drawing.Graphics graphics, PreviewSprite preview,
            PointF origin, float commonScale)
        {
            AtlasSprite sprite = preview.Sprite;
            Rectangle source = sprite.Element.Frame;
            RectangleF destination = sprite.Element.GetLocalRectangle(
                preview.AnchorX, preview.AnchorY);
            Color tint = gameLoop.GetDmsPartPreviewTint(preview.Part);
            GraphicsState state = graphics.Save();
            try
            {
                graphics.TranslateTransform(origin.X + preview.Position.X * commonScale,
                    origin.Y + preview.Position.Y * commonScale);
                graphics.RotateTransform(preview.Rotation);
                graphics.ScaleTransform(commonScale * preview.ScaleX, commonScale * preview.ScaleY);
                PointF[] destinationPoints =
                {
                    new PointF(destination.Left, destination.Top),
                    new PointF(destination.Right, destination.Top),
                    new PointF(destination.Left, destination.Bottom)
                };
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix(new float[][]
                    {
                        new float[] { tint.R / 255.0f, 0, 0, 0, 0 },
                        new float[] { 0, tint.G / 255.0f, 0, 0, 0 },
                        new float[] { 0, 0, tint.B / 255.0f, 0, 0 },
                        new float[] { 0, 0, 0, 1, 0 },
                        new float[] { 0, 0, 0, 0, 1 }
                    }));
                    graphics.DrawImage(sprite.Atlas.Image, destinationPoints,
                        new RectangleF(source.X, source.Y, source.Width, source.Height),
                        GraphicsUnit.Pixel, attributes);
                }
            }
            finally { graphics.Restore(state); }
        }

        private static Button ActionButton(string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(85, 30) };
            button.Click += click;
            return button;
        }

        private void Changed(string message)
        { if (stateChanged != null) stateChanged(); previewPanel.Invalidate(); SetStatus(message); }
        private void SetStatus(string message) { statusLabel.Text = message ?? string.Empty; }
        private string CurrentCharacterName()
        { return gameLoop.SelectedSlugcat.DisplayName; }

        private static CharacterChoice[] BuildCharacterChoices()
        {
            CharacterChoice[] choices = new CharacterChoice[SlugcatProfiles.All.Count];
            for (int i = 0; i < choices.Length; i++)
            {
                SlugcatProfile profile = SlugcatProfiles.All[i];
                choices[i] = new CharacterChoice(
                    SlugcatProfiles.SelectionLabel(profile.Id), profile.Id);
            }
            return choices;
        }

        private static SlugcatId LegacyCharacterId(SlugcatVariant variant, SlugcatSkin skin)
        {
            switch (skin)
            {
                case SlugcatSkin.Artificer: return SlugcatId.Artificer;
                case SlugcatSkin.Spearmaster: return SlugcatId.SpearMaster;
                case SlugcatSkin.Rivulet: return SlugcatId.Rivulet;
                case SlugcatSkin.Saint: return SlugcatId.Saint;
            }
            switch (variant)
            {
                case SlugcatVariant.Monk: return SlugcatId.Yellow;
                case SlugcatVariant.Hunter: return SlugcatId.Red;
                case SlugcatVariant.Gourmand: return SlugcatId.Gourmand;
                default: return SlugcatId.White;
            }
        }

        private DmsSkinDefinition FindSet(string id)
        {
            if (string.Equals(id, "default", StringComparison.OrdinalIgnoreCase)) return null;
            for (int i = 0; i < gameLoop.DmsSkins.Count; i++)
                if (string.Equals(gameLoop.DmsSkins[i].Id, id,
                    StringComparison.OrdinalIgnoreCase)) return gameLoop.DmsSkins[i];
            return null;
        }

        private static string PartDisplayName(string part)
        {
            switch (part)
            {
                case "HEAD": return T("머리", "Head");
                case "FACE": return T("얼굴", "Face");
                case "BODY": return T("몸", "Body");
                case "ARMS": return T("팔", "Arms");
                case "HIPS": return T("엉덩이", "Hips");
                case "LEGS": return T("다리", "Legs");
                case "TAIL": return T("꼬리", "Tail");
                case "FACESCAR": return T("기술병 얼굴 흉터", "Artificer Face Scar");
                case "GILLS": return T("물살이 아가미", "Rivulet Gills");
                case "TAILSPECKLES": return T("창술가 꼬리 무늬", "Spearmaster Tail Speckles");
                case "ASCENSION": return T("성자 승천", "Saint Ascension");
                case "PIXEL": return T("표식 / 픽셀", "The Mark / Pixel");
                default: return part;
            }
        }

        private static string NormalizePresetPart(string part)
        {
            string compact = (part ?? string.Empty).Trim().Replace(" ", string.Empty)
                .Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
            if (compact == "THEMARK" || compact == "MARK") return "PIXEL";
            if (compact == "FACESCAR") return "FACESCAR";
            if (compact == "TAILSPECKLES") return "TAILSPECKLES";
            return compact;
        }

        private static int FindChoice(ComboBox selector, string id)
        {
            for (int i = 0; i < selector.Items.Count; i++)
            {
                SpriteChoice choice = selector.Items[i] as SpriteChoice;
                if (choice != null && string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private static Color ReadableText(Color background)
        {
            double luminance = background.R * 0.299 + background.G * 0.587 + background.B * 0.114;
            return luminance > 150.0 ? Color.Black : Color.White;
        }

        private static string T(string korean, string english)
        { return UiLocalization.Text(korean, english); }

        private sealed class SpriteChoice
        {
            public SpriteChoice(string name, string id, DmsSkinDefinition set) { Name = name; Id = id; Set = set; }
            public readonly string Name;
            public readonly string Id;
            public readonly DmsSkinDefinition Set;
            public override string ToString() { return Name; }
        }

        private sealed class PreviewSprite
        {
            public string Part;
            public AtlasSprite Sprite;
            public PointF Position;
            public float Rotation;
            public double AnchorX;
            public double AnchorY;
            public float ScaleX;
            public float ScaleY;
        }

        private sealed class CharacterChoice
        {
            public CharacterChoice(string name, SlugcatId id)
            { Name = name; Id = id; }
            public readonly string Name;
            public readonly SlugcatId Id;
            public override string ToString() { return Name; }
        }
    }
}
