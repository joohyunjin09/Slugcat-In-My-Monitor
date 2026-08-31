using System;
using System.Collections.Generic;
using System.IO;
using Fmod5Sharp;
using Fmod5Sharp.FmodTypes;
using NVorbis;
using RainWorldDesktopPet.RainWorld;

namespace RainWorldDesktopPet.Audio
{
    public sealed class RainWorldPcmClip
    {
        internal RainWorldPcmClip(string name, byte[] data, int dataOffset,
            int dataLength, int channels, int sampleRate, int bitsPerSample)
        {
            Name = name;
            Data = data;
            DataOffset = dataOffset;
            DataLength = dataLength;
            Channels = channels;
            SampleRate = sampleRate;
            BitsPerSample = bitsPerSample;
        }

        public readonly string Name;
        public readonly byte[] Data;
        public readonly int DataOffset;
        public readonly int DataLength;
        public readonly int Channels;
        public readonly int SampleRate;
        public readonly int BitsPerSample;
        public int MemoryBytes { get { return Data == null ? 0 : Data.Length; } }
    }

    public sealed class RainWorldSoundBank : IDisposable
    {
        private readonly Dictionary<string, UnityAudioClipInfo> clips =
            new Dictionary<string, UnityAudioClipInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<UnityAudioClipInfo>> variants =
            new Dictionary<string, List<UnityAudioClipInfo>>(StringComparer.OrdinalIgnoreCase);
        private UnityFsBundleReader bundle;

        public RainWorldSoundBank(RainWorldInstallation installation)
            : this(installation, null)
        {
        }

        public RainWorldSoundBank(RainWorldInstallation installation,
            IEnumerable<string> requestedClipNames)
        {
            if (installation == null) throw new ArgumentNullException("installation");
            HashSet<string> requested = requestedClipNames == null ? null :
                new HashSet<string>(requestedClipNames, StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(installation.StreamingAssetsPath,
                "AssetBundles", "loadedsoundeffects");
            bundle = new UnityFsBundleReader(path);
            try
            {
                UnityFsNodeInfo serializedNode = bundle.FindSerializedFileNode();
                if (serializedNode == null)
                    throw new InvalidDataException("Rain World sound bundle has no serialized file.");
                using (UnitySerializedFileReader serialized = new UnitySerializedFileReader(
                    bundle.ReadNode(serializedNode), serializedNode.Path))
                {
                    IList<UnityAudioClipInfo> all = serialized.ReadAudioClips(
                        delegate(string name) { return IsRequested(name, requested); });
                    for (int i = 0; i < all.Count; i++) AddClip(all[i]);
                }
            }
            catch
            {
                bundle.Dispose();
                bundle = null;
                throw;
            }
        }

        public int ClipCount { get { return clips.Count; } }

        private static bool IsRequested(string clipName, ISet<string> requested)
        {
            if (requested == null) return true;
            if (requested.Contains(clipName ?? string.Empty)) return true;
            string baseName = VariantBaseName(clipName);
            return baseName != null && requested.Contains(baseName);
        }

        public IList<string> ResolveClipNames(string requestedName)
        {
            UnityAudioClipInfo exact;
            if (clips.TryGetValue(requestedName ?? string.Empty, out exact))
                return new string[] { exact.Name };
            List<UnityAudioClipInfo> group;
            if (!variants.TryGetValue(requestedName ?? string.Empty, out group))
                return new string[0];
            string[] result = new string[group.Count];
            for (int i = 0; i < group.Count; i++) result[i] = group[i].Name;
            return result;
        }

        public bool TryLoadPcm(string clipName, out RainWorldPcmClip clip,
            out string reason)
        {
            clip = null;
            reason = null;
            UnityAudioClipInfo info;
            if (!clips.TryGetValue(clipName ?? string.Empty, out info))
            {
                reason = "clip metadata missing";
                return false;
            }
            if (info.ResourceSize > int.MaxValue)
            {
                reason = "clip resource is too large";
                return false;
            }
            UnityFsNodeInfo resource = bundle.FindResourceNode(info.ResourceSource);
            if (resource == null)
            {
                reason = "resource node missing";
                return false;
            }
            byte[] fsb = bundle.ReadNodeRange(resource, (long)info.ResourceOffset,
                (int)info.ResourceSize);
            if (fsb.Length < 60 || fsb[0] != (byte)'F' || fsb[1] != (byte)'S' ||
                fsb[2] != (byte)'B' || fsb[3] != (byte)'5')
            {
                reason = "resource is not FSB5";
                return false;
            }
            int sampleCount = ReadInt32Little(fsb, 8);
            int sampleHeadersSize = ReadInt32Little(fsb, 12);
            int nameTableSize = ReadInt32Little(fsb, 16);
            int dataSize = ReadInt32Little(fsb, 20);
            int codec = ReadInt32Little(fsb, 24) & 0x0f;
            if (sampleCount != 1)
            {
                reason = "FSB5 subsound count " + sampleCount;
                return false;
            }
            if (codec == 15)
                return TryDecodeVorbis(info, fsb, out clip, out reason);
            if (codec != 2 || info.BitsPerSample != 16)
            {
                reason = "unsupported FSB5 codec " + codec;
                return false;
            }
            long dataOffsetValue = 60L + sampleHeadersSize + nameTableSize;
            if (sampleHeadersSize < 8 || nameTableSize < 0 || dataSize <= 0 ||
                dataOffsetValue < 0 || dataOffsetValue + dataSize > fsb.Length)
            {
                reason = "invalid FSB5 PCM range";
                return false;
            }
            int expected = (int)Math.Round(info.LengthSeconds * info.Frequency) *
                info.Channels * 2;
            int pcmLength = expected > 0 && expected <= dataSize ? expected : dataSize;
            pcmLength -= pcmLength % Math.Max(2, info.Channels * 2);
            if (pcmLength <= 0)
            {
                reason = "empty PCM payload";
                return false;
            }
            clip = new RainWorldPcmClip(info.Name, fsb, (int)dataOffsetValue,
                pcmLength, info.Channels, info.Frequency, info.BitsPerSample);
            return true;
        }

        private static bool TryDecodeVorbis(UnityAudioClipInfo info, byte[] fsb,
            out RainWorldPcmClip clip, out string reason)
        {
            clip = null;
            reason = null;
            try
            {
                FmodSoundBank decodedBank = FsbLoader.LoadFsbFromByteArray(fsb);
                if (decodedBank == null || decodedBank.Samples == null ||
                    decodedBank.Samples.Count != 1)
                {
                    reason = "FSB5 Vorbis subsound count is not one";
                    return false;
                }
                byte[] ogg;
                string extension;
                if (!decodedBank.Samples[0].RebuildAsStandardFileFormat(
                    out ogg, out extension) || ogg == null ||
                    !string.Equals(extension, "ogg", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "FSB5 Vorbis reconstruction failed";
                    return false;
                }

                using (MemoryStream source = new MemoryStream(ogg, false))
                using (VorbisReader reader = new VorbisReader(source, false))
                using (MemoryStream pcm = new MemoryStream(ExpectedPcmCapacity(info)))
                {
                    int channels = reader.Channels;
                    int sampleRate = reader.SampleRate;
                    if (channels <= 0 || channels > 8 || sampleRate <= 0)
                    {
                        reason = "invalid decoded Vorbis format";
                        return false;
                    }
                    float[] samples = new float[4096 * channels];
                    byte[] bytes = new byte[samples.Length * 2];
                    int read;
                    while ((read = reader.ReadSamples(samples, 0, samples.Length)) > 0)
                    {
                        if (pcm.Length + read * 2L > 24L * 1024L * 1024L)
                        {
                            reason = "decoded Vorbis clip exceeds PCM cache limit";
                            return false;
                        }
                        for (int i = 0; i < read; i++)
                        {
                            double value = Math.Max(-1.0, Math.Min(1.0, samples[i]));
                            short sample = (short)Math.Round(value < 0.0
                                ? value * 32768.0 : value * 32767.0);
                            bytes[i * 2] = (byte)(sample & 0xff);
                            bytes[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
                        }
                        pcm.Write(bytes, 0, read * 2);
                    }
                    if (pcm.Length == 0)
                    {
                        reason = "decoded Vorbis payload is empty";
                        return false;
                    }
                    byte[] data = pcm.ToArray();
                    clip = new RainWorldPcmClip(info.Name, data, 0, data.Length,
                        channels, sampleRate, 16);
                    return true;
                }
            }
            catch (Exception exception)
            {
                reason = "FSB5 Vorbis decode failed: " + exception.Message;
                return false;
            }
        }

        private static int ExpectedPcmCapacity(UnityAudioClipInfo info)
        {
            double bytes = Math.Max(0.0, info.LengthSeconds) *
                Math.Max(1, info.Frequency) * Math.Max(1, info.Channels) * 2.0;
            return (int)Math.Min(24L * 1024L * 1024L,
                Math.Max(4096.0, bytes));
        }

        public void TrimReadCache()
        {
            if (bundle != null) bundle.TrimBlockCache();
        }

        private void AddClip(UnityAudioClipInfo info)
        {
            clips[info.Name] = info;
            string baseName = VariantBaseName(info.Name);
            if (baseName == null) return;
            List<UnityAudioClipInfo> group;
            if (!variants.TryGetValue(baseName, out group))
            {
                group = new List<UnityAudioClipInfo>();
                variants.Add(baseName, group);
            }
            group.Add(info);
        }

        private static string VariantBaseName(string name)
        {
            int underscore = name == null ? -1 : name.LastIndexOf('_');
            if (underscore <= 0 || underscore == name.Length - 1) return null;
            for (int i = underscore + 1; i < name.Length; i++)
                if (name[i] < '0' || name[i] > '9') return null;
            return name.Substring(0, underscore);
        }

        private static int ReadInt32Little(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8) |
                (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        public void Dispose()
        {
            if (bundle == null) return;
            bundle.Dispose();
            bundle = null;
        }
    }
}
