using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Audio
{
    public sealed class RainWorldSoundDefinition
    {
        public RainWorldSoundDefinition(string id, bool playAll,
            params RainWorldSoundClipDefinition[] clips)
        {
            Id = id;
            PlayAll = playAll;
            Clips = clips ?? new RainWorldSoundClipDefinition[0];
        }

        public readonly string Id;
        public readonly bool PlayAll;
        public readonly RainWorldSoundClipDefinition[] Clips;
    }

    public sealed class RainWorldSoundClipDefinition
    {
        public RainWorldSoundClipDefinition(string name)
        {
            Name = name;
            MinimumVolume = MaximumVolume = 1.0;
            MinimumPitch = MaximumPitch = 1.0;
        }

        public readonly string Name;
        public double MinimumVolume;
        public double MaximumVolume;
        public double MinimumPitch;
        public double MaximumPitch;
    }

    public static class RainWorldSoundCatalog
    {
        private static readonly Dictionary<string, RainWorldSoundDefinition> fallback =
            new Dictionary<string, RainWorldSoundDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                { "Slugcat_Step_A", Single("Slugcat_Step_A", "walk3A") },
                { "Slugcat_Step_B", Single("Slugcat_Step_B", "walk3B") }
            };

        private static RainWorldSoundDefinition Single(string id, string clip)
        {
            return new RainWorldSoundDefinition(id, false,
                new RainWorldSoundClipDefinition(clip));
        }

        public static IDictionary<string, RainWorldSoundDefinition> Load(string path)
        {
            Dictionary<string, RainWorldSoundDefinition> result =
                new Dictionary<string, RainWorldSoundDefinition>(fallback,
                    StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;
                int colon = line.IndexOf(':');
                if (colon <= 0 || colon >= line.Length - 1) continue;
                string[] eventParts = line.Substring(0, colon).Trim().Split('/');
                string id = eventParts[0].Trim();
                if (id.Length == 0 || id.IndexOf(' ') >= 0) continue;
                bool playAll = false;
                for (int i = 1; i < eventParts.Length; i++)
                    if (string.Equals(eventParts[i].Trim(), "PLAYALL",
                        StringComparison.OrdinalIgnoreCase)) playAll = true;

                string[] encodedClips = line.Substring(colon + 1).Split(',');
                List<RainWorldSoundClipDefinition> clips =
                    new List<RainWorldSoundClipDefinition>();
                for (int i = 0; i < encodedClips.Length; i++)
                {
                    string[] parts = encodedClips[i].Trim().Split('/');
                    if (parts.Length == 0 || parts[0].Trim().Length == 0) continue;
                    RainWorldSoundClipDefinition clip =
                        new RainWorldSoundClipDefinition(parts[0].Trim());
                    for (int part = 1; part < parts.Length; part++)
                    {
                        int equals = parts[part].IndexOf('=');
                        if (equals <= 0) continue;
                        string key = parts[part].Substring(0, equals).Trim();
                        double value;
                        if (!double.TryParse(parts[part].Substring(equals + 1).Trim(),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out value)) continue;
                        if (key == "vol") clip.MinimumVolume = clip.MaximumVolume = value;
                        else if (key == "minVol") clip.MinimumVolume = value;
                        else if (key == "maxVol") clip.MaximumVolume = value;
                        else if (key == "pitch") clip.MinimumPitch = clip.MaximumPitch = value;
                        else if (key == "minPitch") clip.MinimumPitch = value;
                        else if (key == "maxPitch") clip.MaximumPitch = value;
                    }
                    clips.Add(clip);
                }
                if (clips.Count > 0)
                    result[id] = new RainWorldSoundDefinition(id, playAll, clips.ToArray());
            }
            return result;
        }
    }

    // Fixed-tick event gate backed by Rain World's own sounds.txt and UnityFS
    // sound bank. Disk reads, PCM extraction and SoundPlayer.Load all stay on
    // the dedicated worker so a first-time sound can never stall simulation.
    public sealed class RainWorldAudioEngine : IDisposable
    {
        private sealed class QueuedClip
        {
            public UnityAudioClipInfo Clip;
            public string LoosePath;
            public string ClipName;
            public SoundEvent Sound;
            public double Volume;
            public double Pitch;
            public double Pan;
            public bool Loop;
        }

        private sealed class PcmCacheEntry
        {
            public byte[] Data;
            public int Channels;
            public int Frequency;
        }

        private sealed class ActiveVoice : IDisposable
        {
            public readonly MemoryStream Stream;
            public readonly SoundPlayer Player;
            public ActiveVoice(byte[] wave)
            {
                Stream = new MemoryStream(wave, false);
                Player = new SoundPlayer(Stream);
                Player.Load();
            }
            public void Dispose()
            {
                try { Player.Stop(); }
                catch { }
                Player.Dispose();
                Stream.Dispose();
            }
        }

        private readonly string looseSoundDirectory;
        private readonly Dictionary<string, long> lastPlayed =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ActiveVoice> activePlayers = new List<ActiveVoice>();
        private readonly Dictionary<string, ActiveVoice> activeLoops =
            new Dictionary<string, ActiveVoice>(StringComparer.Ordinal);
        private readonly Dictionary<string, UnityAudioClipInfo> clips =
            new Dictionary<string, UnityAudioClipInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<UnityAudioClipInfo>> clipFamilies =
            new Dictionary<string, List<UnityAudioClipInfo>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> looseClips =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PcmCacheEntry> pcmCache =
            new Dictionary<string, PcmCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> pcmCacheOrder = new Queue<string>();
        private readonly Dictionary<string, byte[]> looseWaveCache =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<QueuedClip> queuedClips = new Queue<QueuedClip>();
        private readonly HashSet<string> pendingLoopKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> cancelledLoopKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly object audioSync = new object();
        private readonly AutoResetEvent queueSignal = new AutoResetEvent(false);
        private IDictionary<string, RainWorldSoundDefinition> soundDefinitions =
            new Dictionary<string, RainWorldSoundDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Random random = new Random(0x50A0D);
        private UnityFsBundleReader soundBundle;
        private Thread audioWorker;
        private volatile bool stopping;
        private int variantCounter;
        private string lastEvent = "none";
        private volatile bool enabled = true;

        public RainWorldAudioEngine(RainWorldInstallation installation)
        {
            if (installation == null || string.IsNullOrEmpty(installation.RootPath))
            {
                Status = "audio disabled: Rain World installation unavailable";
                return;
            }
            looseSoundDirectory = Path.Combine(installation.RootPath,
                "RainWorld_Data", "StreamingAssets", "loadedsoundeffects");
            CacheLooseSounds(looseSoundDirectory);
            string map = Path.Combine(installation.RootPath,
                "RainWorld_Data", "StreamingAssets", "soundeffects", "sounds.txt");
            if (!File.Exists(map))
            {
                Status = "audio disabled: local sounds.txt missing";
                return;
            }
            soundDefinitions = RainWorldSoundCatalog.Load(map);
            string bundlePath = Path.Combine(installation.StreamingAssetsPath,
                "AssetBundles", "loadedsoundeffects");
            try
            {
                soundBundle = new UnityFsBundleReader(bundlePath);
                UnityFsNodeInfo serializedNode = soundBundle.FindSerializedFileNode();
                using (UnitySerializedFileReader serialized = new UnitySerializedFileReader(
                    soundBundle.ReadNode(serializedNode), serializedNode.Path))
                {
                    IList<UnityAudioClipInfo> allClips = serialized.ReadAudioClips();
                    for (int i = 0; i < allClips.Count; i++)
                    {
                        clips[allClips[i].Name] = allClips[i];
                        AddClipFamily(allClips[i]);
                    }
                }
                Status = "local sounds.txt (" + soundDefinitions.Count +
                    " SoundIDs) + UnityFS sound bank ready (" + clips.Count + " clips)";
            }
            catch (Exception exception)
            {
                if (soundBundle != null) soundBundle.Dispose();
                soundBundle = null;
                Status = "local sounds.txt mapped; UnityFS audio unavailable: " + exception.Message;
                LogDiagnostic("[Audio] UnityFS initialization failed: " + exception);
            }
            audioWorker = new Thread(AudioWorkerMain);
            audioWorker.IsBackground = true;
            audioWorker.Name = "Rain World audio decode/playback";
            audioWorker.Start();
        }

        public string Status { get; private set; }
        public string LastEvent { get { return lastEvent; } }
        public bool Enabled { get { return enabled; } }

        internal bool TryResolveAndDecodeForDiagnostics(string requested,
            out string resolved, out int pcmBytes, out string reason)
        {
            resolved = null;
            pcmBytes = 0;
            if (soundBundle == null)
            {
                reason = "UnityFS sound bank unavailable";
                return false;
            }
            UnityAudioClipInfo clip;
            if (!TryResolveClip(requested, out clip))
            {
                reason = "no exact or indexed family match";
                return false;
            }
            resolved = clip.Name;
            byte[] pcm;
            if (!TryReadPcm(clip, out pcm, out reason)) return false;
            pcmBytes = pcm.Length;
            return true;
        }

        public void SetEnabled(bool value)
        {
            if (enabled == value) return;
            enabled = value;
            if (!enabled)
            {
                StopAllVoices();
                lastEvent = "sound disabled";
            }
            else lastEvent = "sound enabled";
        }

        public void Play(SoundEvent sound, Vec2 listener, long simulationTick,
            double audibleRange)
        {
            if (sound == null) return;
            // Events are intentionally consumed while disabled. Re-enabling
            // starts with the next event and never replays a stale effect.
            if (!enabled) return;
            if (sound.StopLoop)
            {
                StopLoopVoice(sound.LoopKey);
                lastEvent = "stop loop " + sound.Id;
                return;
            }
            if (sound.Loop && !string.IsNullOrEmpty(sound.LoopKey) &&
                IsLoopActiveOrPending(sound.LoopKey)) return;
            long previous;
            if (lastPlayed.TryGetValue(sound.Id, out previous) &&
                simulationTick - previous < sound.CooldownTicks) return;
            lastPlayed[sound.Id] = simulationTick;
            double pan = MathUtil.Clamp((sound.Position.X - listener.X) /
                Math.Max(1.0, audibleRange), -1.0, 1.0);
            lastEvent = string.Format("{0} volume:{1:0.00} pitch:{2:0.00} pan:{3:0.00}",
                sound.Id, sound.Volume, sound.Pitch, pan);

            RainWorldSoundDefinition definition;
            soundDefinitions.TryGetValue(sound.Id ?? string.Empty, out definition);
            if (definition == null || definition.Clips.Length == 0 ||
                string.IsNullOrEmpty(looseSoundDirectory))
            {
                ReportUnavailable(sound.Id, "SoundID has no active sounds.txt mapping");
                return;
            }
            if (sound.Loop)
            {
                RainWorldSoundClipDefinition loopClip = definition.Clips[
                    variantCounter++ % definition.Clips.Length];
                PlayClip(loopClip, sound, pan, true);
                return;
            }
            if (definition.PlayAll)
            {
                for (int i = 0; i < definition.Clips.Length; i++)
                    PlayClip(definition.Clips[i], sound, pan, false);
            }
            else
            {
                RainWorldSoundClipDefinition clip = definition.Clips[
                    variantCounter++ % definition.Clips.Length];
                PlayClip(clip, sound, pan, false);
            }
        }

        private void PlayClip(RainWorldSoundClipDefinition definition, SoundEvent sound,
            double pan, bool loop)
        {
            string clip = definition.Name;
            double clipVolume = MathUtil.Lerp(definition.MinimumVolume,
                definition.MaximumVolume, random.NextDouble()) * sound.Volume;
            double clipPitch = MathUtil.Lerp(definition.MinimumPitch,
                definition.MaximumPitch, random.NextDouble()) * sound.Pitch;
            UnityAudioClipInfo clipInfo;
            if (soundBundle != null && TryResolveClip(clip, out clipInfo))
            {
                QueueClip(new QueuedClip
                {
                    Clip = clipInfo,
                    ClipName = clipInfo.Name,
                    Sound = sound,
                    Volume = clipVolume,
                    Pitch = clipPitch,
                    Pan = pan,
                    Loop = loop
                });
                return;
            }
            string path;
            if (looseClips.TryGetValue(clip, out path))
            {
                QueueClip(new QueuedClip
                {
                    LoosePath = path,
                    ClipName = clip,
                    Sound = sound,
                    Volume = clipVolume,
                    Pitch = clipPitch,
                    Pan = pan,
                    Loop = loop
                });
                return;
            }
            ReportUnavailable(sound.Id, "clip '" + clip + "' is absent; no exact or indexed family match");
        }

        private void CacheLooseSounds(string directory)
        {
            if (!Directory.Exists(directory)) return;
            try
            {
                string[] files = Directory.GetFiles(directory, "*.wav",
                    SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo info = new FileInfo(files[i]);
                    if (info.Length > 44)
                        looseClips[Path.GetFileNameWithoutExtension(files[i])] = files[i];
                }
            }
            catch (Exception exception)
            {
                LogDiagnostic("[Audio] Failed to scan loose WAV cache: " + exception);
            }
        }

        private void AddClipFamily(UnityAudioClipInfo clip)
        {
            string family = ClipFamilyName(clip.Name);
            if (string.Equals(family, clip.Name, StringComparison.OrdinalIgnoreCase)) return;
            List<UnityAudioClipInfo> variants;
            if (!clipFamilies.TryGetValue(family, out variants))
            {
                variants = new List<UnityAudioClipInfo>();
                clipFamilies[family] = variants;
            }
            variants.Add(clip);
        }

        private static string ClipFamilyName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int underscore = name.LastIndexOf('_');
            int ignored;
            if (underscore > 0 && int.TryParse(name.Substring(underscore + 1), out ignored))
                return name.Substring(0, underscore);
            char last = name[name.Length - 1];
            if (last >= 'A' && last <= 'Z' && name.Length > 1 &&
                char.IsDigit(name[name.Length - 2])) return name.Substring(0, name.Length - 1);
            return name;
        }

        private bool TryResolveClip(string requested, out UnityAudioClipInfo clip)
        {
            if (clips.TryGetValue(requested, out clip)) return true;
            List<UnityAudioClipInfo> variants;
            if (!clipFamilies.TryGetValue(requested, out variants) || variants.Count == 0)
            {
                clip = null;
                return false;
            }
            clip = variants[variantCounter++ % variants.Count];
            return true;
        }

        private bool IsLoopActiveOrPending(string loopKey)
        {
            lock (audioSync)
                return activeLoops.ContainsKey(loopKey) || pendingLoopKeys.Contains(loopKey);
        }

        private void QueueClip(QueuedClip clip)
        {
            lock (audioSync)
            {
                if (stopping || !enabled) return;
                if (clip.Loop && !string.IsNullOrEmpty(clip.Sound.LoopKey))
                {
                    pendingLoopKeys.Add(clip.Sound.LoopKey);
                    cancelledLoopKeys.Remove(clip.Sound.LoopKey);
                }
                // Bound latency after a burst; oldest one-shot effects are less
                // useful than the current simulation tick.
                while (queuedClips.Count >= 32)
                {
                    QueuedClip dropped = queuedClips.Dequeue();
                    if (dropped.Loop && !string.IsNullOrEmpty(dropped.Sound.LoopKey))
                        pendingLoopKeys.Remove(dropped.Sound.LoopKey);
                }
                queuedClips.Enqueue(clip);
            }
            queueSignal.Set();
        }

        private void AudioWorkerMain()
        {
            while (!stopping)
            {
                QueuedClip work = null;
                lock (audioSync)
                {
                    if (queuedClips.Count > 0) work = queuedClips.Dequeue();
                }
                if (work == null)
                {
                    queueSignal.WaitOne(250);
                    continue;
                }
                ProcessQueuedClip(work);
            }
        }

        private void ProcessQueuedClip(QueuedClip work)
        {
            string loopKey = work.Sound == null ? null : work.Sound.LoopKey;
            try
            {
                lock (audioSync)
                {
                    if (!enabled || stopping || (!string.IsNullOrEmpty(loopKey) &&
                        cancelledLoopKeys.Contains(loopKey))) return;
                }
                byte[] wave;
                if (work.Clip != null)
                {
                    PcmCacheEntry pcm;
                    string reason;
                    if (!TryGetCachedPcm(work.Clip, out pcm, out reason))
                    {
                        ReportUnavailable(work.Sound.Id, work.ClipName + ": " + reason);
                        return;
                    }
                    byte[] processed = new byte[pcm.Data.Length];
                    Buffer.BlockCopy(pcm.Data, 0, processed, 0, processed.Length);
                    ApplyGain(processed, pcm.Channels, work.Volume, work.Pan);
                    int sampleRate = MathUtil.Clamp((int)Math.Round(
                        pcm.Frequency * work.Pitch), 8000, 192000);
                    wave = BuildWave(processed, pcm.Channels, sampleRate, 16);
                }
                else
                {
                    byte[] cached;
                    lock (audioSync)
                        looseWaveCache.TryGetValue(work.LoosePath, out cached);
                    wave = cached ?? File.ReadAllBytes(work.LoosePath);
                    if (cached == null)
                        lock (audioSync) looseWaveCache[work.LoosePath] = wave;
                }
                ActiveVoice voice = new ActiveVoice(wave);
                if (!StartVoice(voice, work.Sound, work.Loop)) return;
                lastEvent = "playback started: " + work.Sound.Id + " -> " + work.ClipName;
                LogDiagnostic("[Audio] Playback started: " + work.Sound.Id + " -> " +
                    work.ClipName + (work.Loop ? " [loop]" : string.Empty));
            }
            catch (Exception exception)
            {
                Status = "audio playback failed: " + exception.Message;
                lastEvent = "playback failed: " + work.ClipName + " (" + exception.Message + ")";
                LogDiagnostic("[Audio] Playback failed for " + work.ClipName + ": " + exception);
            }
            finally
            {
                if (!string.IsNullOrEmpty(loopKey))
                {
                    lock (audioSync) pendingLoopKeys.Remove(loopKey);
                }
            }
        }

        private bool TryGetCachedPcm(UnityAudioClipInfo clip, out PcmCacheEntry entry,
            out string reason)
        {
            lock (audioSync)
                if (pcmCache.TryGetValue(clip.Name, out entry))
                {
                    reason = null;
                    return true;
                }
            byte[] pcm;
            if (!TryReadPcm(clip, out pcm, out reason))
            {
                entry = null;
                return false;
            }
            entry = new PcmCacheEntry
            {
                Data = pcm,
                Channels = clip.Channels,
                Frequency = clip.Frequency
            };
            lock (audioSync)
            {
                pcmCache[clip.Name] = entry;
                pcmCacheOrder.Enqueue(clip.Name);
                while (pcmCacheOrder.Count > 64)
                    pcmCache.Remove(pcmCacheOrder.Dequeue());
            }
            return true;
        }

        private void ReportUnavailable(string id, string reason)
        {
            lastEvent = id + " unavailable: " + reason;
            LogDiagnostic("[Audio] Failed to load " + id + ": " + reason);
        }

        private static void LogDiagnostic(string message)
        {
            Debug.WriteLine(message);
            Trace.WriteLine(message);
        }

        private bool StartVoice(ActiveVoice voice, SoundEvent sound, bool loop)
        {
            lock (audioSync)
            {
                if (!enabled || stopping || (!string.IsNullOrEmpty(sound.LoopKey) &&
                    cancelledLoopKeys.Contains(sound.LoopKey)))
                {
                    voice.Dispose();
                    return false;
                }
                if (loop && !string.IsNullOrEmpty(sound.LoopKey))
                {
                    StopLoopVoiceLocked(sound.LoopKey);
                    activeLoops[sound.LoopKey] = voice;
                    voice.Player.PlayLooping();
                    return true;
                }
                activePlayers.Add(voice);
                voice.Player.Play();
                TrimVoices();
                return true;
            }
        }

        private void StopLoopVoice(string loopKey)
        {
            if (string.IsNullOrEmpty(loopKey)) return;
            lock (audioSync)
            {
                cancelledLoopKeys.Add(loopKey);
                pendingLoopKeys.Remove(loopKey);
                StopLoopVoiceLocked(loopKey);
            }
        }

        private void StopLoopVoiceLocked(string loopKey)
        {
            ActiveVoice voice;
            if (!activeLoops.TryGetValue(loopKey, out voice)) return;
            voice.Player.Stop();
            voice.Dispose();
            activeLoops.Remove(loopKey);
        }

        public void StopAllLoops()
        {
            lock (audioSync)
            {
                string[] keys = new string[activeLoops.Count];
                activeLoops.Keys.CopyTo(keys, 0);
                for (int i = 0; i < keys.Length; i++)
                {
                    cancelledLoopKeys.Add(keys[i]);
                    StopLoopVoiceLocked(keys[i]);
                }
                pendingLoopKeys.Clear();
            }
        }

        public void StopAllVoices()
        {
            lock (audioSync)
            {
                string[] keys = new string[activeLoops.Count];
                activeLoops.Keys.CopyTo(keys, 0);
                for (int i = 0; i < keys.Length; i++) StopLoopVoiceLocked(keys[i]);
                for (int i = 0; i < activePlayers.Count; i++)
                    activePlayers[i].Dispose();
                activePlayers.Clear();
                queuedClips.Clear();
                pendingLoopKeys.Clear();
            }
        }

        private bool TryReadPcm(UnityAudioClipInfo clip, out byte[] pcm,
            out string reason)
        {
            pcm = null;
            reason = null;
            if (clip.ResourceSize > int.MaxValue)
            {
                reason = "clip too large";
                return false;
            }
            UnityFsNodeInfo resource = soundBundle.FindResourceNode(clip.ResourceSource);
            if (resource == null)
            {
                reason = "resource node missing";
                return false;
            }
            byte[] fsb = soundBundle.ReadNodeRange(resource, (long)clip.ResourceOffset,
                (int)clip.ResourceSize);
            if (fsb.Length < 32 || fsb[0] != (byte)'F' || fsb[1] != (byte)'S' ||
                fsb[2] != (byte)'B' || fsb[3] != (byte)'5')
            {
                reason = "not FSB5";
                return false;
            }
            int codec = ReadInt32Little(fsb, 24);
            if (codec != 2 || clip.BitsPerSample != 16)
            {
                reason = "FSB codec " + codec;
                return false;
            }
            int dataSize = ReadInt32Little(fsb, 20);
            int dataOffset = fsb.Length - dataSize;
            int frames = (int)Math.Round(clip.LengthSeconds * clip.Frequency);
            int pcmSize = checked(frames * clip.Channels * 2);
            if (dataOffset < 0 || pcmSize <= 0 || dataOffset + pcmSize > fsb.Length)
            {
                reason = "invalid PCM range";
                return false;
            }
            pcm = new byte[pcmSize];
            Buffer.BlockCopy(fsb, dataOffset, pcm, 0, pcm.Length);
            return true;
        }

        private static void ApplyGain(byte[] pcm, int channels, double volume, double pan)
        {
            double leftGain = MathUtil.Clamp01(volume) * (pan > 0.0 ? 1.0 - pan : 1.0);
            double rightGain = MathUtil.Clamp01(volume) * (pan < 0.0 ? 1.0 + pan : 1.0);
            for (int offset = 0; offset + 1 < pcm.Length; offset += 2)
            {
                int channel = (offset / 2) % channels;
                double gain = channels < 2 || channel == 0 ? leftGain : rightGain;
                short sample = unchecked((short)(pcm[offset] | (pcm[offset + 1] << 8)));
                int scaled = MathUtil.Clamp((int)Math.Round(sample * gain), short.MinValue, short.MaxValue);
                pcm[offset] = (byte)(scaled & 0xff);
                pcm[offset + 1] = (byte)((scaled >> 8) & 0xff);
            }
        }

        private static byte[] BuildWave(byte[] pcm, int channels, int sampleRate, int bits)
        {
            using (MemoryStream output = new MemoryStream(pcm.Length + 44))
            using (BinaryWriter writer = new BinaryWriter(output))
            {
                writer.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(pcm.Length + 36);
                writer.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                writer.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                int blockAlign = channels * bits / 8;
                writer.Write(sampleRate * blockAlign);
                writer.Write((short)blockAlign);
                writer.Write((short)bits);
                writer.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(pcm.Length);
                writer.Write(pcm);
                return output.ToArray();
            }
        }

        private static int ReadInt32Little(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8) |
                (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        private void TrimVoices()
        {
            while (activePlayers.Count > 8)
            {
                activePlayers[0].Dispose();
                activePlayers.RemoveAt(0);
            }
        }

        public void Dispose()
        {
            stopping = true;
            queueSignal.Set();
            if (audioWorker != null && audioWorker.IsAlive)
                audioWorker.Join(2000);
            StopAllVoices();
            if (soundBundle != null) soundBundle.Dispose();
            queueSignal.Dispose();
        }
    }
}
