using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.RainWorld;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.Audio
{
    public sealed class PushToMeowSound
    {
        internal PushToMeowSound(string soundId, double volume, double pitch)
        {
            SoundId = soundId;
            Volume = volume;
            Pitch = pitch;
        }

        public readonly string SoundId;
        public readonly double Volume;
        public readonly double Pitch;
    }

    public interface IPushToMeowSource
    {
        bool PushToMeowAvailable { get; }
        bool Muted { get; }
        bool TryResolveMeow(SlugcatId slugcat, bool pup, bool shortMeow,
            out PushToMeowSound sound);
    }

    internal sealed class PushToMeowVoiceProfile
    {
        internal string Short;
        internal string Long;
        internal string ShortPup;
        internal string LongPup;
        internal double Volume = 1.0;
    }

    public sealed class PushToMeowLibrary
    {
        // Push To Meow 1.2.4's default Remix volume setting.
        private const double GlobalMeowVolume = 0.85;
        private readonly Dictionary<string, PushToMeowVoiceProfile> profiles =
            new Dictionary<string, PushToMeowVoiceProfile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> looseWavFiles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private PushToMeowLibrary(string rootPath, string version,
            bool active, RainWorldSoundCatalog sounds)
        {
            RootPath = rootPath;
            Version = version;
            IsActive = active;
            Sounds = sounds;
        }

        public string RootPath { get; private set; }
        public string Version { get; private set; }
        public bool IsActive { get; private set; }
        internal RainWorldSoundCatalog Sounds { get; private set; }
        public int LooseClipCount { get { return looseWavFiles.Count; } }
        public int PreparedClipCount { get; internal set; }
        internal IEnumerable<KeyValuePair<string, string>> LooseClips
        { get { return looseWavFiles; } }

        internal static PushToMeowLibrary TryLoad(RainWorldInstallation installation,
            out string reason)
        {
            reason = null;
            if (installation == null)
            {
                reason = "Rain World installation unavailable";
                return null;
            }

            RainWorldMod mod;
            using (WorkshopCatalog workshop = new WorkshopCatalog(installation,
                new WorkshopLog(false)))
            {
                mod = workshop.FindById("pushtomeow");
            }
            if (mod == null)
            {
                reason = "Push To Meow is not installed";
                return null;
            }
            return LoadFromRoot(mod.RootPath, mod.IsActive, out reason);
        }

        public static PushToMeowLibrary LoadFromRoot(string rootPath,
            bool active, out string reason)
        {
            reason = null;
            try
            {
                string dll = Path.Combine(rootPath, "plugins", "PushToMeowMod.dll");
                string soundsPath = Path.Combine(rootPath, "modify", "soundeffects",
                    "sounds.txt");
                string soundEffects = Path.Combine(rootPath, "soundeffects");
                if (!File.Exists(dll) || !File.Exists(soundsPath) ||
                    !Directory.Exists(soundEffects))
                {
                    reason = "Push To Meow DLL or sound files are incomplete";
                    return null;
                }

                string version = ReadVersion(Path.Combine(rootPath, "modinfo.json"));
                PushToMeowLibrary library = new PushToMeowLibrary(
                    Path.GetFullPath(rootPath), version, active, null);
                library.LoadDefaultProfiles();
                library.LoadCustomProfiles(Path.Combine(rootPath, "pushtomeow",
                    "custom_meows.json"));
                library.Sounds = RainWorldSoundCatalog.Load(soundsPath,
                    library.RelevantSoundIds());
                ISet<string> permittedClips = library.Sounds.ReferencedClipNames();
                foreach (string file in Directory.EnumerateFiles(soundEffects, "*.wav",
                    SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrWhiteSpace(name) && permittedClips.Contains(name))
                        library.looseWavFiles[name] = file;
                }
                if (library.looseWavFiles.Count == 0)
                {
                    reason = "Push To Meow has no WAV files";
                    return null;
                }
                return library;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return null;
            }
        }

        internal bool TryGetLoosePath(string clipName, out string path)
        {
            return looseWavFiles.TryGetValue(clipName ?? string.Empty, out path);
        }

        internal IEnumerable<string> RelevantClipNames()
        {
            ISet<string> referenced = Sounds.ReferencedClipNames();
            referenced.IntersectWith(looseWavFiles.Keys);
            return referenced;
        }

        private ISet<string> RelevantSoundIds()
        {
            HashSet<string> soundIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int slugcatIndex = 0; slugcatIndex < SlugcatProfiles.All.Count;
                slugcatIndex++)
            {
                SlugcatId slugcat = SlugcatProfiles.All[slugcatIndex].Id;
                for (int pup = 0; pup < 2; pup++)
                {
                    for (int shortMeow = 0; shortMeow < 2; shortMeow++)
                    {
                        PushToMeowSound sound;
                        if (TryResolve(slugcat, pup != 0, shortMeow != 0, out sound))
                            soundIds.Add(sound.SoundId);
                    }
                }
            }
            return soundIds;
        }

        public bool TryResolve(SlugcatId slugcat, bool pup, bool shortMeow,
            out PushToMeowSound sound)
        {
            sound = null;
            string originalId = OriginalId(slugcat);
            PushToMeowVoiceProfile profile;
            if (!profiles.TryGetValue(originalId, out profile))
                profile = DefaultNormalProfile();

            string id;
            bool usesPupVoice = pup &&
                !string.IsNullOrWhiteSpace(shortMeow ? profile.ShortPup : profile.LongPup);
            if (usesPupVoice)
                id = shortMeow ? profile.ShortPup : profile.LongPup;
            else
                id = shortMeow ? profile.Short : profile.Long;
            if (string.IsNullOrWhiteSpace(id)) return false;

            // FindMeowSoundID starts pups at 1.3 pitch, but resets to 1.0 when
            // a custom profile supplies a dedicated pup SoundID.
            double pitch = pup && !usesPupVoice ? 1.3 : 1.0;
            sound = new PushToMeowSound(id,
                GlobalMeowVolume * profile.Volume, pitch);
            return true;
        }

        private void LoadDefaultProfiles()
        {
            PushToMeowVoiceProfile normal = DefaultNormalProfile();
            profiles["White"] = Clone(normal);
            profiles["Yellow"] = Clone(normal);
            profiles["Red"] = Clone(normal);
            profiles["Gourmand"] = Clone(normal);
            profiles["Artificer"] = Clone(normal);
            profiles["Spear"] = Clone(normal);
            profiles["Saint"] = Clone(normal);
            profiles["Rivulet"] = new PushToMeowVoiceProfile
            {
                Short = "SlugcatMeowRivuletAShort",
                Long = "SlugcatMeowRivuletA",
                Volume = 0.8
            };
        }

        private void LoadCustomProfiles(string path)
        {
            if (!File.Exists(path)) return;
            Dictionary<string, object> root = new JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
            object raw;
            object[] entries;
            if (root == null || !root.TryGetValue("custom_meows", out raw) ||
                (entries = raw as object[]) == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
                Dictionary<string, object> entry = entries[i] as Dictionary<string, object>;
                if (entry == null) continue;
                string slugcatId = StringValue(entry, "slugcat_id");
                if (string.IsNullOrWhiteSpace(slugcatId)) continue;
                PushToMeowVoiceProfile profile = new PushToMeowVoiceProfile();
                profile.Short = StringValue(entry, "short_meow_soundid");
                profile.Long = StringValue(entry, "long_meow_soundid");
                profile.ShortPup = StringValue(entry, "short_meow_pup_soundid");
                profile.LongPup = StringValue(entry, "long_meow_pup_soundid");
                profile.Volume = NumberValue(entry, "volume_multiplier", 1.0);
                profiles[slugcatId] = profile;
            }
        }

        private static PushToMeowVoiceProfile DefaultNormalProfile()
        {
            return new PushToMeowVoiceProfile
            {
                Short = "SlugcatMeowNormalShort",
                Long = "SlugcatMeowNormal",
                ShortPup = "SlugcatMeowPupShort",
                LongPup = "SlugcatMeowPup"
            };
        }

        private static PushToMeowVoiceProfile Clone(PushToMeowVoiceProfile source)
        {
            return new PushToMeowVoiceProfile
            {
                Short = source.Short,
                Long = source.Long,
                ShortPup = source.ShortPup,
                LongPup = source.LongPup,
                Volume = source.Volume
            };
        }

        private static string OriginalId(SlugcatId slugcat)
        {
            switch (slugcat)
            {
                case SlugcatId.SpearMaster: return "Spear";
                case SlugcatId.White: return "White";
                case SlugcatId.Yellow: return "Yellow";
                case SlugcatId.Red: return "Red";
                case SlugcatId.Gourmand: return "Gourmand";
                case SlugcatId.Artificer: return "Artificer";
                case SlugcatId.Rivulet: return "Rivulet";
                case SlugcatId.Saint: return "Saint";
                default: return "White";
            }
        }

        private static string StringValue(Dictionary<string, object> entry, string key)
        {
            object value;
            return entry.TryGetValue(key, out value) ? value as string : null;
        }

        private static double NumberValue(Dictionary<string, object> entry,
            string key, double fallback)
        {
            object value;
            if (!entry.TryGetValue(key, out value) || value == null) return fallback;
            double result;
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                ? result : fallback;
        }

        private static string ReadVersion(string path)
        {
            if (!File.Exists(path)) return "unknown";
            string value = WorkshopCatalog.ExtractJsonString(File.ReadAllText(path), "version");
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }
    }
}
