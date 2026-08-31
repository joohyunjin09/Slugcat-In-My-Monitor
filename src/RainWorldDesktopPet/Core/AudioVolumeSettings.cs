using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RainWorldDesktopPet.Core
{
    public static class AudioVolumeSettings
    {
        public const double Minimum = 0.0;
        public const double Maximum = 2.0;
        public const double Default = 1.0;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "audio-volume.txt");
        private static bool loaded;
        private static double current;

        public static double Current
        {
            get
            {
                EnsureLoaded();
                return current;
            }
        }

        public static double Clamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return Default;
            return Math.Max(Minimum, Math.Min(Maximum, value));
        }

        public static void Set(double value)
        {
            current = Clamp(value);
            loaded = true;
            try
            {
                string directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(SettingsPath,
                    current.ToString("0.00", CultureInfo.InvariantCulture),
                    Encoding.UTF8);
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
                if (!File.Exists(SettingsPath)) return;
                double parsed;
                if (double.TryParse(File.ReadAllText(SettingsPath).Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    current = Clamp(parsed);
            }
            catch (Exception)
            {
                current = Default;
            }
        }
    }
}
