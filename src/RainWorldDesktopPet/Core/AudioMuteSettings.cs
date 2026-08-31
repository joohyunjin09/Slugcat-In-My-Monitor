using System;
using System.IO;
using System.Text;

namespace RainWorldDesktopPet.Core
{
    public static class AudioMuteSettings
    {
        public const bool Default = true;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "audio-muted.txt");
        private static bool loaded;
        private static bool current;

        public static bool Current
        {
            get
            {
                EnsureLoaded();
                return current;
            }
        }

        public static bool ParseSavedValue(string value)
        {
            bool parsed;
            if (bool.TryParse((value ?? string.Empty).Trim(), out parsed))
                return parsed;
            if (string.Equals((value ?? string.Empty).Trim(), "1",
                StringComparison.Ordinal)) return true;
            if (string.Equals((value ?? string.Empty).Trim(), "0",
                StringComparison.Ordinal)) return false;
            return Default;
        }

        public static void Set(bool value)
        {
            current = value;
            loaded = true;
            try
            {
                string directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(SettingsPath, value ? "true" : "false", Encoding.UTF8);
            }
            catch (Exception)
            {
                // A read-only settings directory must not prevent audio playback.
            }
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            current = Default;
            try
            {
                if (File.Exists(SettingsPath))
                    current = ParseSavedValue(File.ReadAllText(SettingsPath));
            }
            catch (Exception)
            {
                current = Default;
            }
        }
    }
}
