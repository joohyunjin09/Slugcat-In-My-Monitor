using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.Audio
{
    public sealed class MeowSoundVariation
    {
        public string SoundId;
        public string AssetName;
        public string FilePath;
        public double DurationSeconds;
        public float Volume = 1f;
        public float MinimumPitch = 1f;
        public float MaximumPitch = 1f;
        public float PlaybackPitch = 1f;
        public float PlaybackVolume = 1f;
    }

    public sealed class MeowVoiceSet
    {
        public string SlugcatId;
        public string ShortSoundId;
        public string LongSoundId;
        public float VolumeMultiplier = 1f;
        public readonly List<MeowSoundVariation> ShortVariations = new List<MeowSoundVariation>();
        public readonly List<MeowSoundVariation> LongVariations = new List<MeowSoundVariation>();
    }

    internal sealed class CustomMeowRecord
    {
        public string SlugcatId;
        public string ShortSoundId;
        public string LongSoundId;
        public float VolumeMultiplier = 1f;
        public int Priority;
        public string SourcePath;
    }

    public sealed class PushToMeowLibrary
    {
        private readonly WorkshopLog log;
        private readonly Dictionary<string, List<MeowSoundVariation>> sounds =
            new Dictionary<string, List<MeowSoundVariation>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MeowVoiceSet> voices =
            new Dictionary<string, MeowVoiceSet>(StringComparer.OrdinalIgnoreCase);
        private readonly Random random;
        private readonly WorkshopAssetCache assetCache;

        public PushToMeowLibrary(WorkshopCatalog workshop, WorkshopLog log, int seed)
            : this(workshop, log, seed, null)
        {
        }

        public PushToMeowLibrary(WorkshopCatalog workshop, WorkshopLog log, int seed,
            WorkshopAssetCache assetCache)
        {
            if (workshop == null) throw new ArgumentNullException("workshop");
            this.log = log ?? new WorkshopLog(false);
            random = new Random(seed);
            this.assetCache = assetCache ?? new WorkshopAssetCache();
            RainWorldMod pushToMeow = workshop.FindById("pushtomeow");
            IsInstalled = pushToMeow != null;
            IsActive = pushToMeow != null && pushToMeow.IsActive;
            if (!IsInstalled)
            {
                this.log.Info("PushToMeow", "Mod was not detected; automatic meowing is disabled.");
                return;
            }

            RootPath = pushToMeow.RootPath;
            this.log.Info("PushToMeow", "Mod detected at " + RootPath +
                (IsActive ? " [active]" : " [inactive]"));
            List<RainWorldMod> sources = workshop.InLoadOrder(false).ToList();
            if (!sources.Contains(pushToMeow)) sources.Insert(0, pushToMeow);
            ParseSoundRegistrations(sources);
            ParseVoiceRegistrations(sources);
            IsAvailable = IsActive && voices.Values.Any(voice => voice.ShortVariations.Count > 0 ||
                voice.LongVariations.Count > 0);
            this.log.Info("PushToMeow", IsAvailable
                ? "Loaded " + voices.Count + " slugcat voice mappings from mod registrations."
                : IsActive
                    ? "No playable registered meow assets were found; integration is disabled."
                    : "Mod is installed but disabled in Rain World Remix; integration is disabled.");
        }

        public bool IsInstalled { get; private set; }
        public bool IsActive { get; private set; }
        public bool IsAvailable { get; private set; }
        public string RootPath { get; private set; }
        public IEnumerable<MeowVoiceSet> VoiceSets { get { return voices.Values; } }

        public MeowVoiceSet GetVoice(string slugcatId)
        {
            MeowVoiceSet voice;
            if (voices.TryGetValue(slugcatId ?? string.Empty, out voice)) return voice;
            return BuildVoice(slugcatId, "SlugcatMeowNormalShort", "SlugcatMeowNormal", 1f);
        }

        public MeowSoundVariation Choose(string slugcatId, bool shortMeow)
        {
            MeowVoiceSet voice = GetVoice(slugcatId);
            if (voice == null) return null;
            List<MeowSoundVariation> candidates = shortMeow ? voice.ShortVariations : voice.LongVariations;
            if (candidates.Count == 0) candidates = shortMeow ? voice.LongVariations : voice.ShortVariations;
            if (candidates.Count == 0) return null;
            MeowSoundVariation source = candidates[random.Next(candidates.Count)];
            return new MeowSoundVariation
            {
                SoundId = source.SoundId,
                AssetName = source.AssetName,
                FilePath = source.FilePath,
                DurationSeconds = source.DurationSeconds,
                Volume = source.Volume,
                MinimumPitch = source.MinimumPitch,
                MaximumPitch = source.MaximumPitch,
                PlaybackPitch = source.MinimumPitch + (float)random.NextDouble() *
                    Math.Max(0f, source.MaximumPitch - source.MinimumPitch),
                // MeowMeowOptions defaults to 0.85 and CustomMeow may add a multiplier.
                PlaybackVolume = Math.Min(1f, source.Volume * voice.VolumeMultiplier * 0.85f)
            };
        }

        private void ParseSoundRegistrations(IEnumerable<RainWorldMod> mods)
        {
            foreach (RainWorldMod mod in mods)
            {
                string path = Path.Combine(mod.RootPath, "modify", "soundeffects", "sounds.txt");
                if (!File.Exists(path)) continue;
                Dictionary<string, string> assetFiles = EnumerateAudioAssets(mod.RootPath);
                try
                {
                    foreach (string rawLine in File.ReadAllLines(path))
                    {
                        string line = rawLine.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        Match match = Regex.Match(line,
                            "^(?:\\[ADD\\])?(?<id>[^/:\\s]+)(?:/[^:]*)?\\s*:\\s*(?<assets>.+)$",
                            RegexOptions.IgnoreCase);
                        if (!match.Success) continue;
                        string soundId = match.Groups["id"].Value.Trim();
                        List<MeowSoundVariation> variations = new List<MeowSoundVariation>();
                        foreach (string entry in match.Groups["assets"].Value.Split(','))
                        {
                            string[] parts = entry.Trim().Split('/');
                            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) continue;
                            string assetName = parts[0].Trim();
                            string file;
                            if (!TryResolveAsset(assetFiles, assetName, out file))
                            {
                                log.Warning("PushToMeow", "Missing registered sound asset " + assetName +
                                    " for " + soundId + " in " + mod.Name);
                                continue;
                            }
                            double duration;
                            if (!assetCache.TryGetWaveDuration(file, out duration))
                            {
                                log.Warning("PushToMeow", "Unsupported or damaged audio skipped: " + file);
                                continue;
                            }
                            MeowSoundVariation variation = new MeowSoundVariation
                            {
                                SoundId = soundId,
                                AssetName = assetName,
                                FilePath = file,
                                DurationSeconds = duration
                            };
                            for (int index = 1; index < parts.Length; index++)
                                ParseSoundAttribute(variation, parts[index]);
                            variations.Add(variation);
                        }
                        if (variations.Count > 0) sounds[soundId] = variations;
                    }
                }
                catch (Exception exception)
                {
                    log.Warning("PushToMeow", "Could not parse " + path + ": " + exception.Message);
                }
            }
        }

        private void ParseVoiceRegistrations(IEnumerable<RainWorldMod> mods)
        {
            List<CustomMeowRecord> records = new List<CustomMeowRecord>();
            foreach (RainWorldMod mod in mods)
            {
                string directory = Path.Combine(mod.RootPath, "pushtomeow");
                if (!Directory.Exists(directory)) continue;
                string[] files;
                try { files = Directory.GetFiles(directory, "custom_meows.json", SearchOption.TopDirectoryOnly); }
                catch (Exception) { continue; }
                foreach (string file in files) ParseCustomMeowFile(file, records);
            }

            foreach (CustomMeowRecord record in records.OrderBy(record => record.Priority))
            {
                MeowVoiceSet voice = BuildVoice(record.SlugcatId, record.ShortSoundId,
                    record.LongSoundId, record.VolumeMultiplier);
                if (voice == null) continue;
                voices[record.SlugcatId] = voice;
                log.Info("PushToMeow", "Loaded " + record.SlugcatId + " voice set: short=" +
                    record.ShortSoundId + " (" + voice.ShortVariations.Count + "), long=" +
                    record.LongSoundId + " (" + voice.LongVariations.Count + ") from " + record.SourcePath);
            }

            if (!voices.ContainsKey("Rivulet"))
            {
                // MeowUtils.FindMeowSoundID uses Rivulet A unless the Remix alternate toggle is enabled.
                MeowVoiceSet rivulet = BuildVoice("Rivulet", "SlugcatMeowRivuletAShort",
                    "SlugcatMeowRivuletA", 0.8f);
                if (rivulet != null) voices["Rivulet"] = rivulet;
            }
        }

        private void ParseCustomMeowFile(string path, List<CustomMeowRecord> records)
        {
            try
            {
                Dictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(
                    File.ReadAllText(path)) as Dictionary<string, object>;
                if (root == null) return;
                int priority = ReadInt(root, "priority", 0);
                object raw;
                object[] items = root.TryGetValue("custom_meows", out raw) ? raw as object[] : null;
                if (items == null) return;
                foreach (object item in items)
                {
                    Dictionary<string, object> data = item as Dictionary<string, object>;
                    if (data == null) continue;
                    string slugcatId = ReadString(data, "slugcat_id");
                    string shortId = ReadString(data, "short_meow_soundid");
                    string longId = ReadString(data, "long_meow_soundid");
                    if (string.IsNullOrWhiteSpace(slugcatId) ||
                        (string.IsNullOrWhiteSpace(shortId) && string.IsNullOrWhiteSpace(longId))) continue;
                    records.Add(new CustomMeowRecord
                    {
                        SlugcatId = slugcatId,
                        ShortSoundId = shortId,
                        LongSoundId = longId,
                        VolumeMultiplier = ReadFloat(data, "volume_multiplier", 1f),
                        Priority = priority,
                        SourcePath = path
                    });
                }
            }
            catch (Exception exception)
            {
                log.Warning("PushToMeow", "Invalid custom meow file skipped: " + path + " (" +
                    exception.Message + ")");
            }
        }

        private MeowVoiceSet BuildVoice(string slugcatId, string shortId, string longId,
            float volumeMultiplier)
        {
            List<MeowSoundVariation> shortVariations;
            List<MeowSoundVariation> longVariations;
            sounds.TryGetValue(shortId ?? string.Empty, out shortVariations);
            sounds.TryGetValue(longId ?? string.Empty, out longVariations);
            if ((shortVariations == null || shortVariations.Count == 0) &&
                (longVariations == null || longVariations.Count == 0)) return null;
            MeowVoiceSet voice = new MeowVoiceSet
            {
                SlugcatId = slugcatId,
                ShortSoundId = shortId,
                LongSoundId = longId,
                VolumeMultiplier = volumeMultiplier
            };
            if (shortVariations != null) voice.ShortVariations.AddRange(shortVariations);
            if (longVariations != null) voice.LongVariations.AddRange(longVariations);
            return voice;
        }

        private static Dictionary<string, string> EnumerateAudioAssets(string modRoot)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            string directory = Path.Combine(modRoot, "soundeffects");
            if (!Directory.Exists(directory)) return result;
            try
            {
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(file);
                    if (!extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)) continue;
                    result[Path.GetFileNameWithoutExtension(file)] = file;
                    result[Path.GetFileName(file)] = file;
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private static bool TryResolveAsset(Dictionary<string, string> assets, string assetName,
            out string file)
        {
            if (assets.TryGetValue(assetName, out file)) return true;
            return assets.TryGetValue(Path.GetFileNameWithoutExtension(assetName), out file);
        }

        private static void ParseSoundAttribute(MeowSoundVariation variation, string attribute)
        {
            string[] pair = attribute.Split(new[] { '=' }, 2);
            if (pair.Length != 2) return;
            float value;
            if (!float.TryParse(pair[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value)) return;
            switch (pair[0].Trim().ToLowerInvariant())
            {
                case "vol": variation.Volume = value; break;
                case "minpitch": variation.MinimumPitch = value; break;
                case "maxpitch": variation.MaximumPitch = value; break;
            }
        }

        private static string ReadString(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value as string : null;
        }

        private static int ReadInt(Dictionary<string, object> data, string key, int fallback)
        {
            object value;
            try { return data.TryGetValue(key, out value) ? Convert.ToInt32(value) : fallback; }
            catch (Exception) { return fallback; }
        }

        private static float ReadFloat(Dictionary<string, object> data, string key, float fallback)
        {
            object value;
            try { return data.TryGetValue(key, out value) ? Convert.ToSingle(value,
                System.Globalization.CultureInfo.InvariantCulture) : fallback; }
            catch (Exception) { return fallback; }
        }
    }
}
