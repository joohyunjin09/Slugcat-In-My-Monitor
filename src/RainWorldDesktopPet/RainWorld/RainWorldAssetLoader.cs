using System;
using System.Collections.Generic;
using System.IO;
using RainWorldDesktopPet.Core;

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
            Status = UiLocalization.Text("아직 atlas를 검색하지 않았습니다.",
                "No atlas scan has run.");
        }

        public string Status { get; private set; }

        public RainWorldAtlasSet TryLoadPlayerAtlas()
        {
            if (installation == null)
            {
                Status = UiLocalization.Text("Rain World 설치본을 사용할 수 없습니다.",
                    "Rain World installation is unavailable.");
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
                            Status = UiLocalization.Text("로컬 atlas를 불러왔습니다: ",
                                "Loaded loose local atlas: ") + image;
                            return set;
                        }
                        set.Dispose();
                    }
                    catch (Exception exception)
                    {
                        Status = UiLocalization.Text("호환되지 않는 atlas를 건너뛰었습니다: ",
                            "Skipped incompatible atlas: ") + metadata + ": " + exception.Message;
                    }
                }
            }

            Status = embeddedFailure + UiLocalization.Text(
                " 호환되는 원본 플레이어 atlas를 찾지 못해 절차형 렌더링을 사용합니다.",
                " No compatible non-mod original loose player atlas was found; procedural rendering is active.");
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
