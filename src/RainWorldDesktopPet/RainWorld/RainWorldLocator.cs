using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RainWorldDesktopPet.RainWorld
{
    public sealed class RainWorldLocator
    {
        private const string SteamAppId = "312520";
        private readonly string settingsPath;

        public RainWorldLocator()
            : this(null)
        {
        }

        public RainWorldLocator(string settingsPathOverride)
        {
            settingsPath = string.IsNullOrWhiteSpace(settingsPathOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SlugcatInMyMonitor", "rain-world-path.txt")
                : Path.GetFullPath(settingsPathOverride);
        }

        public RainWorldInstallation Locate(string explicitPath)
        {
            List<string> candidates = new List<string>();
            AddCandidate(candidates, explicitPath);
            AddCandidate(candidates, Environment.GetEnvironmentVariable("RAIN_WORLD_PATH"));

            string savedPath = ReadSavedPath();
            AddCandidate(candidates, savedPath);

            IList<string> steamRoots = FindSteamRoots();
            for (int i = 0; i < steamRoots.Count; i++)
            {
                foreach (string library in FindSteamLibraries(steamRoots[i]))
                {
                    AddCandidate(candidates, Path.Combine(library, "steamapps", "common", "Rain World"));
                    string manifest = Path.Combine(library, "steamapps", "appmanifest_" + SteamAppId + ".acf");
                    string installDirectory = ReadInstallDirectory(manifest);
                    if (!string.IsNullOrEmpty(installDirectory))
                    {
                        AddCandidate(candidates, Path.Combine(library, "steamapps", "common", installDirectory));
                    }
                }
            }

            string[] conventional =
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Rain World",
                @"C:\Program Files\Steam\steamapps\common\Rain World",
                @"D:\SteamLibrary\steamapps\common\Rain World",
                @"E:\SteamLibrary\steamapps\common\Rain World"
            };
            for (int i = 0; i < conventional.Length; i++) AddCandidate(candidates, conventional[i]);

            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsValid(candidates[i]))
                {
                    RainWorldInstallation installation = new RainWorldInstallation(candidates[i]);
                    SavePath(installation.RootPath);
                    return installation;
                }
            }

            return null;
        }

        public bool IsValid(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) return false;
            try
            {
                return File.Exists(Path.Combine(rootPath, "RainWorld.exe")) &&
                       File.Exists(Path.Combine(rootPath, "RainWorld_Data", "Managed", "Assembly-CSharp.dll")) &&
                       Directory.Exists(Path.Combine(rootPath, "RainWorld_Data", "StreamingAssets"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IList<string> FindSteamRoots()
        {
            List<string> roots = new List<string>();
            ReadRegistryPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            ReadRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            ReadRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
            return roots;
        }

        private static IEnumerable<string> FindSteamLibraries(string steamRoot)
        {
            List<string> libraries = new List<string>();
            AddCandidate(libraries, steamRoot);
            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) return libraries;
            try
            {
                string contents = File.ReadAllText(vdf);
                MatchCollection matches = Regex.Matches(contents, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    AddCandidate(libraries, match.Groups["path"].Value.Replace("\\\\", "\\"));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return libraries;
        }

        private static string ReadInstallDirectory(string manifestPath)
        {
            if (!File.Exists(manifestPath)) return null;
            try
            {
                Match match = Regex.Match(File.ReadAllText(manifestPath), "\\\"installdir\\\"\\s+\\\"(?<dir>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                return match.Success ? match.Groups["dir"].Value : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static void ReadRegistryPath(List<string> result, RegistryKey hive, string keyPath, string valueName)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(keyPath))
                {
                    if (key != null) AddCandidate(result, key.GetValue(valueName) as string);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void AddCandidate(ICollection<string> result, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string normalized;
            try { normalized = Path.GetFullPath(path.Trim().Trim('"')); }
            catch (Exception) { return; }
            foreach (string existing in result)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)) return;
            }
            result.Add(normalized);
        }

        private string ReadSavedPath()
        {
            try { return File.Exists(settingsPath) ? File.ReadAllText(settingsPath).Trim() : null; }
            catch (Exception) { return null; }
        }

        private void SavePath(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllText(settingsPath, path);
            }
            catch (Exception)
            {
            }
        }
    }
}
