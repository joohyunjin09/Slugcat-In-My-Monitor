using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Workshop
{
    public enum DmsSpriteSide
    {
        None,
        Left,
        Right
    }

    public sealed class DmsTailDefaults
    {
        public float Length;
        public float Wideness;
        public float Roundness;
        public float Lift;
        public Color Color = Color.Empty;
        public bool HasCustomShape { get { return Length > 0f || Wideness > 0f || Roundness > 0f; } }
    }

    public sealed class DmsSkinDefinition : IDisposable
    {
        private readonly List<RainWorldAtlas> atlases = new List<RainWorldAtlas>();
        private readonly Dictionary<string, AtlasSprite> elements =
            new Dictionary<string, AtlasSprite>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AtlasSprite> leftElements =
            new Dictionary<string, AtlasSprite>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AtlasSprite> rightElements =
            new Dictionary<string, AtlasSprite>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> availableParts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> asymmetricParts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string ModId;
        public string ModName;
        public string WorkshopId;
        public string Id;
        public string Name;
        public string Author;
        public string DirectoryPath;
        public bool IsModActive;
        public long SourceFingerprint;
        public readonly Dictionary<string, Color> DefaultColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        public readonly DmsTailDefaults DefaultTail = new DmsTailDefaults();

        public IEnumerable<string> AvailableParts { get { return availableParts.OrderBy(value => value); } }

        internal void AddAtlas(RainWorldAtlas atlas)
        {
            atlases.Add(atlas);
            foreach (KeyValuePair<string, AtlasElement> item in atlas.Elements)
            {
                string name = item.Key;
                if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                AtlasSprite sprite = new AtlasSprite { Atlas = atlas, Element = item.Value };
                if (name.StartsWith("Left", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
                    leftElements[name.Substring(4)] = sprite;
                else if (name.StartsWith("Right", StringComparison.OrdinalIgnoreCase) && name.Length > 5)
                    rightElements[name.Substring(5)] = sprite;
                else
                    elements[name] = sprite;
            }
        }

        internal void ValidateAvailableParts()
        {
            foreach (KeyValuePair<string, string[]> group in DmsSpriteGroups.Required)
            {
                if (group.Value.All(name => elements.ContainsKey(name))) availableParts.Add(group.Key);
                if (group.Value.All(name => leftElements.ContainsKey(name) && rightElements.ContainsKey(name)))
                {
                    availableParts.Add(group.Key);
                    asymmetricParts.Add(group.Key);
                }
            }
        }

        public bool TryGetSprite(string originalElement, string slugcatId, DmsSpriteSide side,
            out AtlasSprite sprite)
        {
            sprite = null;
            string generic = DmsSpriteGroups.ToGenericElement(originalElement, slugcatId);
            string part = DmsSpriteGroups.PartForElement(generic);
            if (part == null || !availableParts.Contains(part)) return false;
            if (asymmetricParts.Contains(part) && side == DmsSpriteSide.Left &&
                leftElements.TryGetValue(generic, out sprite)) return true;
            if (asymmetricParts.Contains(part) && side == DmsSpriteSide.Right &&
                rightElements.TryGetValue(generic, out sprite)) return true;
            return elements.TryGetValue(generic, out sprite);
        }

        public Color ResolveTint(string originalElement, string slugcatId, Color fallback)
        {
            string part = DmsSpriteGroups.PartForElement(
                DmsSpriteGroups.ToGenericElement(originalElement, slugcatId));
            Color color;
            return part != null && DefaultColors.TryGetValue(part, out color) && color.A > 0
                ? color
                : fallback;
        }

        public Bitmap CreatePreview(int size)
        {
            AtlasSprite sprite;
            if (!TryPreview("HeadA0", out sprite) && !TryPreview("BodyA", out sprite) &&
                !elements.Values.Any()) return null;
            if (sprite == null) sprite = elements.Values.First();
            Bitmap preview = new Bitmap(size, size);
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(preview))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                AtlasElement element = sprite.Element;
                float scale = Math.Min((size - 4f) / element.Frame.Width,
                    (size - 4f) / element.Frame.Height);
                float width = element.Frame.Width * scale;
                float height = element.Frame.Height * scale;
                RectangleF destination = new RectangleF((size - width) * 0.5f,
                    (size - height) * 0.5f, width, height);
                graphics.DrawImage(sprite.Atlas.Image, destination, element.Frame, GraphicsUnit.Pixel);
            }
            return preview;
        }

        private bool TryPreview(string name, out AtlasSprite sprite)
        {
            return elements.TryGetValue(name, out sprite) || leftElements.TryGetValue(name, out sprite) ||
                   rightElements.TryGetValue(name, out sprite);
        }

        public void Dispose()
        {
            foreach (RainWorldAtlas atlas in atlases) atlas.Dispose();
            atlases.Clear();
            elements.Clear();
            leftElements.Clear();
            rightElements.Clear();
            asymmetricParts.Clear();
        }
    }

    internal static class DmsSpriteGroups
    {
        public static readonly Dictionary<string, string[]> Required = BuildRequired();

        private static Dictionary<string, string[]> BuildRequired()
        {
            Dictionary<string, string[]> result = new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase);
            result["HEAD"] = Enumerable.Range(0, 18).Select(index => "HeadA" + index).ToArray();
            result["FACE"] = Enumerable.Range(0, 9).Select(index => "FaceA" + index)
                .Concat(Enumerable.Range(0, 9).Select(index => "FaceB" + index))
                .Concat(new[] { "FaceDead", "FaceStunned" }).ToArray();
            result["BODY"] = new[] { "BodyA" };
            result["ARMS"] = Enumerable.Range(0, 13).Select(index => "PlayerArm" + index)
                .Concat(new[] { "OnTopOfTerrainHand", "OnTopOfTerrainHand2" }).ToArray();
            result["HIPS"] = new[] { "HipsA" };
            result["LEGS"] = Enumerable.Range(0, 7).Select(index => "LegsA" + index)
                .Concat(new[] { "LegsAAir0", "LegsAAir1" })
                .Concat(Enumerable.Range(0, 7).Select(index => "LegsAClimbing" + index))
                .Concat(Enumerable.Range(0, 6).Select(index => "LegsACrawling" + index))
                .Concat(Enumerable.Range(0, 7).Select(index => "LegsAOnPole" + index))
                .Concat(new[] { "LegsAPole", "LegsAVerticalPole", "LegsAWall" }).ToArray();
            result["TAIL"] = new[] { "TailTexture" };
            result["FACESCAR"] = new[] { "MushroomA" };
            result["GILLS"] = new[] { "LizardScaleA3", "LizardScaleB3" };
            result["TAILSPECKLES"] = new[] { "tinyStar" };
            result["ASCENSION"] = new[] { "guardEye", "WormEye" };
            result["PIXEL"] = new[] { "pixel" };
            return result;
        }

        public static string ToGenericElement(string element, string slugcatId)
        {
            if (string.IsNullOrEmpty(element)) return element;
            if (string.Equals(slugcatId, "Saint", StringComparison.OrdinalIgnoreCase) &&
                element.StartsWith("HeadB", StringComparison.OrdinalIgnoreCase))
                return "HeadA" + element.Substring(5);
            if (string.Equals(slugcatId, "Artificer", StringComparison.OrdinalIgnoreCase))
            {
                if (element.StartsWith("FaceC", StringComparison.OrdinalIgnoreCase))
                    return "FaceA" + element.Substring(5);
                if (element.StartsWith("FaceD", StringComparison.OrdinalIgnoreCase))
                    return "FaceB" + element.Substring(5);
            }
            return element;
        }

        public static string PartForElement(string element)
        {
            if (string.IsNullOrEmpty(element)) return null;
            foreach (KeyValuePair<string, string[]> group in Required)
                if (group.Value.Contains(element, StringComparer.OrdinalIgnoreCase)) return group.Key;
            return null;
        }
    }

    public sealed class DmsSkinCatalog : IDisposable
    {
        private readonly WorkshopLog log;

        public DmsSkinCatalog(WorkshopCatalog workshop, WorkshopLog log)
        {
            if (workshop == null) throw new ArgumentNullException("workshop");
            this.log = log ?? new WorkshopLog(false);
            Skins = new List<DmsSkinDefinition>();
            RainWorldMod framework = workshop.FindById("dressmyslugcat");
            IsFrameworkInstalled = framework != null;
            IsFrameworkActive = framework != null && framework.IsActive;
            if (!IsFrameworkInstalled)
            {
                this.log.Info("DMS", "Dress My Slugcat was not detected; skin integration is disabled.");
                return;
            }

            this.log.Info("DMS", "Dress My Slugcat detected at " + framework.RootPath +
                (framework.IsActive ? " [active]" : " [inactive]"));
            foreach (RainWorldMod mod in workshop.InLoadOrder(true)) ScanMod(mod);
            this.log.Info("DMS", "Registered " + Skins.Count + " DMS spritesheets from installed mods.");
        }

        public bool IsFrameworkInstalled { get; private set; }
        public bool IsFrameworkActive { get; private set; }
        public IList<DmsSkinDefinition> Skins { get; private set; }

        public DmsSkinDefinition Find(string id)
        {
            return Skins.FirstOrDefault(skin => string.Equals(skin.Id, id,
                StringComparison.OrdinalIgnoreCase));
        }

        private void ScanMod(RainWorldMod mod)
        {
            string dmsRoot = Path.Combine(mod.RootPath, "dressmyslugcat");
            if (!Directory.Exists(dmsRoot)) return;
            log.Info("DMS", "Compatible skin mod detected: " + mod.Name + " at " + dmsRoot +
                (mod.IsActive ? " [active]" : " [inactive]"));
            string[] metadataFiles;
            try { metadataFiles = Directory.GetFiles(dmsRoot, "metadata.json", SearchOption.AllDirectories); }
            catch (Exception exception)
            {
                log.Warning("DMS", "Could not enumerate " + dmsRoot + ": " + exception.Message);
                return;
            }

            foreach (string metadata in metadataFiles)
            {
                try
                {
                    DmsSkinDefinition skin = LoadSkin(mod, metadata);
                    if (skin == null) continue;
                    if (Skins.Any(existing => string.Equals(existing.Id, skin.Id,
                        StringComparison.OrdinalIgnoreCase)))
                    {
                        log.Warning("DMS", "Duplicate spritesheet ID skipped: " + skin.Id);
                        skin.Dispose();
                        continue;
                    }
                    Skins.Add(skin);
                    log.Verbose("DMS", "Skin registered: " + skin.Name + " (" + skin.Id + ") by " +
                        skin.Author + "; sprites: " + string.Join(", ", skin.AvailableParts));
                }
                catch (Exception exception)
                {
                    log.Warning("DMS", "Skipping broken skin at " + Path.GetDirectoryName(metadata) +
                        ": " + exception.Message);
                }
            }
        }

        private DmsSkinDefinition LoadSkin(RainWorldMod mod, string metadataPath)
        {
            string json = File.ReadAllText(metadataPath);
            DmsSkinDefinition skin = new DmsSkinDefinition();
            skin.Id = WorkshopCatalog.ExtractJsonString(json, "id");
            skin.Name = WorkshopCatalog.ExtractJsonString(json, "name");
            skin.Author = WorkshopCatalog.ExtractJsonString(json, "author");
            if (string.IsNullOrWhiteSpace(skin.Id) || string.IsNullOrWhiteSpace(skin.Name) ||
                string.IsNullOrWhiteSpace(skin.Author))
            {
                skin.Dispose();
                throw new InvalidDataException("metadata.json is missing id, name, or author.");
            }

            skin.ModId = mod.Id;
            skin.ModName = mod.Name;
            skin.WorkshopId = mod.WorkshopId;
            skin.DirectoryPath = Path.GetDirectoryName(metadataPath);
            skin.IsModActive = mod.IsActive;
            skin.SourceFingerprint = mod.SourceFingerprint;
            ParseDefaults(json, skin);

            string[] pngFiles = Directory.GetFiles(skin.DirectoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetExtension(path).Equals(".png",
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (string png in pngFiles)
            {
                string text = Path.ChangeExtension(png, ".txt");
                if (!File.Exists(text))
                {
                    log.Warning("DMS", "Missing optional atlas descriptor: " + text);
                    continue;
                }
                try
                {
                    skin.AddAtlas(RainWorldAtlasLoader.Load(png, text));
                }
                catch (Exception exception)
                {
                    log.Warning("DMS", "Invalid atlas pair skipped: " + png + " (" +
                        exception.Message + ")");
                }
            }
            skin.ValidateAvailableParts();
            if (!skin.AvailableParts.Any())
            {
                skin.Dispose();
                return null;
            }
            return skin;
        }

        private static void ParseDefaults(string json, DmsSkinDefinition skin)
        {
            foreach (string part in DmsSpriteGroups.Required.Keys)
            {
                Match block = Regex.Match(json, "[\\\"]" + Regex.Escape(part) +
                    "[\\\"]\\s*:\\s*\\{(?<body>.*?)\\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (!block.Success) continue;
                string body = block.Groups["body"].Value;
                string colorText = WorkshopCatalog.ExtractJsonString("{" + body + "}", "color");
                Color color;
                if (TryReadColor(colorText, out color))
                {
                    if (part.Equals("TAIL", StringComparison.OrdinalIgnoreCase)) skin.DefaultTail.Color = color;
                    else skin.DefaultColors[part] = color;
                }
                if (part.Equals("TAIL", StringComparison.OrdinalIgnoreCase))
                {
                    skin.DefaultTail.Length = ReadNumber(body, "length");
                    skin.DefaultTail.Wideness = ReadNumber(body, "wideness");
                    skin.DefaultTail.Roundness = ReadNumber(body, "roundness");
                    skin.DefaultTail.Lift = ReadNumber(body, "lift");
                }
            }
        }

        private static float ReadNumber(string body, string key)
        {
            Match match = Regex.Match(body, "[\\\"]" + Regex.Escape(key) +
                "[\\\"]\\s*:\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?)", RegexOptions.IgnoreCase);
            float value;
            return match.Success && float.TryParse(match.Groups["value"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

        private static bool TryReadColor(string value, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                string hex = value.Trim().TrimStart('#');
                if (hex.Length != 6 && hex.Length != 8) return false;
                int alpha = hex.Length == 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) : 255;
                color = Color.FromArgb(alpha, Convert.ToInt32(hex.Substring(0, 2), 16),
                    Convert.ToInt32(hex.Substring(2, 2), 16),
                    Convert.ToInt32(hex.Substring(4, 2), 16));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            foreach (DmsSkinDefinition skin in Skins) skin.Dispose();
            Skins.Clear();
        }
    }
}
