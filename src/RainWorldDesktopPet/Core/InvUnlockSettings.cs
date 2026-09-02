using System;
using System.IO;
using System.Text;

namespace RainWorldDesktopPet.Core
{
    internal sealed class InvUnlockSettings
    {
        internal const string MarkerContents = "sofanthiel";
        private readonly string markerPath;

        internal InvUnlockSettings()
            : this(BuildMarkerPath(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                typeof(InvUnlockSettings).Assembly.ManifestModule.ModuleVersionId))
        {
        }

        internal static string BuildMarkerPath(string localApplicationDataPath, Guid buildId)
        {
            if (string.IsNullOrWhiteSpace(localApplicationDataPath))
                throw new ArgumentException("A local application-data path is required.",
                    "localApplicationDataPath");
            if (buildId == Guid.Empty)
                throw new ArgumentException("A non-empty build ID is required.", "buildId");

            return Path.Combine(localApplicationDataPath, "SlugcatInMyMonitor",
                "inv-unlocks", buildId.ToString("N"), "inv-unlocked.txt");
        }

        internal InvUnlockSettings(string markerPath)
        {
            if (string.IsNullOrWhiteSpace(markerPath))
                throw new ArgumentException("An unlock marker path is required.", "markerPath");
            this.markerPath = markerPath;
        }

        internal string MarkerPath { get { return markerPath; } }

        internal bool IsUnlocked
        {
            get
            {
                try
                {
                    return File.Exists(markerPath) && string.Equals(
                        File.ReadAllText(markerPath, Encoding.UTF8).Trim(),
                        MarkerContents, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception exception)
                {
                    Program.LogException(exception);
                    return false;
                }
            }
        }

        internal bool TryUnlock(out string reason)
        {
            reason = null;
            try
            {
                string directory = Path.GetDirectoryName(markerPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(markerPath, MarkerContents, new UTF8Encoding(false));
                if (!IsUnlocked)
                {
                    reason = "The Inv unlock marker could not be verified.";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                reason = exception.Message;
                return false;
            }
        }

        internal bool TryLock(out string reason)
        {
            reason = null;
            try
            {
                if (File.Exists(markerPath)) File.Delete(markerPath);
                if (File.Exists(markerPath))
                {
                    reason = "The Inv unlock marker could not be removed.";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                Program.LogException(exception);
                reason = exception.Message;
                return false;
            }
        }
    }
}
