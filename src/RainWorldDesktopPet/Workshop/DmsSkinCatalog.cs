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

    internal sealed class SharedDmsAtlasLease : IDisposable
    {
        private string key;

        internal SharedDmsAtlasLease(string key, RainWorldAtlas atlas)
        {
            this.key = key;
            Atlas = atlas;
        }

        public RainWorldAtlas Atlas { get; private set; }

        public void Dispose()
        {
            RainWorldAtlas atlas = Atlas;
            if (atlas == null) return;
            Atlas = null;
            string releaseKey = key;
            key = null;
            SharedDmsAtlasCache.Release(releaseKey, atlas);
        }
    }

    internal static class SharedDmsAtlasCache
    {
        private sealed class Entry
        {
            public RainWorldAtlas Atlas;
            public int References;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static SharedDmsAtlasLease Acquire(string imagePath, string metadataPath)
        {
            string key = BuildKey(imagePath, metadataPath);
            lock (Sync)
            {
                Entry entry;
                if (Entries.TryGetValue(key, out entry))
                {
                    entry.References++;
                    return new SharedDmsAtlasLease(key, entry.Atlas);
                }

                RainWorldAtlas atlas = RainWorldAtlasLoader.Load(imagePath, metadataPath);
                entry = new Entry { Atlas = atlas, References = 1 };
                Entries[key] = entry;
                return new SharedDmsAtlasLease(key, atlas);
            }
        }

        internal static void Release(string key, RainWorldAtlas atlas)
        {
            RainWorldAtlas release = null;
            if (string.IsNullOrEmpty(key) || atlas == null) return;
            lock (Sync)
            {
                Entry entry;
                if (!Entries.TryGetValue(key, out entry) ||
                    !ReferenceEquals(entry.Atlas, atlas)) return;
                entry.References--;
                if (entry.References > 0) return;
                Entries.Remove(key);
                release = entry.Atlas;
            }
            if (release != null) release.Dispose();
        }

        private static string BuildKey(string imagePath, string metadataPath)
        {
            string image = Path.GetFullPath(imagePath);
            string metadata = Path.GetFullPath(metadataPath);
            FileInfo imageInfo = new FileInfo(image);
            FileInfo metadataInfo = new FileInfo(metadata);
            return image + "|" + imageInfo.Length + "|" + imageInfo.LastWriteTimeUtc.Ticks +
                "|" + metadata + "|" + metadataInfo.Length + "|" +
                metadataInfo.LastWriteTimeUtc.Ticks;
        }
    }

    public sealed class DmsSkinDefinition : IDisposable
    {
        private readonly List<SharedDmsAtlasLease> atlasLeases =
            new List<SharedDmsAtlasLease>();
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
        private readonly Dictionary<string, bool> authoredColorCache =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

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

        public bool HasPart(string part)
        {
            return !string.IsNullOrWhiteSpace(part) && availableParts.Contains(part);
        }

        internal void AddAtlas(SharedDmsAtlasLease lease)
        {
            if (lease == null || lease.Atlas == null) throw new ArgumentNullException("lease");
            atlasLeases.Add(lease);
            RainWorldAtlas atlas = lease.Atlas;
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

        // DMS owns the visible source palette. Authored PNG colours and
        // non-white metadata colours therefore win over the editor colour and
        // the selected Slugcat's default. Pure white is a neutral DMS mask,
        // so it inherits fallback like a greyscale sheet.
        public Color ResolveTint(string originalElement, string slugcatId, Color fallback,
            bool hasCustomColor)
        {
            return ResolveTint(null, originalElement, slugcatId, fallback, hasCustomColor);
        }

        public Color ResolveTint(AtlasSprite sprite, string originalElement, string slugcatId,
            Color fallback, bool hasCustomColor)
        {
            string part = DmsSpriteGroups.PartForElement(
                DmsSpriteGroups.ToGenericElement(originalElement, slugcatId));
            Color color;
            if (part != null && DefaultColors.TryGetValue(part, out color) &&
                color.A > 0 && !IsNeutralWhite(color))
                return color;
            // A colour-authored DMS PNG keeps its own palette. The fallback
            // already contains an explicit user colour when one exists, then
            // the selected Slugcat's normal colour for monochrome art.
            return sprite == null || !HasAuthoredColor(sprite) ? fallback : Color.White;
        }

        internal bool HasAuthoredColor(AtlasSprite sprite)
        {
            if (sprite == null || sprite.Atlas == null || sprite.Atlas.Image == null ||
                sprite.Element == null) return false;
            string key = sprite.Atlas.ImagePath + "|" + sprite.Element.Frame.X + "," +
                sprite.Element.Frame.Y + "," + sprite.Element.Frame.Width + "," +
                sprite.Element.Frame.Height;
            bool result;
            if (authoredColorCache.TryGetValue(key, out result)) return result;
            result = HasAuthoredColor(sprite.Atlas.Image, sprite.Element.Frame);
            authoredColorCache[key] = result;
            return result;
        }

        internal static bool HasAuthoredColor(Bitmap bitmap, Rectangle frame)
        {
            if (bitmap == null || frame.Width < 1 || frame.Height < 1) return false;
            Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            frame.Intersect(bounds);
            for (int y = frame.Top; y < frame.Bottom; y++)
            {
                for (int x = frame.Left; x < frame.Right; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.A < 16) continue;
                    int minimum = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                    int maximum = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                    // Keep near-neutral antialiasing pixels from falsely
                    // turning a monochrome DMS sheet into an authored palette.
                    if (maximum - minimum > 12) return true;
                }
            }
            return false;
        }

        internal static bool IsNeutralWhite(Color color)
        {
            return color.R == 255 && color.G == 255 && color.B == 255;
        }

        // TailTexture is a deforming mesh rather than a normal FSprite, but
        // follows the same priority as normal DMS elements.
        public Color ResolveTailTint(AtlasSprite sprite, Color fallback, bool hasCustomColor)
        {
            if (DefaultTail.Color.A > 0 && !IsNeutralWhite(DefaultTail.Color)) return DefaultTail.Color;
            return sprite == null || !HasAuthoredColor(sprite) ? fallback : Color.White;
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
            for (int i = 0; i < atlasLeases.Count; i++) atlasLeases[i].Dispose();
            atlasLeases.Clear();
            elements.Clear();
            leftElements.Clear();
            rightElements.Clear();
            availableParts.Clear();
            asymmetricParts.Clear();
            authoredColorCache.Clear();
        }
    }

    public static class DmsSpriteGroups
    {
        public static readonly Dictionary<string, string[]> Required = BuildRequired();

        public static readonly string[] SelectableParts =
        {
            "HEAD", "FACE", "BODY", "ARMS", "HIPS", "LEGS", "TAIL",
            "FACESCAR", "GILLS", "TAILSPECKLES", "ASCENSION", "PIXEL"
        };

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
            // Dress My Slugcat supplies the normal HeadA/FaceA/FaceB sheets,
            // then SpriteDefinitions.Init aliases those sheets to the concrete
            // PlayerGraphics Slugpup names. A skin must therefore never need
            // duplicate HeadC or PFace sprite files.
            if (string.Equals(slugcatId, "Slugpup", StringComparison.OrdinalIgnoreCase))
            {
                if (element.StartsWith("HeadC", StringComparison.OrdinalIgnoreCase))
                    return "HeadA" + element.Substring(5);
                if (element.StartsWith("PFaceA", StringComparison.OrdinalIgnoreCase))
                    return "FaceA" + element.Substring(6);
                if (element.StartsWith("PFaceB", StringComparison.OrdinalIgnoreCase))
                    return "FaceB" + element.Substring(6);
            }
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

        public static string PreviewElement(string part)
        {
            if (string.IsNullOrWhiteSpace(part)) return null;
            switch (part.ToUpperInvariant())
            {
                case "HEAD": return "HeadA0";
                case "FACE": return "FaceA0";
                case "BODY": return "BodyA";
                case "ARMS": return "PlayerArm0";
                case "HIPS": return "HipsA";
                case "LEGS": return "LegsA0";
                case "TAIL": return "TailTexture";
                case "FACESCAR": return "MushroomA";
                case "GILLS": return "LizardScaleA3";
                case "TAILSPECKLES": return "tinyStar";
                case "ASCENSION": return "guardEye";
                case "PIXEL": return "pixel";
                default: return null;
            }
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
                    skin.AddAtlas(SharedDmsAtlasCache.Acquire(png, text));
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
