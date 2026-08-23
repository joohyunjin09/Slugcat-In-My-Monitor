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

namespace RainWorldDesktopPet.UI
{
    public sealed class SkinEditorWindow : Form
    {
        private static readonly string[] PartNames =
        { "Head", "Face", "Body", "Arms", "Hips", "Legs", "Tail", "The Mark" };

        private static readonly CharacterChoice[] Characters =
        {
            new CharacterChoice("Survivor", SlugcatVariant.Survivor, SlugcatSkin.Default),
            new CharacterChoice("Monk", SlugcatVariant.Monk, SlugcatSkin.Default),
            new CharacterChoice("Hunter", SlugcatVariant.Hunter, SlugcatSkin.Default),
            new CharacterChoice("Gourmand", SlugcatVariant.Gourmand, SlugcatSkin.Default),
            new CharacterChoice("Artificer", SlugcatVariant.Survivor, SlugcatSkin.Artificer),
            new CharacterChoice("Spearmaster", SlugcatVariant.Survivor, SlugcatSkin.Spearmaster),
            new CharacterChoice("Rivulet", SlugcatVariant.Survivor, SlugcatSkin.Rivulet),
            new CharacterChoice("Saint", SlugcatVariant.Survivor, SlugcatSkin.Saint)
        };

        private readonly GameLoop gameLoop;
        private readonly Action stateChanged;
        private readonly DmsSpriteCatalog catalog;
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
        private readonly CheckBox entireSetCheck;
        private bool updatingControls;

        public SkinEditorWindow(GameLoop gameLoop, Action stateChanged)
        {
            if (gameLoop == null) throw new ArgumentNullException("gameLoop");
            this.gameLoop = gameLoop;
            this.stateChanged = stateChanged;
            catalog = new DmsSpriteCatalog(gameLoop.Installation);
            for (int i = 0; i < PartNames.Length; i++) partSelections[PartNames[i]] = "default";

            Text = "Slugcat Appearance Editor";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            MinimumSize = new Size(920, 600);
            ClientSize = new Size(1120, 700);
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10),
                ColumnCount = 3, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(root);

            GroupBox characterGroup = new GroupBox { Text = "Slugcat", Dock = DockStyle.Fill };
            characterList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            for (int i = 0; i < Characters.Length; i++) characterList.Items.Add(Characters[i]);
            characterList.SelectedIndexChanged += CharacterChanged;
            characterGroup.Controls.Add(characterList);
            root.Controls.Add(characterGroup, 0, 0);

            GroupBox partsGroup = new GroupBox { Text = "Appearance parts", Dock = DockStyle.Fill };
            TableLayoutPanel parts = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8),
                ColumnCount = 3, RowCount = PartNames.Length + 1 };
            parts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            parts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            parts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            for (int i = 0; i < PartNames.Length; i++)
            {
                string part = PartNames[i];
                parts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                Label label = new Label { Text = part, Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft };
                ComboBox selector = new ComboBox { Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList, Tag = part };
                selector.SelectedIndexChanged += PartSelectionChanged;
                Button color = new Button { Text = "Color...", Dock = DockStyle.Fill, Tag = part,
                    UseVisualStyleBackColor = false };
                color.Click += CustomizeColor;
                partSelectors[part] = selector;
                colorButtons[part] = color;
                parts.Controls.Add(label, 0, i);
                parts.Controls.Add(selector, 1, i);
                parts.Controls.Add(color, 2, i);
            }
            entireSetCheck = new CheckBox { Text = "Apply the selected set to every available part",
                AutoSize = true, Dock = DockStyle.Fill };
            parts.SetColumnSpan(entireSetCheck, 3);
            parts.Controls.Add(entireSetCheck, 0, PartNames.Length);
            partsGroup.Controls.Add(parts);
            root.Controls.Add(partsGroup, 1, 0);

            GroupBox previewGroup = new GroupBox { Text = "Preview", Dock = DockStyle.Fill };
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
            root.Controls.Add(previewGroup, 2, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Padding = new Padding(0, 7, 0, 0) };
            actions.Controls.Add(ActionButton("Close", delegate { Close(); }));
            actions.Controls.Add(ActionButton("Reload sprites", ReloadCatalog));
            actions.Controls.Add(ActionButton("Load preset...", LoadPreset));
            actions.Controls.Add(ActionButton("Save preset...", SavePreset));
            actions.Controls.Add(ActionButton("Paste", PasteSetup));
            actions.Controls.Add(ActionButton("Copy", CopySetup));
            actions.Controls.Add(ActionButton("Reset", ResetAll));
            root.SetColumnSpan(actions, 3);
            root.Controls.Add(actions, 0, 1);

            StatusStrip status = new StatusStrip();
            statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.Add(statusLabel);
            Controls.Add(status);

            PopulateSpriteSelectors();
            RefreshFromGame();
            SetStatus(catalog.Status);
        }

        public void RefreshFromGame()
        {
            updatingControls = true;
            try
            {
                string current = CurrentCharacterName();
                for (int i = 0; i < Characters.Length; i++)
                    if (Characters[i].Name == current) characterList.SelectedIndex = i;
                for (int i = 0; i < PartNames.Length; i++)
                {
                    string part = PartNames[i];
                    Color color = gameLoop.GetPartColor(part);
                    colorButtons[part].BackColor = color;
                    colorButtons[part].ForeColor = ReadableText(color);
                }
                assetLabel.Text = gameLoop.AssetStatus;
            }
            finally { updatingControls = false; }
            previewPanel.Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F2)
            { Close(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) catalog.Dispose();
            base.Dispose(disposing);
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
                    selector.Items.Add(new SpriteChoice("Default", "default", null));
                    for (int j = 0; j < catalog.Sets.Count; j++)
                    {
                        DmsSpriteSet set = catalog.Sets[j];
                        string image;
                        string metadata;
                        if (set.TryGetPartFiles(part, out image, out metadata))
                            selector.Items.Add(new SpriteChoice(set.Name, set.Id, set));
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
            string reason;
            if (!gameLoop.CanUseSkin(choice.Skin, out reason))
            { SetStatus(reason ?? "The selected skin is unavailable."); RefreshFromGame(); return; }
            gameLoop.SetVariant(choice.Variant);
            gameLoop.SetSkin(choice.Skin);
            Changed(choice.Name + " applied.");
        }

        private void PartSelectionChanged(object sender, EventArgs e)
        {
            if (updatingControls) return;
            ComboBox selector = sender as ComboBox;
            SpriteChoice choice = selector == null ? null : selector.SelectedItem as SpriteChoice;
            string part = selector == null ? null : selector.Tag as string;
            if (choice == null || part == null) return;
            if (entireSetCheck.Checked && choice.Set != null)
            {
                for (int i = 0; i < PartNames.Length; i++) ApplySpriteChoice(PartNames[i], choice.Set);
                PopulateSpriteSelectors();
                Changed(choice.Name + " applied to every available part.");
                return;
            }
            ApplySpriteChoice(part, choice.Set);
            Changed(part + " sprite changed to " + choice.Name + ".");
        }

        private void ApplySpriteChoice(string part, DmsSpriteSet set)
        {
            if (set == null)
            { gameLoop.ClearPartAtlas(part); partSelections[part] = "default"; return; }
            string image;
            string metadata;
            if (!set.TryGetPartFiles(part, out image, out metadata)) return;
            string reason;
            if (!gameLoop.SetPartAtlas(part, image, metadata, out reason))
            { SetStatus("Could not load " + set.Name + ": " + reason); return; }
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
                Changed(part + " color changed.");
            }
        }

        private void ReloadCatalog(object sender, EventArgs e)
        { catalog.Reload(); PopulateSpriteSelectors(); SetStatus(catalog.Status); }

        private void ResetAll(object sender, EventArgs e)
        {
            gameLoop.SetVariant(SlugcatVariant.Survivor);
            gameLoop.SetSkin(SlugcatSkin.Default);
            gameLoop.ClearPartColors();
            for (int i = 0; i < PartNames.Length; i++)
            { gameLoop.ClearPartAtlas(PartNames[i]); partSelections[PartNames[i]] = "default"; }
            PopulateSpriteSelectors();
            RefreshFromGame();
            Changed("Appearance reset.");
        }

        private void CopySetup(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(BuildAppearanceData());
                SetStatus("Appearance copied to the clipboard.");
            }
            catch (Exception exception) { SetStatus("Copy failed: " + exception.Message); }
        }

        private void PasteSetup(object sender, EventArgs e)
        {
            try
            {
                ApplyAppearanceData(Clipboard.GetText(), "Appearance pasted from the clipboard.");
            }
            catch (Exception exception) { SetStatus("Paste failed: " + exception.Message); }
        }

        private void SavePreset(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(PresetDirectory);
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "Save Slugcat appearance preset";
                    dialog.InitialDirectory = PresetDirectory;
                    dialog.Filter = "Slugcat appearance preset (*.simmskin)|*.simmskin|All files (*.*)|*.*";
                    dialog.DefaultExt = "simmskin";
                    dialog.AddExtension = true;
                    dialog.OverwritePrompt = true;
                    dialog.FileName = CurrentCharacterName() + ".simmskin";
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(dialog.FileName, BuildAppearanceData(), Encoding.UTF8);
                    SetStatus("Preset saved: " + Path.GetFileName(dialog.FileName));
                }
            }
            catch (Exception exception) { SetStatus("Preset save failed: " + exception.Message); }
        }

        private void LoadPreset(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(PresetDirectory);
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Load Slugcat appearance preset";
                    dialog.InitialDirectory = PresetDirectory;
                    dialog.Filter = "Slugcat appearance preset (*.simmskin)|*.simmskin|All files (*.*)|*.*";
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    ApplyAppearanceData(File.ReadAllText(dialog.FileName, Encoding.UTF8),
                        "Preset loaded: " + Path.GetFileName(dialog.FileName));
                }
            }
            catch (Exception exception) { SetStatus("Preset load failed: " + exception.Message); }
        }

        private string BuildAppearanceData()
        {
            StringBuilder value = new StringBuilder("SIMM_SKIN_V2|");
            value.Append(gameLoop.Appearance.Variant).Append('|').Append(gameLoop.Skin);
            for (int i = 0; i < PartNames.Length; i++)
            {
                string part = PartNames[i];
                value.Append('|').Append(part).Append('=').Append(partSelections[part]);
                value.Append(',').Append(gameLoop.GetPartColor(part).ToArgb().ToString("X8"));
            }
            return value.ToString();
        }

        private void ApplyAppearanceData(string value, string successMessage)
        {
            string[] fields = (value ?? string.Empty).Trim().Split('|');
            if (fields.Length < 3 || fields[0] != "SIMM_SKIN_V2")
                throw new InvalidOperationException("The file is not a Slugcat appearance preset.");

            SlugcatVariant variant;
            SlugcatSkin skin;
            if (!Enum.TryParse(fields[1], true, out variant) || !Enum.TryParse(fields[2], true, out skin))
                throw new InvalidOperationException("The preset character data is invalid.");
            string reason;
            if (!gameLoop.CanUseSkin(skin, out reason)) throw new InvalidOperationException(reason);

            Dictionary<string, DmsSpriteSet> sets =
                new Dictionary<string, DmsSpriteSet>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Color> colors =
                new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            for (int i = 3; i < fields.Length; i++)
            {
                int equals = fields[i].IndexOf('=');
                int comma = fields[i].LastIndexOf(',');
                if (equals <= 0 || comma <= equals) continue;
                string part = fields[i].Substring(0, equals);
                if (!partSelectors.ContainsKey(part)) continue;
                string id = fields[i].Substring(equals + 1, comma - equals - 1);
                DmsSpriteSet set = FindSet(id);
                if (!string.Equals(id, "default", StringComparison.OrdinalIgnoreCase))
                {
                    if (set == null)
                        throw new InvalidOperationException("Required sprite set is not installed: " + id);
                    string image;
                    string metadata;
                    if (!set.TryGetPartFiles(part, out image, out metadata))
                        throw new InvalidOperationException(set.Name + " does not contain " + part + ".");
                }
                sets[part] = set;
                uint argb;
                if (!uint.TryParse(fields[i].Substring(comma + 1),
                    System.Globalization.NumberStyles.HexNumber, null, out argb))
                    throw new InvalidOperationException("Invalid color for " + part + ".");
                colors[part] = Color.FromArgb(unchecked((int)argb));
            }

            gameLoop.SetVariant(variant);
            gameLoop.SetSkin(skin);
            for (int i = 0; i < PartNames.Length; i++)
            {
                string part = PartNames[i];
                DmsSpriteSet set;
                if (sets.TryGetValue(part, out set)) ApplySpriteChoice(part, set);
                Color color;
                if (colors.TryGetValue(part, out color)) gameLoop.SetPartColor(part, color);
            }
            PopulateSpriteSelectors();
            RefreshFromGame();
            Changed(successMessage);
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
            if (!gameLoop.TryGetAtlasSprite(DmsSpriteCatalog.GetPreviewElement(part), false, out sprite)) return;
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
            Color tint = gameLoop.GetPartColor(preview.Part);
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
        { return gameLoop.Skin == SlugcatSkin.Default ? gameLoop.Appearance.Variant.ToString() : gameLoop.Skin.ToString(); }

        private DmsSpriteSet FindSet(string id)
        {
            if (string.Equals(id, "default", StringComparison.OrdinalIgnoreCase)) return null;
            for (int i = 0; i < catalog.Sets.Count; i++)
                if (string.Equals(catalog.Sets[i].Id, id, StringComparison.OrdinalIgnoreCase)) return catalog.Sets[i];
            return null;
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

        private sealed class SpriteChoice
        {
            public SpriteChoice(string name, string id, DmsSpriteSet set) { Name = name; Id = id; Set = set; }
            public readonly string Name;
            public readonly string Id;
            public readonly DmsSpriteSet Set;
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
            public CharacterChoice(string name, SlugcatVariant variant, SlugcatSkin skin)
            { Name = name; Variant = variant; Skin = skin; }
            public readonly string Name;
            public readonly SlugcatVariant Variant;
            public readonly SlugcatSkin Skin;
            public override string ToString() { return Name; }
        }
    }
}
