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
        private readonly List<RainWorldAtlas> atlases = new List<RainWorldAtlas>();
        private readonly Dictionary<string, AtlasSprite> sprites = new Dictionary<string, AtlasSprite>(StringComparer.OrdinalIgnoreCase);

        public int AtlasCount { get { return atlases.Count; } }

        public void Add(RainWorldAtlas atlas)
        {
            atlases.Add(atlas);
            foreach (KeyValuePair<string, AtlasElement> item in atlas.Elements)
            {
                AtlasSprite sprite = new AtlasSprite();
                sprite.Atlas = atlas;
                sprite.Element = item.Value;
                sprites[item.Key] = sprite;
                if (item.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    sprites[item.Key.Substring(0, item.Key.Length - 4)] = sprite;
                }
            }
        }

        public bool TryGet(string name, out AtlasSprite sprite)
        {
            return sprites.TryGetValue(name, out sprite) || sprites.TryGetValue(name + ".png", out sprite);
        }

        public void Dispose()
        {
            for (int i = 0; i < atlases.Count; i++) atlases[i].Dispose();
            atlases.Clear();
            sprites.Clear();
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
