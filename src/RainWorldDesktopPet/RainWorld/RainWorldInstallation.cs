using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RainWorldDesktopPet.RainWorld
{
    public sealed class RainWorldInstallation
    {
        public RainWorldInstallation(string rootPath)
        {
            RootPath = Path.GetFullPath(rootPath);
            DataPath = Path.Combine(RootPath, "RainWorld_Data");
            StreamingAssetsPath = Path.Combine(DataPath, "StreamingAssets");
            ManagedPath = Path.Combine(DataPath, "Managed");
            AssemblyCSharpPath = Path.Combine(ManagedPath, "Assembly-CSharp.dll");
            ResourcesAssetsPath = Path.Combine(DataPath, "resources.assets");
            ResourcesAssetsStreamPath = ResourcesAssetsPath + ".resS";
            MoreSlugcatsModInfoPath = Path.Combine(StreamingAssetsPath, "mods",
                "moreslugcats", "modinfo.json");
        }

        public readonly string RootPath;
        public readonly string DataPath;
        public readonly string StreamingAssetsPath;
        public readonly string ManagedPath;
        public readonly string AssemblyCSharpPath;
        public readonly string ResourcesAssetsPath;
        public readonly string ResourcesAssetsStreamPath;
        public readonly string MoreSlugcatsModInfoPath;

        // Downpour ships its More Slugcats expansion as this built-in mod. This
        // marker is available before the atlas provider or any Workshop code runs.
        public bool HasMoreSlugcatsExpansion
        {
            get { return File.Exists(MoreSlugcatsModInfoPath); }
        }

        public string ReadGameVersion()
        {
            string path = Path.Combine(StreamingAssetsPath, "GameVersion.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "unknown";
        }

        public string ComputeAssemblyHash()
        {
            using (FileStream stream = File.OpenRead(AssemblyCSharpPath))
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
