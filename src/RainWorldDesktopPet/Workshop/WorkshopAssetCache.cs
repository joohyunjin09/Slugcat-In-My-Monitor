using System;
using System.Collections.Generic;
using System.IO;
using RainWorldDesktopPet.Audio;

namespace RainWorldDesktopPet.Workshop
{
    /// <summary>
    /// Session cache for external-file facts that are safe to reuse. Each entry is
    /// keyed by path, length, and UTC write time, so Steam replacing a mod asset
    /// invalidates it without repeatedly parsing files during the simulation loop.
    /// </summary>
    public sealed class WorkshopAssetCache
    {
        private sealed class DurationEntry
        {
            public long Length;
            public DateTime LastWriteUtc;
            public double Duration;
            public bool Valid;
        }

        private readonly Dictionary<string, DurationEntry> audioDurations =
            new Dictionary<string, DurationEntry>(StringComparer.OrdinalIgnoreCase);

        public bool TryGetWaveDuration(string path, out double duration)
        {
            duration = 0.0;
            try
            {
                FileInfo info = new FileInfo(path);
                DurationEntry entry;
                if (audioDurations.TryGetValue(info.FullName, out entry) &&
                    entry.Length == info.Length && entry.LastWriteUtc == info.LastWriteTimeUtc)
                {
                    duration = entry.Duration;
                    return entry.Valid;
                }
                bool valid = WaveAudio.TryReadDuration(info.FullName, out duration);
                audioDurations[info.FullName] = new DurationEntry
                {
                    Length = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    Duration = duration,
                    Valid = valid
                };
                return valid;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void RemoveMissingEntries()
        {
            List<string> missing = new List<string>();
            foreach (string path in audioDurations.Keys)
                if (!File.Exists(path)) missing.Add(path);
            foreach (string path in missing) audioDurations.Remove(path);
        }
    }
}
