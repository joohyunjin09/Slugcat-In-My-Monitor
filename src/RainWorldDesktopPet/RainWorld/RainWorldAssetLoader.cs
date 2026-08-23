using System;
using System.Collections.Generic;
using System.IO;

namespace RainWorldDesktopPet.RainWorld
{
    public sealed class RainWorldAssetLoader
    {
        private static readonly string[] RequiredPlayerElements =
        {
            "BodyA", "HipsA", "HeadA0", "FaceA0"
        };

        private readonly RainWorldInstallation installation;

        public RainWorldAssetLoader(RainWorldInstallation installation)
        {
            this.installation = installation;
            Status = "No atlas scan has run.";
        }

        public string Status { get; private set; }

        public RainWorldAtlasSet TryLoadPlayerAtlas()
        {
            if (installation == null)
            {
                Status = "Rain World installation is unavailable.";
                return null;
            }

            EmbeddedUnityAtlasProvider embedded = new EmbeddedUnityAtlasProvider(installation);
            RainWorldAtlasSet embeddedSet = embedded.TryLoadPlayerAtlases();
            if (embeddedSet != null)
            {
                Status = embedded.Status;
                return embeddedSet;
            }
            string embeddedFailure = embedded.Status;

            List<string> roots = new List<string>();
            AddIfDirectory(roots, Path.Combine(installation.StreamingAssetsPath, "atlases"));

            // A loose Futile override can contain the complete rainWorld atlas in one pair.
            for (int i = 0; i < roots.Count; i++)
            {
                string[] files;
                // EnumerateFiles is lazy, so exceptions from inaccessible or
                // disappearing subfolders would otherwise escape the try block
                // during foreach and prevent the procedural fallback.
                try { files = Directory.GetFiles(roots[i], "*.txt", SearchOption.AllDirectories); }
                catch (Exception) { continue; }
                foreach (string metadata in files)
                {
                    if (IsModPath(metadata)) continue;
                    string name = Path.GetFileNameWithoutExtension(metadata);
                    if (!LooksLikePlayerAtlas(name, metadata)) continue;
                    string image = Path.ChangeExtension(metadata, ".png");
                    if (!File.Exists(image)) continue;
                    try
                    {
                        RainWorldAtlas atlas = RainWorldAtlasLoader.Load(image, metadata);
                        RainWorldAtlasSet set = new RainWorldAtlasSet();
                        set.Add(atlas);
                        if (ContainsPlayerElements(set))
                        {
                            Status = "Loaded loose local atlas: " + image;
                            return set;
                        }
                        set.Dispose();
                    }
                    catch (Exception exception)
                    {
                        Status = "Skipped incompatible atlas " + metadata + ": " + exception.Message;
                    }
                }
            }

            Status = embeddedFailure + " No compatible non-mod original loose player atlas was found; procedural rendering is active.";
            return null;
        }

        // Retained for callers compiled against the initial prototype. Loading is no
        // longer limited to loose assets; embedded original atlases are attempted first.
        public RainWorldAtlasSet TryLoadLoosePlayerAtlas()
        {
            return TryLoadPlayerAtlas();
        }

        private static bool ContainsPlayerElements(RainWorldAtlasSet atlas)
        {
            for (int i = 0; i < RequiredPlayerElements.Length; i++)
            {
                AtlasSprite element;
                if (!atlas.TryGet(RequiredPlayerElements[i], out element)) return false;
            }
            return true;
        }

        private static bool LooksLikePlayerAtlas(string name, string fullPath)
        {
            string value = (name + " " + fullPath).ToLowerInvariant();
            string canonical = name.Replace("_", string.Empty).Replace("-", string.Empty);
            return string.Equals(canonical, "rainworld", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(canonical, "rainworldmsc", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("player") || value.Contains("slugcat") || value.Contains("body");
        }

        private static bool IsModPath(string path)
        {
            string normalized = path.Replace('/', '\\').ToLowerInvariant();
            return normalized.Contains("\\dressmyslugcat\\") ||
                   normalized.Contains("\\dms\\") ||
                   normalized.Contains("\\mods\\") ||
                   normalized.Contains("\\mergedmods\\");
        }

        private static void AddIfDirectory(ICollection<string> result, string path)
        {
            if (Directory.Exists(path)) result.Add(path);
        }
    }
}
