using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Globalization;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;
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
                { "Slugcat_Step_B", Single("Slugcat_Step_B", "walk3B") },
                { "Slugcat_Belly_Slide_Finish_Success", Single("Slugcat_Belly_Slide_Finish_Success", "Slide3A") },
                { "Slugcat_Belly_Slide_Finish_Fail", Single("Slugcat_Belly_Slide_Finish_Fail", "gravel1b") },
                { "Slugcat_Regain_Footing", Single("Slugcat_Regain_Footing", "gravel1A") }
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
    // sound bank. Disk reads, PCM extraction and multi-voice playback stay on
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

        private sealed class WaveOutVoice : IDisposable
        {
            private IntPtr hWaveOut = IntPtr.Zero;
            private GCHandle dataHandle;
            private GCHandle headerHandle;
            private bool prepared;
            private bool disposed;

            public bool IsDone
            {
                get
                {
                    if (disposed) return true;
                    if (headerHandle.IsAllocated)
                    {
                        NativeMethods.WAVEHDR hdr = (NativeMethods.WAVEHDR)headerHandle.Target;
                        return (hdr.dwFlags & NativeMethods.WHDR_DONE) != 0;
                    }
                    return true;
                }
            }

            public bool TryStart(byte[] pcmData, int channels, int sampleRate, int bitsPerSample, bool loop)
            {
                if (pcmData == null || pcmData.Length == 0 || channels <= 0 || sampleRate <= 0) return false;
                try
                {
                    NativeMethods.WAVEFORMATEX format = new NativeMethods.WAVEFORMATEX();
                    format.wFormatTag = (ushort)NativeMethods.WAVE_FORMAT_PCM;
                    format.nChannels = (ushort)channels;
                    format.nSamplesPerSec = (uint)sampleRate;
                    format.wBitsPerSample = (ushort)bitsPerSample;
                    format.nBlockAlign = (ushort)(channels * bitsPerSample / 8);
                    format.nAvgBytesPerSec = (uint)(sampleRate * format.nBlockAlign);
                    format.cbSize = 0;

                    int result = NativeMethods.waveOutOpen(out hWaveOut, new IntPtr(-1), ref format, IntPtr.Zero, IntPtr.Zero, 0);
                    if (result != 0 || hWaveOut == IntPtr.Zero) return false;

                    dataHandle = GCHandle.Alloc(pcmData, GCHandleType.Pinned);
                    NativeMethods.WAVEHDR header = new NativeMethods.WAVEHDR();
                    header.lpData = dataHandle.AddrOfPinnedObject();
                    header.dwBufferLength = pcmData.Length;
                    header.dwFlags = loop ? (NativeMethods.WHDR_BEGINLOOP | NativeMethods.WHDR_ENDLOOP) : 0;
                    header.dwLoops = loop ? unchecked((int)0xFFFFFFFF) : 1;

                    headerHandle = GCHandle.Alloc(header, GCHandleType.Pinned);
                    int prep = NativeMethods.waveOutPrepareHeader(hWaveOut, headerHandle.AddrOfPinnedObject(), Marshal.SizeOf(typeof(NativeMethods.WAVEHDR)));
                    if (prep != 0)
                    {
                        Dispose();
                        return false;
                    }
                    prepared = true;

                    int write = NativeMethods.waveOutWrite(hWaveOut, headerHandle.AddrOfPinnedObject(), Marshal.SizeOf(typeof(NativeMethods.WAVEHDR)));
                    if (write != 0)
                    {
                        Dispose();
                        return false;
                    }
                    return true;
                }
                catch
                {
                    Dispose();
                    return false;
                }
            }

            public void Stop()
            {
                Dispose();
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (hWaveOut != IntPtr.Zero)
                {
                    try { NativeMethods.waveOutReset(hWaveOut); } catch { }
                    if (prepared && headerHandle.IsAllocated)
                    {
                        try { NativeMethods.waveOutUnprepareHeader(hWaveOut, headerHandle.AddrOfPinnedObject(), Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))); } catch { }
                        prepared = false;
                    }
                    try { NativeMethods.waveOutClose(hWaveOut); } catch { }
                    hWaveOut = IntPtr.Zero;
                }
                if (headerHandle.IsAllocated) headerHandle.Free();
                if (dataHandle.IsAllocated) dataHandle.Free();
            }
        }

        private sealed class ActiveVoice : IDisposable
        {
            private readonly WaveOutVoice waveOutVoice;
            private readonly MemoryStream stream;
            private readonly SoundPlayer player;
            private bool disposed;

            public bool IsDone
            {
                get
                {
                    if (disposed) return true;
                    if (waveOutVoice != null) return waveOutVoice.IsDone;
                    return false;
                }
            }

            public ActiveVoice(byte[] pcm, int channels, int sampleRate, int bits, bool loop)
            {
                waveOutVoice = new WaveOutVoice();
                if (!waveOutVoice.TryStart(pcm, channels, sampleRate, bits, loop))
                {
                    waveOutVoice.Dispose();
                    waveOutVoice = null;
                    try
                    {
                        byte[] wave = BuildWave(pcm, channels, sampleRate, bits);
                        stream = new MemoryStream(wave, false);
                        player = new SoundPlayer(stream);
                        player.Load();
                        if (loop) player.PlayLooping();
                        else player.Play();
                    }
                    catch
                    {
                    }
                }
            }

            public void Stop()
            {
                Dispose();
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (waveOutVoice != null) waveOutVoice.Dispose();
                if (player != null)
                {
                    try { player.Stop(); } catch { }
                    player.Dispose();
                }
                if (stream != null) stream.Dispose();
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
        private readonly Dictionary<string, List<string>> looseClipFamilies =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
        // A few Steam installations leave these Spearmaster WAVs as zero-byte
        // streaming placeholders, while the matching UnityFS entries are also
        // unavailable. Preserve the original sounds.txt event and volume, but
        // use an installed grab-beam variant only for that storage failure.
        private static readonly Dictionary<string, string> unavailableClipFallbacks =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // jump2 is present in every supported sound bank (unlike the
                // zero-byte placeholders) and stays subdued by the original
                // SM_Spear_* /vol=0.25 definition.
                { "smSpearPull", "jump2" },
                { "smSpearPull2", "jump2" },
                { "smSpearGrab", "jump2" }
            };
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
                        UnityAudioClipInfo clip = allClips[i];
                        clips[clip.Name] = clip;
                        string normalized = NormalizeName(clip.Name);
                        if (!clips.ContainsKey(normalized))
                            clips[normalized] = clip;
                        AddClipFamily(clip);
                        if (!string.Equals(clip.Name, normalized, StringComparison.OrdinalIgnoreCase))
                            AddClipFamilyExplicit(normalized, clip);
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
            UnityAudioClipInfo clip;
            if (soundBundle != null && TryResolveClip(requested, out clip))
            {
                byte[] pcm;
                if (TryReadPcm(clip, out pcm, out reason))
                {
                    resolved = clip.Name;
                    pcmBytes = pcm.Length;
                    return true;
                }
            }
            string path;
            if (TryResolveLooseClip(requested, out path))
            {
                try
                {
                    byte[] waveBytes = File.ReadAllBytes(path);
                    byte[] pcm;
                    int channels, freq, bits;
                    if (TryReadWav(waveBytes, out pcm, out channels, out freq, out bits))
                    {
                        resolved = Path.GetFileNameWithoutExtension(path);
                        pcmBytes = pcm.Length;
                        reason = null;
                        return true;
                    }
                    reason = "invalid loose WAV format";
                    return false;
                }
                catch (Exception ex)
                {
                    reason = ex.Message;
                    return false;
                }
            }
            reason = "no exact or indexed family match";
            return false;
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

    // 데스크톱 펫에서는 실제 "게임 오버" 개념을 사용하지 않으므로
    // 원작의 슬러그캣 사망 / 게임 오버 효과음은 재생하지 않습니다.
    //
    // 여기서 차단하기 때문에 디코딩 큐에도 들어가지 않아
    // 뒤늦게 사망음이 재생되는 현상도 방지됩니다.
    if (IsSuppressedDeathSound(sound.Id))
    {
        lastEvent = "suppressed death sound: " + sound.Id;
        LogDiagnostic("[Audio] Suppressed death/game-over sound: " + sound.Id);

        // 혹시 동일 이벤트가 루프 사운드로 들어온 경우에도 정리합니다.
        if (sound.Loop && !string.IsNullOrEmpty(sound.LoopKey))
            StopLoopVoice(sound.LoopKey);

        return;
    }

    // Defensive backstop for old/queued callers. Slugcat.EmitImpactSound no
    // longer creates this event for desktop-pet high-speed contacts, but its
    // bassOnly source is the same low cue users perceive as game-over audio.
    if (string.Equals(sound.Id, "Slugcat_Terrain_Impact_Hard",
        StringComparison.OrdinalIgnoreCase))
    {
        lastEvent = "suppressed high-impact sound: " + sound.Id;
        LogDiagnostic("[Audio] Suppressed desktop high-impact sound: " + sound.Id);
        return;
    }

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

    lastEvent = string.Format(
        "{0} volume:{1:0.00} pitch:{2:0.00} pan:{3:0.00}",
        sound.Id, sound.Volume, sound.Pitch, pan);

    RainWorldSoundDefinition definition;
    soundDefinitions.TryGetValue(sound.Id ?? string.Empty, out definition);

    if (definition == null || definition.Clips.Length == 0 ||
        string.IsNullOrEmpty(looseSoundDirectory))
    {
        ReportUnavailable(sound.Id,
            "SoundID has no active sounds.txt mapping");
        return;
    }

    if (sound.Loop)
    {
        RainWorldSoundClipDefinition loopClip =
            definition.Clips[variantCounter++ % definition.Clips.Length];

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
        RainWorldSoundClipDefinition clip =
            definition.Clips[variantCounter++ % definition.Clips.Length];

        PlayClip(clip, sound, pan, false);
    }
}

private void PlayClip(RainWorldSoundClipDefinition definition, SoundEvent sound,
    double pan, bool loop)
{
    string clip = definition.Name;

    // Rain World sounds.txt의 "silence"는 실제 효과음이 아니라
    // 의도적인 무음 placeholder이다.
    // 파일을 찾으려고 시도하거나 오류로 보고하지 않는다.
    if (string.Equals(clip, "silence", StringComparison.OrdinalIgnoreCase))
    {
        lastEvent = "silent sound: " + sound.Id;
        return;
    }

    double clipVolume = MathUtil.Lerp(definition.MinimumVolume,
        definition.MaximumVolume, random.NextDouble()) * sound.Volume;
    double clipPitch = MathUtil.Lerp(definition.MinimumPitch,
        definition.MaximumPitch, random.NextDouble()) * sound.Pitch;

    UnityAudioClipInfo clipInfo = null;
    bool hasUnityClip = soundBundle != null && TryResolveClip(clip, out clipInfo);
    string fallbackClip = null;
    bool useUnavailableFallback = unavailableClipFallbacks.TryGetValue(clip,
        out fallbackClip);
    if (hasUnityClip)
    {
        PcmCacheEntry cached;
        string reason;

        if (TryGetCachedPcm(clipInfo, out cached, out reason))
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
    }
    // A Unity AudioClip name may exist yet carry no stream data. Treat a
    // failed decode exactly like a missing placeholder and try the known-good
    // installed fallback before reporting this event unavailable.
    if (useUnavailableFallback && soundBundle != null &&
        TryResolveClip(fallbackClip, out clipInfo))
    {
        PcmCacheEntry cached;
        string reason;
        if (TryGetCachedPcm(clipInfo, out cached, out reason))
        {
            LogDiagnostic("[Audio] Missing/empty " + clip + "; using installed " +
                fallbackClip + " fallback for " + sound.Id);
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
    }

    string path;
    bool hasLooseClip = TryResolveLooseClip(clip, out path);
    if (!hasLooseClip && useUnavailableFallback)
        hasLooseClip = TryResolveLooseClip(fallbackClip, out path);
    if (hasLooseClip)
    {
        if (useUnavailableFallback)
            LogDiagnostic("[Audio] Missing " + clip + "; using installed " + fallbackClip +
                " fallback for " + sound.Id);
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

    ReportUnavailable(sound.Id,
        "clip '" + clip +
        "' is absent; no exact or indexed family match");
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
                    {
                        string baseName = Path.GetFileNameWithoutExtension(files[i]);
                        looseClips[baseName] = files[i];
                        string normalized = NormalizeName(baseName);
                        if (!looseClips.ContainsKey(normalized))
                            looseClips[normalized] = files[i];
                        AddLooseClipFamily(baseName, files[i]);
                        if (!string.Equals(baseName, normalized, StringComparison.OrdinalIgnoreCase))
                            AddLooseClipFamily(normalized, files[i]);
                    }
                }
            }
            catch (Exception exception)
            {
                LogDiagnostic("[Audio] Failed to scan loose WAV cache: " + exception);
            }
        }

        private void AddClipFamily(UnityAudioClipInfo clip)
        {
            AddClipFamilyExplicit(clip.Name, clip);
            string normalized = NormalizeName(clip.Name);
            if (!string.Equals(clip.Name, normalized, StringComparison.OrdinalIgnoreCase))
                AddClipFamilyExplicit(normalized, clip);
        }

        private void AddClipFamilyExplicit(string name, UnityAudioClipInfo clip)
        {
            string family = ClipFamilyName(name);
            if (string.Equals(family, name, StringComparison.OrdinalIgnoreCase)) return;
            List<UnityAudioClipInfo> variants;
            if (!clipFamilies.TryGetValue(family, out variants))
            {
                variants = new List<UnityAudioClipInfo>();
                clipFamilies[family] = variants;
            }
            if (!variants.Contains(clip)) variants.Add(clip);
        }

        private void AddLooseClipFamily(string name, string path)
        {
            string family = ClipFamilyName(name);
            if (string.Equals(family, name, StringComparison.OrdinalIgnoreCase)) return;
            List<string> variants;
            if (!looseClipFamilies.TryGetValue(family, out variants))
            {
                variants = new List<string>();
                looseClipFamilies[family] = variants;
            }
            if (!variants.Contains(path)) variants.Add(path);
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return name.Replace("_", "").Trim();
        }

// 기존 ClipFamilyName()을 이 버전으로 교체
private static string ClipFamilyName(string name)
{
    if (string.IsNullOrEmpty(name))
        return name;

    // foo_1, foo_2 같은 형식
    int underscore = name.LastIndexOf('_');
    int ignored;
    if (underscore > 0 &&
        int.TryParse(name.Substring(underscore + 1), out ignored))
    {
        return name.Substring(0, underscore);
    }

    char last = name[name.Length - 1];

    // walk3A 같은 기존 Rain World 변형 규칙 유지
    if (last >= 'A' && last <= 'Z' &&
        name.Length > 1 &&
        char.IsDigit(name[name.Length - 2]))
    {
        return name.Substring(0, name.Length - 1);
    }

    if (last >= 'A' && last <= 'Z' &&
        name.Length > 2 &&
        name[name.Length - 2] >= 'A' &&
        name[name.Length - 2] <= 'Z')
    {
        return name.Substring(0, name.Length - 1);
    }

    // ★ 추가:
    // smSpearPull1 / smSpearPull2
    // smSpearGrab1 / smSpearGrab2
    // 같은 숫자 suffix를 하나의 family로 묶는다.
    int digitStart = name.Length;

    while (digitStart > 0 && char.IsDigit(name[digitStart - 1]))
        digitStart--;

    if (digitStart > 0 && digitStart < name.Length)
        return name.Substring(0, digitStart);

    return name;
}

        private bool TryResolveClip(string requested, out UnityAudioClipInfo clip)
        {
            if (clips.TryGetValue(requested, out clip)) return true;
            string normalized = NormalizeName(requested);
            if (clips.TryGetValue(normalized, out clip)) return true;
            List<UnityAudioClipInfo> variants;
            if (clipFamilies.TryGetValue(requested, out variants) && variants.Count > 0)
            {
                clip = variants[variantCounter++ % variants.Count];
                return true;
            }
            if (clipFamilies.TryGetValue(normalized, out variants) && variants.Count > 0)
            {
                clip = variants[variantCounter++ % variants.Count];
                return true;
            }
            clip = null;
            return false;
        }

        private bool TryResolveLooseClip(string requested, out string path)
        {
            if (looseClips.TryGetValue(requested, out path)) return true;
            string normalized = NormalizeName(requested);
            if (looseClips.TryGetValue(normalized, out path)) return true;
            List<string> variants;
            if (looseClipFamilies.TryGetValue(requested, out variants) && variants.Count > 0)
            {
                path = variants[variantCounter++ % variants.Count];
                return true;
            }
            if (looseClipFamilies.TryGetValue(normalized, out variants) && variants.Count > 0)
            {
                path = variants[variantCounter++ % variants.Count];
                return true;
            }
            path = null;
            return false;
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
                byte[] processed;
                int channels;
                int sampleRate;
                int bits = 16;
                if (work.Clip != null)
                {
                    PcmCacheEntry pcm;
                    string reason;
                    if (!TryGetCachedPcm(work.Clip, out pcm, out reason))
                    {
                        ReportUnavailable(work.Sound.Id, work.ClipName + ": " + reason);
                        return;
                    }
                    processed = new byte[pcm.Data.Length];
                    Buffer.BlockCopy(pcm.Data, 0, processed, 0, processed.Length);
                    channels = pcm.Channels;
                    sampleRate = MathUtil.Clamp((int)Math.Round(
                        pcm.Frequency * work.Pitch), 8000, 192000);
                }
                else
                {
                    byte[] cached;
                    lock (audioSync)
                        looseWaveCache.TryGetValue(work.LoosePath, out cached);
                    byte[] waveFileBytes = cached ?? File.ReadAllBytes(work.LoosePath);
                    if (cached == null)
                        lock (audioSync) looseWaveCache[work.LoosePath] = waveFileBytes;

                    byte[] rawPcm;
                    int srcChannels, srcFreq, srcBits;
                    if (!TryReadWav(waveFileBytes, out rawPcm, out srcChannels, out srcFreq, out srcBits))
                    {
                        ReportUnavailable(work.Sound.Id, "failed to parse WAV " + work.LoosePath);
                        return;
                    }
                    processed = rawPcm;
                    channels = srcChannels;
                    bits = srcBits;
                    sampleRate = MathUtil.Clamp((int)Math.Round(srcFreq * work.Pitch), 8000, 192000);
                }
                ApplyGain(processed, channels, work.Volume, work.Pan);
                ActiveVoice voice = new ActiveVoice(processed, channels, sampleRate, bits, work.Loop);
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

        // RainWorldAudioEngine 클래스 내부 필드 영역에 추가합니다.
// 슬러그캣 사망 / 게임 오버 계열 사운드는 오디오 엔진 단계에서 완전히 차단합니다.
        private static readonly HashSet<string> suppressedDeathSoundIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Slugcat_Death",
                "Player_Death",
                // Player.TerrainImpact and Player.Die use these real SoundIDs
                // in the installed DLL. They were missing from the original
                // desktop-pet filter, so a hard wall/floor impact could still
                // leak a game-over cue.
                "Slugcat_Terrain_Impact_Death",
                "UI_Slugcat_Die",
                "Game_Over",
                "GameOver",
                "HUD_Game_Over",
                "HUD_Game_Over_Prompt",
                "UI_Multiplayer_Game_Over"
            };

    private static bool IsSuppressedDeathSound(string soundId)
    {
        if (string.IsNullOrEmpty(soundId))
            return false;

        if (suppressedDeathSoundIds.Contains(soundId))
            return true;

        // sounds.txt 또는 게임 버전에 따라 이름이 조금 달라도
        // 슬러그캣/플레이어의 사망 및 게임 오버 이벤트만 차단합니다.
        string normalized = soundId.Replace("-", "_")
            .Replace(" ", "_");

        if (normalized.IndexOf("Game_Over",
            StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (normalized.IndexOf("GameOver",
            StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (normalized.IndexOf("Slugcat_Death",
            StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (normalized.IndexOf("Player_Death",
            StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (normalized.IndexOf("Terrain_Impact_Death",
            StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (normalized.IndexOf("UI_Slugcat_Die",
            StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

        private static bool TryReadWav(byte[] waveBytes, out byte[] pcm,
            out int channels, out int sampleRate, out int bits)
        {
            pcm = null;
            channels = 1;
            sampleRate = 44100;
            bits = 16;
            if (waveBytes == null || waveBytes.Length < 44) return false;
            try
            {
                using (MemoryStream stream = new MemoryStream(waveBytes, false))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (new string(reader.ReadChars(4)) != "RIFF") return false;
                    reader.ReadUInt32();
                    if (new string(reader.ReadChars(4)) != "WAVE") return false;

                    bool foundFmt = false;
                    bool foundData = false;
                    while (stream.Position + 8 <= stream.Length)
                    {
                        string chunkId = new string(reader.ReadChars(4));
                        uint chunkSize = reader.ReadUInt32();
                        long nextPos = stream.Position + chunkSize + (chunkSize & 1);
                        if (chunkId == "fmt " && chunkSize >= 16)
                        {
                            ushort formatTag = reader.ReadUInt16();
                            channels = reader.ReadUInt16();
                            sampleRate = (int)reader.ReadUInt32();
                            reader.ReadUInt32();
                            reader.ReadUInt16();
                            bits = reader.ReadUInt16();
                            foundFmt = (formatTag == 1);
                        }
                        else if (chunkId == "data")
                        {
                            int dataLen = (int)Math.Min((long)chunkSize, stream.Length - stream.Position);
                            if (dataLen > 0)
                            {
                                pcm = reader.ReadBytes(dataLen);
                                foundData = true;
                            }
                        }
                        stream.Position = Math.Min(nextPos, stream.Length);
                    }
                    return foundFmt && foundData && pcm != null && pcm.Length > 0 && bits == 16;
                }
            }
            catch
            {
                return false;
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
                    return true;
                }
                activePlayers.Add(voice);
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
            voice.Stop();
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
            for (int i = activePlayers.Count - 1; i >= 0; i--)
            {
                if (activePlayers[i].IsDone)
                {
                    activePlayers[i].Dispose();
                    activePlayers.RemoveAt(i);
                }
            }
            while (activePlayers.Count > 16)
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
