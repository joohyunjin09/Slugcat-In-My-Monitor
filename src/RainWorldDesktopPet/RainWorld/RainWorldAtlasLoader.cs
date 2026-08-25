using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace RainWorldDesktopPet.RainWorld
{
    public sealed class RainWorldAtlas : IDisposable
    {
        private readonly Dictionary<string, AtlasElement> elements;

        public RainWorldAtlas(string imagePath, Bitmap image, Dictionary<string, AtlasElement> elements)
        {
            ImagePath = imagePath;
            Image = image;
            this.elements = elements;
        }

        public readonly string ImagePath;
        public readonly Bitmap Image;
        public IDictionary<string, AtlasElement> Elements { get { return elements; } }

        public bool TryGet(string name, out AtlasElement element)
        {
            if (elements.TryGetValue(name, out element)) return true;
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return elements.TryGetValue(name.Substring(0, name.Length - 4), out element);
            }
            return elements.TryGetValue(name + ".png", out element);
        }

        public void Dispose()
        {
            Image.Dispose();
        }
    }

    public sealed class AtlasSprite
    {
        public RainWorldAtlas Atlas;
        public AtlasElement Element;
    }

    public sealed class RainWorldAtlasSet : IDisposable
    {
        private sealed class SharedAtlasStorage
        {
            public readonly List<RainWorldAtlas> Atlases = new List<RainWorldAtlas>();
            public readonly Dictionary<string, AtlasSprite> Sprites =
                new Dictionary<string, AtlasSprite>(StringComparer.OrdinalIgnoreCase);
            private int references = 1;
            private bool disposed;

            public void AddReference()
            {
                lock (this)
                {
                    if (disposed) throw new ObjectDisposedException("RainWorldAtlasSet");
                    references++;
                }
            }

            public void Release()
            {
                RainWorldAtlas[] release = null;
                lock (this)
                {
                    if (references <= 0) return;
                    references--;
                    if (references != 0) return;
                    disposed = true;
                    release = Atlases.ToArray();
                    Atlases.Clear();
                    Sprites.Clear();
                }
                for (int i = 0; i < release.Length; i++) release[i].Dispose();
            }

            public void Add(RainWorldAtlas atlas)
            {
                if (atlas == null) throw new ArgumentNullException("atlas");
                lock (this)
                {
                    if (disposed) throw new ObjectDisposedException("RainWorldAtlasSet");
                    Atlases.Add(atlas);
                    foreach (KeyValuePair<string, AtlasElement> item in atlas.Elements)
                    {
                        AtlasSprite sprite = new AtlasSprite { Atlas = atlas, Element = item.Value };
                        Sprites[item.Key] = sprite;
                        if (item.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            Sprites[item.Key.Substring(0, item.Key.Length - 4)] = sprite;
                    }
                }
            }
        }

        private readonly SharedAtlasStorage shared;
        private readonly Dictionary<string, RainWorldAtlas> overrideAtlases =
            new Dictionary<string, RainWorldAtlas>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> overrideNames =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AtlasSprite> overrides =
            new Dictionary<string, AtlasSprite>(StringComparer.OrdinalIgnoreCase);
        private bool disposed;

        public RainWorldAtlasSet()
        {
            shared = new SharedAtlasStorage();
        }

        private RainWorldAtlasSet(SharedAtlasStorage shared)
        {
            this.shared = shared;
            shared.AddReference();
        }

        public int AtlasCount { get { return shared.Atlases.Count; } }

        public RainWorldAtlasSet CreateSharedView()
        {
            if (disposed) throw new ObjectDisposedException("RainWorldAtlasSet");
            return new RainWorldAtlasSet(shared);
        }

        public void Add(RainWorldAtlas atlas)
        {
            if (disposed) throw new ObjectDisposedException("RainWorldAtlasSet");
            shared.Add(atlas);
        }

        public bool TryGet(string name, out AtlasSprite sprite)
        {
            if (disposed)
            {
                sprite = null;
                return false;
            }
            return overrides.TryGetValue(name, out sprite) ||
                overrides.TryGetValue(name + ".png", out sprite) ||
                shared.Sprites.TryGetValue(name, out sprite) ||
                shared.Sprites.TryGetValue(name + ".png", out sprite);
        }

        public bool TryGetBase(string name, out AtlasSprite sprite)
        {
            if (disposed)
            {
                sprite = null;
                return false;
            }
            return shared.Sprites.TryGetValue(name, out sprite) ||
                shared.Sprites.TryGetValue(name + ".png", out sprite);
        }

        public void SetPartOverride(string part, RainWorldAtlas atlas)
        {
            if (disposed) throw new ObjectDisposedException("RainWorldAtlasSet");
            if (string.IsNullOrWhiteSpace(part)) throw new ArgumentNullException("part");
            ClearPartOverride(part);
            if (atlas == null) return;

            overrideAtlases[part] = atlas;
            List<string> names = new List<string>();
            foreach (KeyValuePair<string, AtlasElement> item in atlas.Elements)
            {
                names.Add(item.Key);
                if (item.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    string shortName = item.Key.Substring(0, item.Key.Length - 4);
                    names.Add(shortName);
                }
            }
            overrideNames[part] = names;
            RebuildOverrides();
        }

        public void ClearPartOverride(string part)
        {
            if (disposed) return;
            List<string> names;
            if (overrideNames.TryGetValue(part, out names))
            {
                for (int i = 0; i < names.Count; i++) overrides.Remove(names[i]);
                overrideNames.Remove(part);
            }
            RainWorldAtlas atlas;
            if (overrideAtlases.TryGetValue(part, out atlas))
            {
                overrideAtlases.Remove(part);
                atlas.Dispose();
            }
            RebuildOverrides();
        }

        private void RebuildOverrides()
        {
            overrides.Clear();
            foreach (RainWorldAtlas atlas in overrideAtlases.Values)
            {
                foreach (KeyValuePair<string, AtlasElement> item in atlas.Elements)
                {
                    AtlasSprite sprite = new AtlasSprite { Atlas = atlas, Element = item.Value };
                    overrides[item.Key] = sprite;
                    if (item.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        overrides[item.Key.Substring(0, item.Key.Length - 4)] = sprite;
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (RainWorldAtlas atlas in overrideAtlases.Values) atlas.Dispose();
            overrideAtlases.Clear();
            overrideNames.Clear();
            overrides.Clear();
            shared.Release();
        }
    }

    public static class RainWorldAtlasLoader
    {
        public static RainWorldAtlas Load(string imagePath, string metadataPath)
        {
            if (!File.Exists(imagePath)) throw new FileNotFoundException("Atlas image was not found.", imagePath);
            if (!File.Exists(metadataPath)) throw new FileNotFoundException("Atlas metadata was not found.", metadataPath);

            string json = File.ReadAllText(metadataPath);
            // Detach from the source file so the installed game can update while the pet is running.
            Bitmap image;
            using (Image source = Image.FromFile(imagePath))
            {
                image = new Bitmap(source);
            }
            return LoadFromMemory(imagePath, image, json);
        }

        /// <summary>
        /// Builds an atlas from an already decoded image and TexturePacker/Futile JSON.
        /// Ownership of <paramref name="image"/> transfers to the returned atlas. The
        /// bitmap is disposed if metadata parsing fails.
        /// </summary>
        public static RainWorldAtlas LoadFromMemory(string imageSource, Bitmap image, string metadataJson)
        {
            if (image == null) throw new ArgumentNullException("image");
            if (metadataJson == null)
            {
                image.Dispose();
                throw new ArgumentNullException("metadataJson");
            }

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> root = serializer.DeserializeObject(metadataJson) as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("frames"))
                {
                    throw new InvalidDataException("Atlas metadata does not contain a frames object: " + imageSource);
                }

                Dictionary<string, object> frameMap = root["frames"] as Dictionary<string, object>;
                if (frameMap == null)
                {
                    throw new InvalidDataException("Unsupported atlas frames representation: " + imageSource);
                }

                Dictionary<string, AtlasElement> elements = new Dictionary<string, AtlasElement>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, object> item in frameMap)
                {
                    Dictionary<string, object> record = item.Value as Dictionary<string, object>;
                    if (record == null) continue;
                    AtlasElement element = new AtlasElement();
                    element.Name = item.Key;
                    element.Frame = ReadRectangle(record, "frame");
                    element.SpriteSource = record.ContainsKey("spriteSourceSize")
                        ? ReadRectangle(record, "spriteSourceSize")
                        : new Rectangle(0, 0, element.Frame.Width, element.Frame.Height);
                    element.SourceSize = record.ContainsKey("sourceSize")
                        ? ReadSize(record, "sourceSize")
                        : element.Frame.Size;
                    object rotated;
                    element.Rotated = record.TryGetValue("rotated", out rotated) &&
                        Convert.ToBoolean(rotated, CultureInfo.InvariantCulture);
                    if (element.Rotated)
                        throw new InvalidDataException("Rotated atlas elements are not supported: " + item.Key);
                    ValidateElement(element, image.Size);

                    elements[item.Key] = element;
                    if (item.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        elements[item.Key.Substring(0, item.Key.Length - 4)] = element;
                }

                if (elements.Count == 0)
                    throw new InvalidDataException("Atlas metadata contains no usable frames: " + imageSource);
                return new RainWorldAtlas(imageSource ?? "memory", image, elements);
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        private static void ValidateElement(AtlasElement element, Size atlasSize)
        {
            Rectangle frame = element.Frame;
            long frameRight = (long)frame.X + frame.Width;
            long frameBottom = (long)frame.Y + frame.Height;
            long sourceRight = (long)element.SpriteSource.X + element.SpriteSource.Width;
            long sourceBottom = (long)element.SpriteSource.Y + element.SpriteSource.Height;
            if (frame.X < 0 || frame.Y < 0 || frame.Width <= 0 || frame.Height <= 0 ||
                frameRight > atlasSize.Width || frameBottom > atlasSize.Height)
            {
                throw new InvalidDataException("Atlas frame is outside the image: " + element.Name);
            }
            if (element.SourceSize.Width <= 0 || element.SourceSize.Height <= 0 ||
                element.SpriteSource.X < 0 || element.SpriteSource.Y < 0 ||
                element.SpriteSource.Width != frame.Width || element.SpriteSource.Height != frame.Height ||
                sourceRight > element.SourceSize.Width || sourceBottom > element.SourceSize.Height)
            {
                throw new InvalidDataException("Atlas source rectangle is invalid: " + element.Name);
            }
        }

        private static Rectangle ReadRectangle(Dictionary<string, object> parent, string key)
        {
            Dictionary<string, object> value = parent[key] as Dictionary<string, object>;
            if (value == null) throw new InvalidDataException("Invalid rectangle: " + key);
            return new Rectangle(ReadInt(value, "x"), ReadInt(value, "y"), ReadInt(value, "w"), ReadInt(value, "h"));
        }

        private static Size ReadSize(Dictionary<string, object> parent, string key)
        {
            Dictionary<string, object> value = parent[key] as Dictionary<string, object>;
            if (value == null) throw new InvalidDataException("Invalid size: " + key);
            return new Size(ReadInt(value, "w"), ReadInt(value, "h"));
        }

        private static int ReadInt(Dictionary<string, object> value, string key)
        {
            return Convert.ToInt32(value[key], CultureInfo.InvariantCulture);
        }
    }
}
