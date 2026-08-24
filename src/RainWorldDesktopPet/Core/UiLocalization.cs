using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RainWorldDesktopPet.Core
{
    public enum UiLanguage
    {
        Korean,
        English
    }

    public static class UiLocalization
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlugcatInMyMonitor", "language.txt");
        private static bool loaded;
        private static UiLanguage current;

        public static UiLanguage Current
        {
            get
            {
                EnsureLoaded();
                return current;
            }
        }

        public static string Text(string korean, string english)
        {
            return Current == UiLanguage.Korean ? korean : english;
        }

        public static void SetLanguage(UiLanguage language)
        {
            current = language;
            loaded = true;
            try
            {
                string directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(SettingsPath,
                    language == UiLanguage.Korean ? "ko" : "en", Encoding.UTF8);
            }
            catch (Exception)
            {
                // A read-only settings directory must not prevent the app from running.
            }
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string value = File.ReadAllText(SettingsPath).Trim();
                    if (string.Equals(value, "ko", StringComparison.OrdinalIgnoreCase))
                    {
                        current = UiLanguage.Korean;
                        return;
                    }
                    if (string.Equals(value, "en", StringComparison.OrdinalIgnoreCase))
                    {
                        current = UiLanguage.English;
                        return;
                    }
                }
            }
            catch (Exception)
            {
            }

            current = string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                "ko", StringComparison.OrdinalIgnoreCase)
                ? UiLanguage.Korean : UiLanguage.English;
        }
    }
}
