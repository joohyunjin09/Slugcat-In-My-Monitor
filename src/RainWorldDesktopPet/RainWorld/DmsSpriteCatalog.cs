using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace RainWorldDesktopPet.RainWorld
{
    public sealed class DmsSpriteSet : IDisposable
    {
        private readonly Dictionary<string, RainWorldAtlas> thumbnails =
            new Dictionary<string, RainWorldAtlas>(StringComparer.OrdinalIgnoreCase);

        internal DmsSpriteSet(string directory, string id, string name, string author)
        {
            DirectoryPath = directory;
            Id = id;
            Name = name;
            Author = author;
        }

        public readonly string DirectoryPath;
        public readonly string Id;
        public readonly string Name;
        public readonly string Author;

        public bool TryGetPartFiles(string part, out string imagePath, out string metadataPath)
        {
            string stem = DmsSpriteCatalog.GetFileStem(part);
            imagePath = Path.Combine(DirectoryPath, stem + ".png");
            metadataPath = Path.Combine(DirectoryPath, stem + ".txt");
            return File.Exists(imagePath) && File.Exists(metadataPath);
        }

        public bool TryGetPreview(string part, out AtlasSprite sprite)
        {
            sprite = null;
            RainWorldAtlas partAtlas;
            if (!thumbnails.TryGetValue(part, out partAtlas))
            {
                string image;
                string metadata;
                if (!TryGetPartFiles(part, out image, out metadata)) return false;
                try { partAtlas = RainWorldAtlasLoader.Load(image, metadata); }
                catch { return false; }
                thumbnails[part] = partAtlas;
            }

            AtlasElement element;
            string preferred = DmsSpriteCatalog.GetPreviewElement(part);
            if (!partAtlas.TryGet(preferred, out element))
            {
                foreach (KeyValuePair<string, AtlasElement> item in partAtlas.Elements)
                {
                    element = item.Value;
                    break;
                }
            }
            if (element == null) return false;
            sprite = new AtlasSprite { Atlas = partAtlas, Element = element };
            return true;
        }

        public void Dispose()
        {
            foreach (RainWorldAtlas atlas in thumbnails.Values) atlas.Dispose();
            thumbnails.Clear();
        }
    }

    public sealed class DmsSpriteCatalog : IDisposable
    {
        private readonly RainWorldInstallation installation;
        private readonly string applicationDirectory;
        private readonly List<DmsSpriteSet> sets = new List<DmsSpriteSet>();

        public DmsSpriteCatalog(RainWorldInstallation installation)
            : this(installation, AppDomain.CurrentDomain.BaseDirectory)
        {
        }

        internal DmsSpriteCatalog(RainWorldInstallation installation, string applicationDirectory)
        {
            this.installation = installation;
            this.applicationDirectory = applicationDirectory;
            Reload();
        }

        public IList<DmsSpriteSet> Sets { get { return sets; } }
        public string Status { get; private set; }

        public void Reload()
        {
            for (int i = 0; i < sets.Count; i++) sets[i].Dispose();
            sets.Clear();

            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string local = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "SlugcatInMyMonitor", "skins");
            AddDirectory(roots, local);
            AddDirectory(roots, applicationDirectory);
            AddAncestorSkinRoots(roots, applicationDirectory);
            if (installation != null)
            {
                AddDirectory(roots, Path.Combine(installation.RootPath, "mods"));
                AddDirectory(roots, Path.Combine(installation.StreamingAssetsPath, "mods"));
                AddDirectory(roots, Path.Combine(installation.RootPath, "dressmyslugcat"));
            }

            HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots) FindCandidates(root, candidates);
            foreach (string directory in candidates)
            {
                if (!HasAnyPart(directory)) continue;
                string id = Path.GetFileName(directory);
                string name = id;
                string author = string.Empty;
                ReadMetadata(directory, ref id, ref name, ref author);
                sets.Add(new DmsSpriteSet(directory, id, name, author));
            }
            sets.Sort(delegate(DmsSpriteSet left, DmsSpriteSet right)
            {
                return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });
            Status = sets.Count + " DMS sprite set(s) found in " + roots.Count + " search root(s).";
        }

        public static string GetFileStem(string part)
        {
            switch (part)
            {
                case "Head": return "head";
                case "Face": return "face";
                case "Body": return "body";
                case "Arms": return "arm";
                case "Hips": return "hips";
                case "Legs": return "legs";
                case "Tail": return "tail";
                case "The Mark": return "pixel";
                default: return part.ToLowerInvariant();
            }
        }

        public static string GetPreviewElement(string part)
        {
            switch (part)
            {
                case "Head": return "HeadA0";
                case "Face": return "FaceA0";
                case "Body": return "BodyA";
                case "Arms": return "PlayerArm0";
                case "Hips": return "HipsA";
                case "Legs": return "LegsA0";
                case "Tail": return "TailTexture";
                case "The Mark": return "pixel";
                default: return string.Empty;
            }
        }

        private static void AddAncestorSkinRoots(ICollection<string> roots, string start)
        {
            DirectoryInfo current = new DirectoryInfo(start);
            for (int i = 0; current != null && i < 7; i++, current = current.Parent)
            {
                AddDirectory(roots, Path.Combine(current.FullName, "assets", "skins"));
                AddDirectory(roots, Path.Combine(current.FullName, "skins"));
            }
        }

        private static void AddDirectory(ICollection<string> roots, string path)
        {
            if (Directory.Exists(path)) roots.Add(Path.GetFullPath(path));
        }

        private static void FindCandidates(string root, ICollection<string> candidates)
        {
            try
            {
                string[] metadata = Directory.GetFiles(root, "metadata.json", SearchOption.AllDirectories);
                for (int i = 0; i < metadata.Length; i++)
                    candidates.Add(Path.GetDirectoryName(metadata[i]));
                string[] heads = Directory.GetFiles(root, "head.png", SearchOption.AllDirectories);
                for (int i = 0; i < heads.Length; i++)
                    candidates.Add(Path.GetDirectoryName(heads[i]));
            }
            catch { }
        }

        private static bool HasAnyPart(string directory)
        {
            string[] parts = { "Head", "Face", "Body", "Arms", "Hips", "Legs", "Tail", "The Mark" };
            for (int i = 0; i < parts.Length; i++)
            {
                string stem = GetFileStem(parts[i]);
                if (File.Exists(Path.Combine(directory, stem + ".png")) &&
                    File.Exists(Path.Combine(directory, stem + ".txt"))) return true;
            }
            return false;
        }

        private static void ReadMetadata(string directory, ref string id, ref string name, ref string author)
        {
            string path = Path.Combine(directory, "metadata.json");
            if (!File.Exists(path)) return;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> data = serializer.DeserializeObject(
                    File.ReadAllText(path)) as Dictionary<string, object>;
                object value;
                if (data != null && data.TryGetValue("id", out value)) id = Convert.ToString(value);
                if (data != null && data.TryGetValue("name", out value)) name = Convert.ToString(value);
                if (data != null && data.TryGetValue("author", out value)) author = Convert.ToString(value);
            }
            catch { }
        }

        public void Dispose()
        {
            for (int i = 0; i < sets.Count; i++) sets[i].Dispose();
            sets.Clear();
        }
    }
}
