using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Globalization;
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

    // Fixed-tick event gate. It deliberately never synthesizes replacement
    // sounds: only non-empty local Rain World WAV files are played. Current
    // retail installs keep most PCM/Vorbis data inside UnityFS, so the status
    // remains explicit when the local codec path is unavailable.
    public sealed class RainWorldAudioEngine : IDisposable
    {
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
            public void Dispose() { Player.Dispose(); Stream.Dispose(); }
        }

        private readonly string looseSoundDirectory;
        private readonly Dictionary<string, long> lastPlayed =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ActiveVoice> activePlayers = new List<ActiveVoice>();
        private readonly Dictionary<string, UnityAudioClipInfo> clips =
            new Dictionary<string, UnityAudioClipInfo>(StringComparer.OrdinalIgnoreCase);
        private IDictionary<string, RainWorldSoundDefinition> soundDefinitions =
            new Dictionary<string, RainWorldSoundDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Random random = new Random(0x50A0D);
        private UnityFsBundleReader soundBundle;
        private int variantCounter;
        private string lastEvent = "none";

        public RainWorldAudioEngine(RainWorldInstallation installation)
        {
            if (installation == null || string.IsNullOrEmpty(installation.RootPath))
            {
                Status = "audio disabled: Rain World installation unavailable";
                return;
            }
            looseSoundDirectory = Path.Combine(installation.RootPath,
                "RainWorld_Data", "StreamingAssets", "loadedsoundeffects");
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
                    for (int i = 0; i < allClips.Count; i++) clips[allClips[i].Name] = allClips[i];
                }
                Status = "local sounds.txt (" + soundDefinitions.Count +
                    " SoundIDs) + UnityFS sound bank ready (" + clips.Count + " clips)";
            }
            catch (Exception exception)
            {
                if (soundBundle != null) soundBundle.Dispose();
                soundBundle = null;
                Status = "local sounds.txt mapped; UnityFS audio unavailable: " + exception.Message;
            }
        }

        public string Status { get; private set; }
        public string LastEvent { get { return lastEvent; } }

        public void Play(SoundEvent sound, Vec2 listener, long simulationTick,
            double audibleRange)
        {
            if (sound == null) return;
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
                string.IsNullOrEmpty(looseSoundDirectory)) return;
            if (definition.PlayAll)
            {
                for (int i = 0; i < definition.Clips.Length; i++)
                    PlayClip(definition.Clips[i], sound, pan);
            }
            else
            {
                RainWorldSoundClipDefinition clip = definition.Clips[
                    variantCounter++ % definition.Clips.Length];
                PlayClip(clip, sound, pan);
            }
        }

        private void PlayClip(RainWorldSoundClipDefinition definition, SoundEvent sound,
            double pan)
        {
            string clip = definition.Name;
            double clipVolume = MathUtil.Lerp(definition.MinimumVolume,
                definition.MaximumVolume, random.NextDouble()) * sound.Volume;
            double clipPitch = MathUtil.Lerp(definition.MinimumPitch,
                definition.MaximumPitch, random.NextDouble()) * sound.Pitch;
            UnityAudioClipInfo clipInfo;
            if (soundBundle != null && clips.TryGetValue(clip, out clipInfo))
            {
                byte[] wave;
                string reason;
                if (TryDecodePcmWave(clipInfo, clipVolume, clipPitch, pan,
                    out wave, out reason))
                {
                    try
                    {
                        ActiveVoice voice = new ActiveVoice(wave);
                        activePlayers.Add(voice);
                        voice.Player.Play();
                        TrimVoices();
                        return;
                    }
                    catch (Exception exception)
                    {
                        Status = "audio playback failed: " + exception.Message;
                    }
                }
                else
                {
                    lastEvent += " unavailable:" + reason;
                }
            }
            string path = Path.Combine(looseSoundDirectory, clip + ".wav");
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length <= 44) return;
            try
            {
                byte[] wave = File.ReadAllBytes(path);
                ActiveVoice voice = new ActiveVoice(wave);
                activePlayers.Add(voice);
                voice.Player.Play();
                TrimVoices();
            }
            catch (Exception exception)
            {
                Status = "audio playback failed: " + exception.Message;
            }
        }

        private bool TryDecodePcmWave(UnityAudioClipInfo clip, double volume,
            double pitch, double pan, out byte[] wave, out string reason)
        {
            wave = null;
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
            byte[] pcm = new byte[pcmSize];
            Buffer.BlockCopy(fsb, dataOffset, pcm, 0, pcm.Length);
            ApplyGain(pcm, clip.Channels, volume, pan);
            int sampleRate = MathUtil.Clamp((int)Math.Round(clip.Frequency * pitch),
                8000, 192000);
            wave = BuildWave(pcm, clip.Channels, sampleRate, 16);
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
            for (int i = 0; i < activePlayers.Count; i++) activePlayers[i].Dispose();
            activePlayers.Clear();
            if (soundBundle != null) soundBundle.Dispose();
        }
    }
}
