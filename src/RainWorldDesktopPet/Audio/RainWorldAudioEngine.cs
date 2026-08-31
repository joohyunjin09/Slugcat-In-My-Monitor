using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Creature;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Audio
{
    public sealed class RainWorldAudioEngine : ISoundEventSink,
        IPushToMeowSource, IDisposable
    {
        private enum CommandKind
        {
            Play, StartLoop, StopLoop, StopSource, StopAll, SetMasterVolume
        }

        private sealed class AudioCommand
        {
            internal CommandKind Kind;
            internal SoundEvent Sound;
            internal string SourceId;
            internal string LoopKey;
            internal double MasterVolume;
        }

        private sealed class CacheEntry
        {
            internal RainWorldPcmClip Clip;
            internal LinkedListNode<string> Node;
        }

        internal const int MaximumQueuedCommands = 512;
        internal const int MaximumVoices = 128;
        internal const int ReservedPriorityVoices = 32;
        internal const int MaximumCommandsPerWorkerCycle = 256;
        internal const int LoopStopFadeMilliseconds = 12;
        private const long MaximumCacheBytes = 24L * 1024L * 1024L;
        internal const double MovementOutputGain = 1.35;
        internal const double LandingOutputGain = 0.35;
        internal const double TerrainImpactOutputGain = 1.65;
        internal const double SpearmasterExtractionOutputGain = 2.6;
        private static readonly string[] HotSoundIds =
        {
            "Slugcat_Step_A", "Slugcat_Step_B", "Slugcat_Crawling_Step",
            "Slugcat_Normal_Jump", "Slugcat_Wall_Jump",
            "Slugcat_Floor_Impact_Standard", "Slugcat_Floor_Impact_Stealthy",
            "Slugcat_Terrain_Impact_Light", "Slugcat_Terrain_Impact_Medium",
            "Slugcat_Terrain_Impact_Hard", "Slugcat_Down_On_Fours",
            "Fire_Spear_Explode", "Bomb_Explode",
            "Slugcat_Throw_Spear", "SM_Spear_Pull", "SM_Spear_Grab",
            "Spear_Bounce_Off_Wall", "Spear_Stick_In_Wall",
            "Spear_Stick_In_Creature",
            "Spear_Bounce_Off_Creauture_Shell", "Spear_Stick_In_Ground",
            "Tube_Worm_Shoot_Tongue", "Tube_Worm_Tongue_Hit_Terrain",
            "Tube_Worm_Detach_Tongue_Terrain"
        };
        private static readonly HashSet<string> MovementSoundIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Slugcat_Step_A", "Slugcat_Step_B", "Slugcat_Crawling_Step",
                "Slugcat_Normal_Jump", "Slugcat_Wall_Jump", "Slugcat_Flip_Jump",
                "Slugcat_Rocket_Jump", "Slugcat_Sectret_Super_Wall_Jump",
                "Slugcat_Down_On_Fours", "Slugcat_Stand_Up",
                "Slugcat_Roll_Init", "Slugcat_Roll_LOOP", "Slugcat_Roll_Finish",
                "Slugcat_Floor_Impact_Standard",
                "Slugcat_Floor_Impact_Stealthy",
                "Slugcat_Terrain_Impact_Light",
                "Slugcat_Terrain_Impact_Medium",
                "Slugcat_Terrain_Impact_Hard"
            };
        private static readonly HashSet<string> TerrainImpactSoundIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Slugcat_Floor_Impact_Standard",
                "Slugcat_Floor_Impact_Stealthy",
                "Slugcat_Terrain_Impact_Light",
                "Slugcat_Terrain_Impact_Medium",
                "Slugcat_Terrain_Impact_Hard"
            };
        private static readonly HashSet<string> LandingSoundIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Slugcat_Floor_Impact_Standard",
                "Slugcat_Floor_Impact_Stealthy"
            };
        private static readonly HashSet<string> SpearmasterExtractionSoundIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SM_Spear_Pull", "SM_Spear_Grab"
            };
        private static readonly HashSet<string> DownpourAbilitySoundIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Artificer blast jump and self-destruction.
                "Fire_Spear_Explode", "Bomb_Explode",
                // Spearmaster needle creation, throw, and realized spear impacts.
                "Slugcat_Throw_Spear", "SM_Spear_Pull", "SM_Spear_Grab",
                "Spear_Bounce_Off_Wall", "Spear_Stick_In_Wall",
                "Spear_Stick_In_Creature",
                "Spear_Bounce_Off_Creauture_Shell", "Spear_Stick_In_Ground",
                // Saint's desktop-terrain tongue states.
                "Tube_Worm_Shoot_Tongue", "Tube_Worm_Tongue_Hit_Terrain",
                "Tube_Worm_Detach_Tongue_Terrain"
            };
        private static readonly HashSet<string> PermittedBaseSoundIds =
            CreatePermittedBaseSoundIds();

        private readonly RainWorldInstallation installation;
        private readonly double listenerCenterX;
        private readonly Vec2 listenerPoint;
        private readonly double listenerHalfWidth;
        private const double ListenerRadius = 1000.0;
        private readonly ConcurrentQueue<AudioCommand> commands =
            new ConcurrentQueue<AudioCommand>();
        private readonly AutoResetEvent commandSignal = new AutoResetEvent(false);
        private readonly object stateSync = new object();
        private readonly Dictionary<string, long> lastPlayed =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CacheEntry> cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> movementLoudnessGains =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> cacheLru = new LinkedList<string>();
        private readonly Dictionary<string, WaveOutVoice> loops =
            new Dictionary<string, WaveOutVoice>(StringComparer.Ordinal);
        private readonly Random random = new Random();
        private Thread worker;
        private RainWorldSoundCatalog catalog;
        private RainWorldSoundBank bank;
        private WaveOutMixer mixer;
        private volatile PushToMeowLibrary pushToMeow;
        private int queuedCount;
        private int droppedRequestCount;
        private volatile int cachedClipCount;
        private long cacheBytes;
        private volatile bool stopping;
        private volatile bool muted;
        private bool disposed;
        private string status;
        private string lastEvent = "none";
        private double masterVolume = AudioVolumeSettings.Default;
        private double movementReferenceRms;
        private int normalizedMovementClipCount;

        public RainWorldAudioEngine(RainWorldInstallation installation,
            Rectangle virtualDesktopBounds)
        {
            this.installation = installation;
            listenerCenterX = virtualDesktopBounds.Left + virtualDesktopBounds.Width * 0.5;
            listenerPoint = DesktopWorldTransform.ToSimulation(new Vec2(
                listenerCenterX,
                virtualDesktopBounds.Top + virtualDesktopBounds.Height * 0.5));
            listenerHalfWidth = Math.Max(1.0, virtualDesktopBounds.Width * 0.5);
            if (installation == null)
            {
                status = "audio disabled: Rain World installation unavailable";
                return;
            }
            status = "audio: indexing installed Rain World sound bank in background";
            worker = new Thread(WorkerMain);
            worker.IsBackground = true;
            worker.Name = "Rain World audio";
            worker.Start();
        }

        public string Status
        {
            get
            {
                lock (stateSync)
                    return status + (muted ? "; muted" : string.Empty);
            }
        }

        public string LastEvent
        {
            get { lock (stateSync) return lastEvent; }
        }

        public int CachedClipCount { get { return cachedClipCount; } }
        public long CachedBytes { get { return Interlocked.Read(ref cacheBytes); } }
        public int DroppedRequestCount { get { return Thread.VolatileRead(ref droppedRequestCount); } }
        public int OutputDeviceCount
        {
            get { WaveOutMixer current = mixer; return current == null ? 0 : current.DeviceCount; }
        }
        public long RenderedAudioBufferCount
        {
            get
            {
                WaveOutMixer current = mixer;
                return current == null ? 0L : current.RenderedBufferCount;
            }
        }
        public int PeakActiveVoiceCount
        {
            get
            {
                WaveOutMixer current = mixer;
                return current == null ? 0 : current.PeakActiveVoiceCount;
            }
        }
        public double MaximumMixerRenderMilliseconds
        {
            get
            {
                WaveOutMixer current = mixer;
                return current == null ? 0.0 :
                    current.MaximumRenderMilliseconds;
            }
        }
        public bool Muted { get { return muted; } }
        public bool PushToMeowAvailable { get { return pushToMeow != null; } }
        public double MasterVolume
        {
            get { lock (stateSync) return masterVolume; }
        }

        public void SetMuted(bool value)
        {
            if (muted == value) return;
            muted = value;
            if (value && worker != null && !stopping)
                EnqueuePriority(new AudioCommand { Kind = CommandKind.StopAll });
        }

        public void SetMasterVolume(double value)
        {
            value = AudioVolumeSettings.Clamp(value);
            lock (stateSync)
            {
                if (Math.Abs(masterVolume - value) < 0.000001) return;
                masterVolume = value;
            }
            if (worker != null && !stopping)
                EnqueuePriority(new AudioCommand
                {
                    Kind = CommandKind.SetMasterVolume,
                    MasterVolume = value
                });
        }

        public bool TryResolveMeow(SlugcatId slugcat, bool pup,
            bool shortMeow, out PushToMeowSound sound)
        {
            PushToMeowLibrary library = pushToMeow;
            if (library == null)
            {
                sound = null;
                return false;
            }
            return library.TryResolve(slugcat, pup, shortMeow, out sound);
        }

        public static bool IsPermittedCharacterSound(string soundId)
        {
            if (string.IsNullOrWhiteSpace(soundId)) return false;
            return PermittedBaseSoundIds.Contains(soundId) ||
                soundId.StartsWith("SlugcatMeow", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> CreatePermittedBaseSoundIds()
        {
            HashSet<string> result = new HashSet<string>(MovementSoundIds,
                StringComparer.OrdinalIgnoreCase);
            result.UnionWith(DownpourAbilitySoundIds);
            return result;
        }

        public void Play(SoundEvent sound)
        {
            if (sound == null || worker == null || stopping || muted ||
                !IsPermittedCharacterSound(sound.Id)) return;
            string cooldownKey = sound.SourceId + "\n" + sound.Id;
            lock (stateSync)
            {
                long previous;
                if (lastPlayed.TryGetValue(cooldownKey, out previous) &&
                    sound.SimulationTick - previous < sound.CooldownTicks) return;
                lastPlayed[cooldownKey] = sound.SimulationTick;
            }
            AudioCommand command = new AudioCommand
                { Kind = CommandKind.Play, Sound = sound };
            bool accepted = IsPrioritySound(sound.Id)
                ? EnqueuePriority(command)
                : Enqueue(command);
            if (!accepted)
            {
                // A rejected command never reached the device. Do not let it
                // suppress the next real footstep or action through cooldown.
                lock (stateSync)
                {
                    long recorded;
                    if (lastPlayed.TryGetValue(cooldownKey, out recorded) &&
                        recorded == sound.SimulationTick)
                        lastPlayed.Remove(cooldownKey);
                }
            }
        }

        public void StartLoop(SoundEvent sound, string loopKey)
        {
            if (sound == null || string.IsNullOrEmpty(loopKey) || worker == null ||
                stopping || muted || !IsPermittedCharacterSound(sound.Id)) return;
            Enqueue(new AudioCommand { Kind = CommandKind.StartLoop,
                Sound = sound, LoopKey = loopKey });
        }

        public void StopLoop(string sourceId, string loopKey)
        {
            if (string.IsNullOrEmpty(loopKey) || worker == null || stopping) return;
                EnqueuePriority(new AudioCommand { Kind = CommandKind.StopLoop,
                SourceId = sourceId ?? string.Empty, LoopKey = loopKey });
        }

        public void StopSource(string sourceId)
        {
            if (worker == null || stopping) return;
            EnqueuePriority(new AudioCommand { Kind = CommandKind.StopSource,
                SourceId = sourceId ?? string.Empty });
        }

        private bool Enqueue(AudioCommand command)
        {
            if (Interlocked.Increment(ref queuedCount) > MaximumQueuedCommands)
            {
                Interlocked.Decrement(ref queuedCount);
                Interlocked.Increment(ref droppedRequestCount);
                return false;
            }
            commands.Enqueue(command);
            commandSignal.Set();
            return true;
        }

        private bool EnqueuePriority(AudioCommand command)
        {
            Interlocked.Increment(ref queuedCount);
            commands.Enqueue(command);
            commandSignal.Set();
            return true;
        }

        private void WorkerMain()
        {
            try
            {
                string soundsPath = Path.Combine(installation.StreamingAssetsPath,
                    "soundeffects", "sounds.txt");
                catalog = RainWorldSoundCatalog.Load(soundsPath,
                    PermittedBaseSoundIds);
                bank = new RainWorldSoundBank(installation,
                    catalog.ReferencedClipNames());
                BuildMovementLoudnessProfile();
                PreloadHotSounds();
                string meowReason;
                PushToMeowLibrary meow = PushToMeowLibrary.TryLoad(
                    installation, out meowReason);
                if (meow != null)
                {
                    int preparedMeowClips = PreloadPushToMeowSounds(meow);
                    pushToMeow = meow;
                    meow.PreparedClipCount = preparedMeowClips;
                }
                bank.TrimReadCache();
                mixer = new WaveOutMixer(MasterVolume);
                SetStatus("audio ready: " + catalog.Count +
                    " permitted SoundIDs, " + bank.ClipCount +
                    " matching Rain World clips indexed; cache " +
                    CacheMegabytes() + " MiB / 24 MiB" +
                    "; movement loudness normalized across " +
                    normalizedMovementClipCount + " sound variants" +
                    (meow == null
                        ? "; " + meowReason
                        : "; Push To Meow " + meow.Version + " " +
                            (meow.IsActive ? "active" : "installed") + ", " +
                            meow.PreparedClipCount + "/" + meow.LooseClipCount +
                            " permitted meow clips prepared") +
                    "; single-device 48 kHz software mixer");
            }
            catch (Exception exception)
            {
                SetStatus("audio unavailable: " + exception.Message);
            }

            while (!stopping)
            {
                AudioCommand command;
                int processed = 0;
                while (processed < MaximumCommandsPerWorkerCycle &&
                    commands.TryDequeue(out command))
                {
                    Interlocked.Decrement(ref queuedCount);
                    ProcessCommand(command);
                    processed++;
                }
                if (processed == 0)
                    commandSignal.WaitOne(15);
            }
            AudioCommand ignored;
            while (commands.TryDequeue(out ignored)) Interlocked.Decrement(ref queuedCount);
            StopAllVoices();
            if (mixer != null) mixer.Dispose();
            mixer = null;
            cache.Clear();
            cacheLru.Clear();
            cachedClipCount = 0;
            Interlocked.Exchange(ref cacheBytes, 0L);
            if (bank != null) bank.Dispose();
            bank = null;
        }

        private void ProcessCommand(AudioCommand command)
        {
            try
            {
                if (command.Kind == CommandKind.Play && !muted)
                    PlayDefinition(command.Sound, null, false);
                else if (command.Kind == CommandKind.StartLoop && !muted)
                    PlayDefinition(command.Sound,
                        LoopIdentity(command.Sound.SourceId, command.LoopKey), true);
                else if (command.Kind == CommandKind.StopLoop)
                    StopLoopWorker(LoopIdentity(command.SourceId, command.LoopKey));
                else if (command.Kind == CommandKind.StopSource)
                    StopSourceWorker(command.SourceId);
                else if (command.Kind == CommandKind.StopAll)
                    StopAllVoices();
                else if (command.Kind == CommandKind.SetMasterVolume)
                    SetVoiceMasterVolume(command.MasterVolume);
            }
            catch (Exception exception)
            {
                SetStatus("audio playback error: " + exception.Message);
            }
        }

        private void PlayDefinition(SoundEvent sound, string loopIdentity, bool loop)
        {
            if (catalog == null || bank == null || mixer == null) return;
            if (loop && loops.ContainsKey(loopIdentity)) return;
            RainWorldSoundDefinition definition;
            PushToMeowLibrary meow = pushToMeow;
            if ((meow == null || !meow.Sounds.TryGet(sound.Id, out definition)) &&
                !catalog.TryGet(sound.Id, out definition)) return;
            if (definition.Clips.Count == 0) return;
            if (definition.SilentChance > 0.0 &&
                random.NextDouble() < definition.SilentChance) return;

            int first = definition.PlayAll ? 0 : random.Next(definition.Clips.Count);
            int count = definition.PlayAll ? definition.Clips.Count : 1;
            bool priorityEvent = IsPrioritySound(sound.Id);
            int requestedVoices = loop ? 1 : count;
            // PLAYALL is one authored event. Reserve every layer before the
            // first header starts so a saturated mix cannot truncate it.
            if (!MakeVoiceRoom(priorityEvent, requestedVoices))
            {
                Interlocked.Increment(ref droppedRequestCount);
                return;
            }
            double spatialVolume = CalculateSpatialGain(sound.Position,
                listenerPoint, ListenerRadius, definition.RangeFactor);
            double dopplerPitch = CalculateDopplerPitch(sound.Position,
                sound.Velocity, listenerPoint, definition.DopplerFactor);
            for (int i = 0; i < count; i++)
            {
                RainWorldSoundClipChoice choice = definition.Clips[(first + i) % definition.Clips.Count];
                RainWorldPcmClip clip = ResolveClip(choice.Name);
                if (clip == null) continue;
                double triggerVolume = definition.ChooseVolume(random);
                double triggerPitch = definition.ChoosePitch(random);
                double volume = CalculateOutputVolume(sound.Volume,
                    triggerVolume, choice.ChooseVolume(random), spatialVolume,
                    catalog.MasterVolume, catalog.VolumeExponent);
                volume = ApplyCategoryOutputGain(sound.Id, volume);
                volume = ApplyMovementLoudnessNormalization(
                    sound.Id, clip.Name, volume);
                volume = ApplyEventOutputGain(sound.Id, volume);
                double pitch = MathUtil.Clamp(sound.Pitch * triggerPitch *
                    choice.ChoosePitch(random) * dopplerPitch, 0.25, 4.0);
                double desktopX = DesktopWorldTransform.ToDesktop(sound.Position).X;
                double pan = MathUtil.Clamp((desktopX - listenerCenterX) /
                    listenerHalfWidth, -1.0, 1.0);
                WaveOutVoice voice = new WaveOutVoice(clip, volume, pan, pitch,
                    loop, WaveOutMixer.OutputSampleRate);
                mixer.AddVoice(voice);
                if (loop)
                {
                    loops[loopIdentity] = voice;
                    break;
                }
            }
            lock (stateSync)
            {
                lastEvent = sound.Id + (loop ? " loop" : string.Empty);
            }
        }

        internal static double CalculateSpatialGain(Vec2 position,
            Vec2 listener, double listenerRadius, double rangeFactor)
        {
            if (listenerRadius <= 0.0 || rangeFactor <= 0.0) return 0.0;
            double scaledDistance = Vec2.Distance(position, listener) / rangeFactor;
            double near = listenerRadius * 0.5;
            double far = listenerRadius * 2.0;
            return MathUtil.Clamp01((far - scaledDistance) / (far - near));
        }

        internal static double CalculateDopplerPitch(Vec2 position, Vec2 velocity,
            Vec2 listener, double dopplerFactor)
        {
            if (dopplerFactor <= 0.0) return 1.0;
            Vec2 outward = position - listener;
            double distance = outward.Length;
            if (distance <= 0.000001) return 1.0;
            double radialVelocityPerTick =
                (velocity.X * outward.X + velocity.Y * outward.Y) / distance;
            double approachPerSecond = -radialVelocityPerTick *
                SimulationConstants.LogicTicksPerSecond;
            // VirtualMicrophone fixes dopplerBlock at 0.5 in this runtime.
            return MathUtil.Clamp(1.0 + approachPerSecond / 4000.0 *
                dopplerFactor * 0.5, 0.1, 2.0);
        }

        internal static double CalculateOutputVolume(double eventVolume,
            double triggerVolume, double clipVolume, double spatialVolume,
            double masterVolume, double volumeExponent)
        {
            double linear = Math.Max(0.0, eventVolume * triggerVolume *
                clipVolume * spatialVolume);
            double shaped = Math.Pow(linear, Math.Max(0.01, volumeExponent));
            // AudioListener.volume is documented by Unity as a 0..1 control.
            return MathUtil.Clamp01(shaped * MathUtil.Clamp01(masterVolume));
        }

        internal static double ApplyCategoryOutputGain(string soundId,
            double outputVolume)
        {
            double gain = MovementSoundIds.Contains(soundId)
                ? MovementOutputGain : 1.0;
            return MathUtil.Clamp01(outputVolume * gain);
        }

        internal static double ApplyMasterOutputGain(double outputVolume,
            double masterGain)
        {
            return MathUtil.Clamp01(Math.Max(0.0, outputVolume) *
                AudioVolumeSettings.Clamp(masterGain));
        }

        internal static bool IsTerrainImpactSound(string soundId)
        {
            return !string.IsNullOrEmpty(soundId) &&
                TerrainImpactSoundIds.Contains(soundId);
        }

        internal static bool IsPrioritySound(string soundId)
        {
            return IsTerrainImpactSound(soundId) ||
                IsMeowSound(soundId) ||
                (!string.IsNullOrEmpty(soundId) &&
                 DownpourAbilitySoundIds.Contains(soundId));
        }

        internal static bool IsMeowSound(string soundId)
        {
            return !string.IsNullOrEmpty(soundId) &&
                soundId.StartsWith("SlugcatMeow",
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static double ApplyEventOutputGain(string soundId,
            double outputVolume)
        {
            double gain;
            if (LandingSoundIds.Contains(soundId ?? string.Empty))
                gain = LandingOutputGain;
            else if (TerrainImpactSoundIds.Contains(soundId ?? string.Empty))
                gain = TerrainImpactOutputGain;
            else if (SpearmasterExtractionSoundIds.Contains(soundId ?? string.Empty))
                gain = SpearmasterExtractionOutputGain;
            else
                gain = 1.0;
            return MathUtil.Clamp01(Math.Max(0.0, outputVolume) * gain);
        }

        internal static double CalculatePcmRms(RainWorldPcmClip clip)
        {
            if (clip == null || clip.Data == null || clip.BitsPerSample != 16 ||
                clip.DataOffset < 0 || clip.DataLength < 2 ||
                clip.DataOffset >= clip.Data.Length) return 0.0;
            int end = (int)Math.Min(clip.Data.Length,
                (long)clip.DataOffset + clip.DataLength);
            double sumSquares = 0.0;
            int sampleCount = 0;
            for (int offset = clip.DataOffset; offset + 1 < end; offset += 2)
            {
                short sample = unchecked((short)(clip.Data[offset] |
                    (clip.Data[offset + 1] << 8)));
                double normalized = sample / 32768.0;
                sumSquares += normalized * normalized;
                sampleCount++;
            }
            return sampleCount == 0 ? 0.0 : Math.Sqrt(sumSquares / sampleCount);
        }

        internal static double CalculateLoudnessNormalizationGain(
            double clipRms, double referenceRms)
        {
            if (clipRms <= 0.0001 || referenceRms <= 0.0001) return 1.0;
            return MathUtil.Clamp(referenceRms / clipRms, 0.25, 4.0);
        }

        private double ApplyMovementLoudnessNormalization(string soundId,
            string clipName, double outputVolume)
        {
            if (!MovementSoundIds.Contains(soundId)) return outputVolume;
            double gain;
            if (!movementLoudnessGains.TryGetValue(
                LoudnessIdentity(soundId, clipName),
                out gain)) return outputVolume;
            return MathUtil.Clamp01(outputVolume * gain);
        }

        private static string LoudnessIdentity(string soundId, string clipName)
        {
            return (soundId ?? string.Empty) + "\n" +
                (clipName ?? string.Empty);
        }

        private RainWorldPcmClip ResolveClip(string requestedName)
        {
            PushToMeowLibrary meow = pushToMeow;
            string loosePath;
            if (meow != null && meow.TryGetLoosePath(requestedName, out loosePath))
            {
                CacheEntry looseEntry;
                if (cache.TryGetValue(requestedName, out looseEntry))
                {
                    Touch(looseEntry);
                    return looseEntry.Clip;
                }
                RainWorldPcmClip looseClip;
                string looseReason;
                if (!WavPcmReader.TryLoad(loosePath, requestedName,
                    out looseClip, out looseReason)) return null;
                AddToCache(looseClip);
                return looseClip;
            }
            IList<string> names = bank.ResolveClipNames(requestedName);
            if (names.Count == 0) return null;
            string name = names[random.Next(names.Count)];
            CacheEntry entry;
            if (cache.TryGetValue(name, out entry))
            {
                Touch(entry);
                return entry.Clip;
            }
            RainWorldPcmClip clip;
            string reason;
            if (!bank.TryLoadPcm(name, out clip, out reason))
            {
                bank.TrimReadCache();
                return null;
            }
            AddToCache(clip);
            bank.TrimReadCache();
            return clip;
        }

        private void PreloadHotSounds()
        {
            for (int idIndex = 0; idIndex < HotSoundIds.Length; idIndex++)
            {
                RainWorldSoundDefinition definition;
                if (!catalog.TryGet(HotSoundIds[idIndex], out definition)) continue;
                for (int choiceIndex = 0; choiceIndex < definition.Clips.Count; choiceIndex++)
                {
                    IList<string> names = bank.ResolveClipNames(
                        definition.Clips[choiceIndex].Name);
                    for (int nameIndex = 0; nameIndex < names.Count; nameIndex++)
                    {
                        if (cache.ContainsKey(names[nameIndex])) continue;
                        RainWorldPcmClip clip;
                        string reason;
                        if (bank.TryLoadPcm(names[nameIndex], out clip, out reason))
                            AddToCache(clip);
                    }
                }
            }
        }

        private void BuildMovementLoudnessProfile()
        {
            Dictionary<string, double> measured =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, double> clipRms =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double logSum = 0.0;
            int count = 0;
            foreach (string soundId in MovementSoundIds)
            {
                RainWorldSoundDefinition definition;
                if (!catalog.TryGet(soundId, out definition)) continue;
                for (int choiceIndex = 0;
                    choiceIndex < definition.Clips.Count; choiceIndex++)
                {
                    IList<string> names = bank.ResolveClipNames(
                        definition.Clips[choiceIndex].Name);
                    for (int nameIndex = 0; nameIndex < names.Count; nameIndex++)
                    {
                        string name = names[nameIndex];
                        double rms;
                        if (!clipRms.TryGetValue(name, out rms))
                        {
                            RainWorldPcmClip clip;
                            CacheEntry cached;
                            if (cache.TryGetValue(name, out cached))
                            {
                                clip = cached.Clip;
                                Touch(cached);
                            }
                            else
                            {
                                string reason;
                                if (!bank.TryLoadPcm(name, out clip, out reason)) continue;
                                AddToCache(clip);
                            }
                            rms = CalculatePcmRms(clip);
                            if (rms <= 0.0001) continue;
                            clipRms.Add(name, rms);
                        }

                        string identity = LoudnessIdentity(soundId, name);
                        if (measured.ContainsKey(identity)) continue;
                        double triggerVolume = (definition.MinimumVolume +
                            definition.MaximumVolume) * 0.5;
                        RainWorldSoundClipChoice choice =
                            definition.Clips[choiceIndex];
                        double clipVolume = (choice.MinimumVolume +
                            choice.MaximumVolume) * 0.5;
                        double nominalOutput = CalculateOutputVolume(
                            1.0, triggerVolume, clipVolume, 1.0,
                            catalog.MasterVolume, catalog.VolumeExponent);
                        nominalOutput = ApplyCategoryOutputGain(
                            soundId, nominalOutput);
                        double renderedRms = rms * nominalOutput;
                        if (renderedRms <= 0.0001) continue;
                        measured.Add(identity, renderedRms);
                        logSum += Math.Log(renderedRms);
                        count++;
                    }
                }
            }
            movementReferenceRms = count == 0 ? 0.0 : Math.Exp(logSum / count);
            foreach (KeyValuePair<string, double> pair in measured)
                movementLoudnessGains[pair.Key] =
                    CalculateLoudnessNormalizationGain(
                        pair.Value, movementReferenceRms);
            normalizedMovementClipCount = movementLoudnessGains.Count;
            bank.TrimReadCache();
        }

        private int PreloadPushToMeowSounds(PushToMeowLibrary library)
        {
            int prepared = 0;
            foreach (string name in library.RelevantClipNames())
            {
                string path;
                if (!library.TryGetLoosePath(name, out path)) continue;
                if (cache.ContainsKey(name))
                {
                    prepared++;
                    continue;
                }
                RainWorldPcmClip clip;
                string reason;
                if (WavPcmReader.TryLoad(path, name, out clip, out reason))
                {
                    AddToCache(clip);
                    if (cache.ContainsKey(name)) prepared++;
                }
            }
            return prepared;
        }

        private void AddToCache(RainWorldPcmClip clip)
        {
            if (clip == null || clip.MemoryBytes <= 0 || clip.MemoryBytes > MaximumCacheBytes) return;
            CacheEntry existing;
            if (cache.TryGetValue(clip.Name, out existing))
            {
                Touch(existing);
                return;
            }
            CacheEntry entry = new CacheEntry();
            entry.Clip = clip;
            entry.Node = cacheLru.AddLast(clip.Name);
            cache.Add(clip.Name, entry);
            cachedClipCount = cache.Count;
            Interlocked.Add(ref cacheBytes, clip.MemoryBytes);
            while (cacheBytes > MaximumCacheBytes && cacheLru.First != null)
            {
                string expired = cacheLru.First.Value;
                CacheEntry removed = cache[expired];
                cacheLru.RemoveFirst();
                cache.Remove(expired);
                cachedClipCount = cache.Count;
                Interlocked.Add(ref cacheBytes, -removed.Clip.MemoryBytes);
            }
        }

        private void Touch(CacheEntry entry)
        {
            cacheLru.Remove(entry.Node);
            cacheLru.AddLast(entry.Node);
        }

        internal static bool CanAdmitVoices(int activeVoices,
            int requestedVoices, bool priorityEvent)
        {
            if (requestedVoices <= 0) return true;
            int admissionLimit = priorityEvent ? MaximumVoices :
                MaximumVoices - ReservedPriorityVoices;
            return activeVoices >= 0 &&
                activeVoices <= admissionLimit - requestedVoices;
        }

        private bool MakeVoiceRoom(bool priorityEvent, int requestedVoices)
        {
            WaveOutMixer current = mixer;
            if (current == null || current.DeviceCount != 1) return false;
            string mixerError = current.LastError;
            if (!string.IsNullOrEmpty(mixerError))
            {
                SetStatus("audio playback error: " + mixerError);
                return false;
            }
            // Cached PCM bytes are shared. Voices contain only playback cursors
            // mixed through one fixed device, so the larger eight-pet burst
            // bound does not multiply device handles or PCM storage.
            return CanAdmitVoices(current.ActiveVoiceCount,
                requestedVoices, priorityEvent);
        }

        private void StopLoopWorker(string identity)
        {
            WaveOutVoice voice;
            if (!loops.TryGetValue(identity, out voice)) return;
            loops.Remove(identity);
            // The shared mixer applies the 12 ms envelope sample by sample.
            if (mixer != null) mixer.BeginStop(voice, LoopStopFadeMilliseconds);
        }

        private void StopSourceWorker(string sourceId)
        {
            string prefix = (sourceId ?? string.Empty) + "\n";
            List<string> found = new List<string>();
            foreach (string key in loops.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) found.Add(key);
            for (int i = 0; i < found.Count; i++) StopLoopWorker(found[i]);
        }

        private void SetVoiceMasterVolume(double value)
        {
            if (mixer != null) mixer.SetMasterGain(value);
        }

        private void StopAllVoices()
        {
            if (mixer != null) mixer.StopAll();
            loops.Clear();
        }

        private static string LoopIdentity(string sourceId, string loopKey)
        {
            return (sourceId ?? string.Empty) + "\n" + (loopKey ?? string.Empty);
        }

        private string CacheMegabytes()
        {
            return (cacheBytes / (1024.0 * 1024.0)).ToString("0.0",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private void SetStatus(string value)
        {
            lock (stateSync) status = value;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stopping = true;
            commandSignal.Set();
            bool stopped = worker == null || worker.Join(3000);
            if (!stopped)
                SetStatus("audio shutdown is still completing in background");
            worker = null;
            if (stopped) commandSignal.Dispose();
        }
    }
}
