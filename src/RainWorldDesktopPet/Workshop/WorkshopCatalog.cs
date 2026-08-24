using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Workshop
{
    public sealed class RainWorldMod
    {
        public string Id;
        public string Name;
        public string Authors;
        public string WorkshopId;
        public string RootPath;
        public string[] Requirements = new string[0];
        public bool IsActive;
        public bool IsWorkshop;
        public int LoadOrder = int.MaxValue;
        public long SourceFingerprint;

        public override string ToString()
        {
            return Name + " (" + Id + ")";
        }
    }

    public sealed class WorkshopCatalog : IDisposable
    {
        private const string RainWorldAppId = "312520";
        private readonly RainWorldInstallation installation;
        private readonly WorkshopLog log;
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly object dirtyLock = new object();
        private volatile bool pendingChanges;

        public WorkshopCatalog(RainWorldInstallation installation, WorkshopLog log)
        {
            if (installation == null) throw new ArgumentNullException("installation");
            this.installation = installation;
            this.log = log ?? new WorkshopLog(false);
            Mods = new List<RainWorldMod>();
            Scan();
            StartWatchers();
        }

        public IList<RainWorldMod> Mods { get; private set; }
        public IList<string> WorkshopRoots { get; private set; }
        public bool HasPendingChanges { get { return pendingChanges; } }
        public int Revision { get; private set; }

        public RainWorldMod FindById(string id)
        {
            return Mods.FirstOrDefault(mod => string.Equals(mod.Id, id,
                StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<RainWorldMod> InLoadOrder(bool includeInactive)
        {
            return Mods.Where(mod => includeInactive || mod.IsActive)
                .OrderBy(mod => mod.LoadOrder)
                .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase);
        }

        public void Refresh()
        {
            Scan();
            lock (dirtyLock) pendingChanges = false;
        }

        private void Scan()
        {
            HashSet<string> active = ReadEnabledMods();
            Dictionary<string, int> loadOrder = ReadLoadOrder();
            List<string> roots = FindWorkshopContentRoots();
            List<RainWorldMod> found = new List<RainWorldMod>();

            foreach (string root in roots)
            {
                log.Info("Workshop", "Rain World workshop path found: " + root);
                AddModsBelow(found, root, true, active, loadOrder);
            }

            string localMods = Path.Combine(installation.RootPath, "mods");
            if (Directory.Exists(localMods)) AddModsBelow(found, localMods, false, active, loadOrder);
            string mergedMods = Path.Combine(installation.RootPath, "mergedmods");
            if (Directory.Exists(mergedMods)) AddModsBelow(found, mergedMods, false, active, loadOrder);

            Mods = found.GroupBy(mod => mod.RootPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList();
            WorkshopRoots = roots;
            Revision++;
            log.Info("Workshop", "Discovered " + Mods.Count + " installed Rain World mods; " +
                Mods.Count(mod => mod.IsActive) + " are enabled in Remix.");
        }

        private void AddModsBelow(List<RainWorldMod> result, string root, bool workshop,
            HashSet<string> active, Dictionary<string, int> loadOrder)
        {
            string[] directories;
            try { directories = Directory.GetDirectories(root); }
            catch (Exception exception)
            {
                log.Warning("Workshop", "Could not enumerate " + root + ": " + exception.Message);
                return;
            }

            foreach (string directory in directories)
            {
                string modInfo = Path.Combine(directory, "modinfo.json");
                if (!File.Exists(modInfo)) continue;
                RainWorldMod mod;
                try
                {
                    mod = ReadMod(modInfo);
                    mod.RootPath = Path.GetFullPath(directory);
                    mod.IsWorkshop = workshop;
                    mod.WorkshopId = workshop ? Path.GetFileName(directory) : null;
                    mod.IsActive = active.Contains(mod.Id);
                    int order;
                    if (loadOrder.TryGetValue(mod.Id, out order)) mod.LoadOrder = order;
                    mod.SourceFingerprint = ComputeSourceFingerprint(directory);
                    result.Add(mod);
                    log.Verbose("Workshop", "Detected " + mod + " at " + directory +
                        (mod.IsActive ? " [active]" : " [inactive]"));
                }
                catch (Exception exception)
                {
                    log.Warning("Workshop", "Skipping invalid mod metadata " + modInfo + ": " +
                        exception.Message);
                }
            }
        }

        private static RainWorldMod ReadMod(string path)
        {
            string json = File.ReadAllText(path);
            Dictionary<string, object> data = null;
            try
            {
                data = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (ArgumentException)
            {
                // Remix modinfo files in the wild sometimes contain trailing commas.
            }

            RainWorldMod mod = new RainWorldMod();
            mod.Id = ReadString(data, "id") ?? ExtractJsonString(json, "id");
            mod.Name = ReadString(data, "name") ?? ExtractJsonString(json, "name") ?? mod.Id;
            mod.Authors = ReadString(data, "authors") ?? ExtractJsonString(json, "authors");
            object requirements;
            if (data != null && data.TryGetValue("requirements", out requirements))
            {
                object[] values = requirements as object[];
                if (values != null) mod.Requirements = values.Select(value => Convert.ToString(value)).ToArray();
            }
            if (string.IsNullOrWhiteSpace(mod.Id)) throw new InvalidDataException("Missing mod id.");
            return mod;
        }

        private static string ReadString(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as string : null;
        }

        internal static string ExtractJsonString(string json, string key)
        {
            Match match = Regex.Match(json ?? string.Empty, "[\\\"]" + Regex.Escape(key) +
                "[\\\"]\\s*:\\s*[\\\"](?<value>(?:\\\\.|[^\\\"])*)[\\\"]",
                RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            try { return Regex.Unescape(match.Groups["value"].Value); }
            catch (ArgumentException) { return match.Groups["value"].Value; }
        }

        private HashSet<string> ReadEnabledMods()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string options = ReadRainWorldOptions();
            Match match = Regex.Match(options, "EnabledMods<optB>(?<value>.*?)<optA>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return result;
            foreach (string id in match.Groups["value"].Value.Split(new[] { "<optC>" },
                StringSplitOptions.RemoveEmptyEntries))
            {
                result.Add(id.Trim());
            }
            return result;
        }

        private Dictionary<string, int> ReadLoadOrder()
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string options = ReadRainWorldOptions();
            Match match = Regex.Match(options, "ModLoadOrder<optB>(?<value>.*?)<optA>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return result;
            foreach (string record in match.Groups["value"].Value.Split(new[] { "<optC>" },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = record.Split(new[] { "<optD>" }, StringSplitOptions.None);
                int value;
                if (parts.Length == 2 && int.TryParse(parts[1], out value)) result[parts[0]] = value;
            }
            return result;
        }

        private static string ReadRainWorldOptions()
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "LocalLow", "Videocult", "Rain World");
                string path = Path.GetFullPath(Path.Combine(root, "options"));
                return File.Exists(path) ? WebUtility.HtmlDecode(File.ReadAllText(path)) : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private List<string> FindWorkshopContentRoots()
        {
            List<string> result = new List<string>();
            foreach (string library in FindSteamLibraries())
            {
                AddUniqueExisting(result, Path.Combine(library, "steamapps", "workshop", "content", RainWorldAppId));
            }

            DirectoryInfo common = Directory.GetParent(installation.RootPath);
            if (common != null && common.Parent != null && common.Name.Equals("common",
                StringComparison.OrdinalIgnoreCase))
            {
                string steamApps = common.Parent.FullName;
                AddUniqueExisting(result, Path.Combine(steamApps, "workshop", "content", RainWorldAppId));
            }
            return result;
        }

        private static IEnumerable<string> FindSteamLibraries()
        {
            List<string> roots = new List<string>();
            ReadRegistryPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            ReadRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            ReadRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
            List<string> libraries = new List<string>();
            foreach (string root in roots)
            {
                AddUnique(libraries, root);
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdf),
                        "[\\\"]path[\\\"]\\s+[\\\"](?<path>[^\\\"]+)[\\\"]", RegexOptions.IgnoreCase))
                    {
                        AddUnique(libraries, match.Groups["path"].Value.Replace("\\\\", "\\"));
                    }
                }
                catch (Exception)
                {
                }
            }
            return libraries;
        }

        private static void ReadRegistryPath(List<string> result, RegistryKey hive,
            string keyPath, string valueName)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(keyPath))
                {
                    if (key != null) AddUnique(result, key.GetValue(valueName) as string);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void AddUnique(List<string> result, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string normalized;
            try { normalized = Path.GetFullPath(path.Trim().Trim('"')); }
            catch (Exception) { return; }
            if (!result.Any(existing => string.Equals(existing, normalized,
                StringComparison.OrdinalIgnoreCase))) result.Add(normalized);
        }

        private static void AddUniqueExisting(List<string> result, string path)
        {
            if (Directory.Exists(path)) AddUnique(result, path);
        }

        private static long ComputeSourceFingerprint(string root)
        {
            long fingerprint = 1469598103934665603L;
            try
            {
                List<string> files = new List<string>();
                AddFileIfPresent(files, Path.Combine(root, "modinfo.json"));
                AddIntegrationFiles(files, Path.Combine(root, "plugins"), true);
                AddIntegrationFiles(files, Path.Combine(root, "newest", "plugins"), true);
                AddIntegrationFiles(files, Path.Combine(root, "pushtomeow"), true);
                AddIntegrationFiles(files, Path.Combine(root, "soundeffects"), true);
                AddIntegrationFiles(files, Path.Combine(root, "modify", "soundeffects"), true);
                AddIntegrationFiles(files, Path.Combine(root, "dressmyslugcat"), true);
                foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    FileInfo info = new FileInfo(file);
                    unchecked
                    {
                        fingerprint ^= StringComparer.OrdinalIgnoreCase.GetHashCode(
                            file.Substring(root.Length));
                        fingerprint *= 1099511628211L;
                        fingerprint ^= info.Length;
                        fingerprint *= 1099511628211L;
                        fingerprint ^= info.LastWriteTimeUtc.Ticks;
                        fingerprint *= 1099511628211L;
                    }
                }
            }
            catch (Exception)
            {
                return 0L;
            }
            return fingerprint;
        }

        private static void AddFileIfPresent(List<string> files, string path)
        {
            if (File.Exists(path)) files.Add(path);
        }

        private static void AddIntegrationFiles(List<string> files, string directory, bool recursive)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                if (IsIntegrationInput(file)) files.Add(file);
        }

        private static bool IsIntegrationInput(string path)
        {
            string extension = Path.GetExtension(path);
            string name = Path.GetFileName(path);
            return name.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("custom_meows.json", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("sounds.txt", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("metadata.json", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private void StartWatchers()
        {
            foreach (string root in WorkshopRoots.Concat(new[]
            {
                Path.Combine(installation.RootPath, "mods"),
                Path.Combine(installation.RootPath, "mergedmods")
            }))
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    FileSystemWatcher watcher = new FileSystemWatcher(root);
                    watcher.IncludeSubdirectories = true;
                    watcher.NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName |
                        NotifyFilters.LastWrite | NotifyFilters.Size;
                    watcher.Changed += MarkDirty;
                    watcher.Created += MarkDirty;
                    watcher.Deleted += MarkDirty;
                    watcher.Renamed += MarkDirty;
                    watcher.EnableRaisingEvents = true;
                    watchers.Add(watcher);
                }
                catch (Exception exception)
                {
                    log.Warning("Workshop", "Could not watch " + root + ": " + exception.Message);
                }
            }
        }

        private void MarkDirty(object sender, FileSystemEventArgs eventArgs)
        {
            lock (dirtyLock) pendingChanges = true;
        }

        public void Dispose()
        {
            foreach (FileSystemWatcher watcher in watchers) watcher.Dispose();
            watchers.Clear();
        }
    }
}
