using System;
using System.IO;

namespace RainWorldDesktopPet.Core
{
    public sealed class AppSettings
    {
        private readonly string path;

        private AppSettings(string path)
        {
            this.path = path;
            SoundEnabled = true;
        }

        public bool SoundEnabled { get; set; }
        public string Path { get { return path; } }

        public static AppSettings Load()
        {
            return Load(null);
        }

        public static AppSettings Load(string pathOverride)
        {
            string settingsPath = string.IsNullOrWhiteSpace(pathOverride)
                ? System.IO.Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                    "SlugcatInMyMonitor", "settings.txt")
                : System.IO.Path.GetFullPath(pathOverride);
            AppSettings settings = new AppSettings(settingsPath);
            try
            {
                if (!File.Exists(settingsPath)) return settings;
                string[] lines = File.ReadAllLines(settingsPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split(new[] { '=' }, 2);
                    if (parts.Length != 2 || !string.Equals(parts[0].Trim(),
                        "SoundEnabled", StringComparison.OrdinalIgnoreCase)) continue;
                    bool value;
                    if (bool.TryParse(parts[1].Trim(), out value))
                        settings.SoundEnabled = value;
                }
            }
            catch
            {
                // A missing/corrupt settings file must preserve the documented
                // Sound ON default and must not prevent the pet from starting.
                settings.SoundEnabled = true;
            }
            return settings;
        }

        public void Save()
        {
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, "SoundEnabled=" + SoundEnabled + Environment.NewLine);
        }
    }
}
