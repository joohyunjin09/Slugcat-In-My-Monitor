using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RainWorldDesktopPet.RainWorld
{
    public sealed class UnitySerializedObjectInfo
    {
        internal UnitySerializedObjectInfo(long pathId, int classId, long byteOffset, uint byteSize)
        {
            PathId = pathId;
            ClassId = classId;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        public readonly long PathId;
        public readonly int ClassId;
        public readonly long ByteOffset;
        public readonly uint ByteSize;
    }

    public sealed class UnityTextAssetInfo
    {
        internal UnityTextAssetInfo(UnitySerializedObjectInfo serializedObject, string name, string text)
        {
            SerializedObject = serializedObject;
            Name = name;
            Text = text;
        }

        public readonly UnitySerializedObjectInfo SerializedObject;
        public readonly string Name;
        public readonly string Text;
    }

    public sealed class UnityTexture2DInfo
    {
        internal UnityTexture2DInfo(
            UnitySerializedObjectInfo serializedObject,
            string name,
            int width,
            int height,
            uint completeImageSize,
            int textureFormat,
            int mipCount,
            int filterMode,
            int wrapU,
            int wrapV,
            int wrapW,
            byte[] inlineData,
            ulong streamOffset,
            uint streamSize,
            string streamPath)
        {
            SerializedObject = serializedObject;
            Name = name;
            Width = width;
            Height = height;
            CompleteImageSize = completeImageSize;
            TextureFormat = textureFormat;
            MipCount = mipCount;
            FilterMode = filterMode;
            WrapU = wrapU;
            WrapV = wrapV;
            WrapW = wrapW;
            InlineData = inlineData;
            StreamOffset = streamOffset;
            StreamSize = streamSize;
            StreamPath = streamPath;
        }

        public readonly UnitySerializedObjectInfo SerializedObject;
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;
        public readonly uint CompleteImageSize;
        public readonly int TextureFormat;
        public readonly int MipCount;
        public readonly int FilterMode;
        public readonly int WrapU;
        public readonly int WrapV;
        public readonly int WrapW;
        public readonly ulong StreamOffset;
        public readonly uint StreamSize;
        public readonly string StreamPath;
        internal readonly byte[] InlineData;
    }

    public sealed class UnityAudioClipInfo
    {
        internal UnityAudioClipInfo(UnitySerializedObjectInfo serializedObject,
            string name, int channels, int frequency, int bitsPerSample,
            float lengthSeconds, string resourceSource, ulong resourceOffset,
            ulong resourceSize, int compressionFormat)
        {
            SerializedObject = serializedObject;
            Name = name;
            Channels = channels;
            Frequency = frequency;
            BitsPerSample = bitsPerSample;
            LengthSeconds = lengthSeconds;
            ResourceSource = resourceSource;
            ResourceOffset = resourceOffset;
            ResourceSize = resourceSize;
            CompressionFormat = compressionFormat;
        }

        public readonly UnitySerializedObjectInfo SerializedObject;
        public readonly string Name;
        public readonly int Channels;
        public readonly int Frequency;
        public readonly int BitsPerSample;
        public readonly float LengthSeconds;
        public readonly string ResourceSource;
        public readonly ulong ResourceOffset;
        public readonly ulong ResourceSize;
        public readonly int CompressionFormat;
    }

    /// <summary>
    /// Minimal reader for the Unity 2020 SerializedFile format used by Rain World.
    /// It deliberately reads only the object table, TextAsset and Texture2D. It does
    /// not load Unity, Assembly-CSharp, or any game code.
    /// </summary>
    public sealed class UnitySerializedFileReader : IDisposable
    {
        public const int Texture2DClassId = 28;
        public const int TextAssetClassId = 49;
        public const int AudioClipClassId = 83;

        private const int SupportedSerializedVersion = 22;
        private const int MaximumCollectionCount = 10000000;
        private const int MaximumStringBytes = 16 * 1024 * 1024;

        private readonly string filePath;
        private readonly Stream stream;
        private readonly bool ownsStream;
        private readonly UnityEndianReader reader;
        private readonly object sync = new object();
        private readonly List<UnitySerializedObjectInfo> objects = new List<UnitySerializedObjectInfo>();
        private bool disposed;

        public UnitySerializedFileReader(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException("filePath");
            this.filePath = Path.GetFullPath(filePath);
            stream = new FileStream(this.filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
            ownsStream = true;
            reader = new UnityEndianReader(stream, false);
            try
            {
                ReadMetadata();
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public UnitySerializedFileReader(byte[] data, string displayName)
        {
            if (data == null) throw new ArgumentNullException("data");
            filePath = displayName ?? "<memory>";
            stream = new MemoryStream(data, false);
            ownsStream = true;
            reader = new UnityEndianReader(stream, false);
            try
            {
                ReadMetadata();
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public string FilePath { get { return filePath; } }
        public string UnityVersion { get; private set; }
        public int SerializedVersion { get; private set; }
        public long DataOffset { get; private set; }
        public IList<UnitySerializedObjectInfo> Objects { get { return objects.AsReadOnly(); } }

        public bool TryReadTextAsset(string name, out UnityTextAssetInfo asset)
        {
            if (name == null) throw new ArgumentNullException("name");
            lock (sync)
            {
                ThrowIfDisposed();
                for (int i = 0; i < objects.Count; i++)
                {
                    UnitySerializedObjectInfo item = objects[i];
                    if (item.ClassId != TextAssetClassId) continue;
                    string objectName = ReadObjectName(item);
                    if (!string.Equals(objectName, name, StringComparison.OrdinalIgnoreCase)) continue;
                    asset = ReadTextAsset(item);
                    return true;
                }
            }
            asset = null;
            return false;
        }

        public UnityTextAssetInfo ReadTextAsset(string name)
        {
            UnityTextAssetInfo asset;
            if (!TryReadTextAsset(name, out asset))
                throw new InvalidDataException("Unity TextAsset was not found by name: " + name);
            return asset;
        }

        public bool TryReadTexture2D(string name, out UnityTexture2DInfo texture)
        {
            if (name == null) throw new ArgumentNullException("name");
            lock (sync)
            {
                ThrowIfDisposed();
                for (int i = 0; i < objects.Count; i++)
                {
                    UnitySerializedObjectInfo item = objects[i];
                    if (item.ClassId != Texture2DClassId) continue;
                    string objectName = ReadObjectName(item);
                    if (!string.Equals(objectName, name, StringComparison.OrdinalIgnoreCase)) continue;
                    texture = ReadTexture2D(item);
                    return true;
                }
            }
            texture = null;
            return false;
        }

        public UnityTexture2DInfo ReadTexture2D(string name)
        {
            UnityTexture2DInfo texture;
            if (!TryReadTexture2D(name, out texture))
                throw new InvalidDataException("Unity Texture2D was not found by name: " + name);
            return texture;
        }

        public IList<UnityAudioClipInfo> ReadAudioClips()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                List<UnityAudioClipInfo> result = new List<UnityAudioClipInfo>();
                for (int i = 0; i < objects.Count; i++)
                    if (objects[i].ClassId == AudioClipClassId)
                        result.Add(ReadAudioClip(objects[i]));
                return result;
            }
        }

        public byte[] ReadTextureData(UnityTexture2DInfo texture)
        {
            if (texture == null) throw new ArgumentNullException("texture");
            ThrowIfDisposed();

            if (!string.IsNullOrEmpty(texture.StreamPath) && texture.StreamSize > 0)
            {
                string resolvedPath = ResolveExternalResourcePath(texture.StreamPath);
                using (FileStream payload = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess))
                {
                    long offset = CheckedLong(texture.StreamOffset, "Texture stream offset is too large.");
                    int size = CheckedInt(texture.StreamSize, "Texture stream payload is too large.");
                    EnsureRange(offset, size, payload.Length, "Texture stream payload is outside " + resolvedPath);
                    payload.Position = offset;
                    return ReadExactly(payload, size);
                }
            }

            if (texture.InlineData != null && texture.InlineData.Length > 0)
            {
                byte[] result = new byte[texture.InlineData.Length];
                Buffer.BlockCopy(texture.InlineData, 0, result, 0, result.Length);
                return result;
            }

            throw new InvalidDataException("Texture2D has neither inline image data nor external stream data: " + texture.Name);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ownsStream) stream.Dispose();
        }

        private void ReadMetadata()
        {
            // The fixed SerializedFile header is always big-endian, including the
            // extended 64-bit size fields introduced in format version 22.
            reader.IsLittleEndian = false;
            reader.ReadUInt32(); // legacy metadata size slot
            reader.ReadUInt32(); // legacy file size slot
            uint versionValue = reader.ReadUInt32();
            reader.ReadUInt32(); // legacy data offset slot
            SerializedVersion = CheckedInt(versionValue, "Serialized file version is too large.");

            if (SerializedVersion != SupportedSerializedVersion)
            {
                throw new NotSupportedException("Rain World embedded atlas reader supports Unity SerializedFile version " +
                    SupportedSerializedVersion + ", but this file uses version " + SerializedVersion + ".");
            }

            byte endianFlag = reader.ReadByte();
            reader.ReadBytes(3);
            uint metadataSize = reader.ReadUInt32();
            ulong declaredFileSizeValue = reader.ReadUInt64();
            ulong dataOffsetValue = reader.ReadUInt64();
            reader.ReadUInt64(); // unknown/reserved in v22

            long declaredFileSize = CheckedLong(declaredFileSizeValue, "Serialized file size is too large.");
            DataOffset = CheckedLong(dataOffsetValue, "Serialized data offset is too large.");
            long metadataStart = reader.Position;
            long metadataEnd = checked(metadataStart + metadataSize);
            if (declaredFileSize > stream.Length || DataOffset > stream.Length || metadataEnd > stream.Length)
                throw new InvalidDataException("SerializedFile header points outside the file.");
            if (DataOffset < metadataEnd)
                throw new InvalidDataException("SerializedFile data overlaps its metadata.");

            reader.IsLittleEndian = endianFlag == 0;
            UnityVersion = reader.ReadNullTerminatedUtf8(MaximumStringBytes);
            reader.ReadInt32(); // BuildTarget
            bool typeTreeEnabled = reader.ReadByte() != 0;

            int typeCount = ReadCollectionCount("serialized type", MaximumCollectionCount);
            List<int> classIds = new List<int>(typeCount);
            for (int i = 0; i < typeCount; i++)
            {
                int classId = reader.ReadInt32();
                reader.ReadByte(); // is stripped type
                reader.ReadInt16(); // script type index
                if (classId == 114) reader.ReadBytes(16); // MonoBehaviour script ID
                reader.ReadBytes(16); // old type hash
                if (typeTreeEnabled)
                {
                    int nodeCount = ReadCollectionCount("type tree node", MaximumCollectionCount);
                    int stringBufferSize = ReadCollectionCount("type tree string byte", MaximumStringBytes);
                    // SerializedFile v22 uses the 32-byte blob node layout:
                    // hBBIIiiiQ, followed by its local string buffer.
                    reader.ReadBytes(checked(nodeCount * 32));
                    reader.ReadBytes(stringBufferSize);
                    int dependencyCount = ReadCollectionCount("type dependency", MaximumCollectionCount);
                    reader.ReadBytes(checked(dependencyCount * 4));
                }
                classIds.Add(classId);
            }

            int objectCount = ReadCollectionCount("serialized object", MaximumCollectionCount);
            for (int i = 0; i < objectCount; i++)
            {
                reader.Align(4);
                long pathId = reader.ReadInt64();
                long relativeOffset = reader.ReadInt64();
                uint byteSize = reader.ReadUInt32();
                int typeIndex = reader.ReadInt32();
                if (typeIndex < 0 || typeIndex >= classIds.Count)
                    throw new InvalidDataException("Serialized object has an invalid type index: " + typeIndex);
                if (relativeOffset < 0)
                    throw new InvalidDataException("Serialized object has a negative data offset.");
                long absoluteOffset = checked(DataOffset + relativeOffset);
                if (absoluteOffset < DataOffset)
                    throw new InvalidDataException("Serialized object points before the data section.");
                EnsureRange(absoluteOffset, byteSize, stream.Length, "Serialized object points outside the file.");
                objects.Add(new UnitySerializedObjectInfo(pathId, classIds[typeIndex], absoluteOffset, byteSize));
            }

            if (reader.Position > metadataEnd)
                throw new InvalidDataException("Serialized object table extends beyond the declared metadata size.");
        }

        private string ReadObjectName(UnitySerializedObjectInfo item)
        {
            reader.Position = item.ByteOffset;
            return reader.ReadAlignedUtf8String(ObjectEnd(item), MaximumStringBytes);
        }

        private UnityTextAssetInfo ReadTextAsset(UnitySerializedObjectInfo item)
        {
            reader.Position = item.ByteOffset;
            long end = ObjectEnd(item);
            string name = reader.ReadAlignedUtf8String(end, MaximumStringBytes);
            byte[] script = reader.ReadSizedByteArray(end);
            string text = Encoding.UTF8.GetString(script);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            return new UnityTextAssetInfo(item, name, text);
        }

        private UnityTexture2DInfo ReadTexture2D(UnitySerializedObjectInfo item)
        {
            reader.Position = item.ByteOffset;
            long end = ObjectEnd(item);
            string name = reader.ReadAlignedUtf8String(end, MaximumStringBytes);

            // Unity 2020.3 Texture2D schema. The fields intentionally not exposed
            // below are still consumed so StreamData is reached without offsets.
            reader.ReadInt32(); // m_ForcedFallbackFormat
            reader.ReadByte(); // m_DownscaleFallback
            reader.ReadByte(); // m_IsAlphaChannelOptional
            reader.Align(4);
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            uint completeImageSize = reader.ReadUInt32();
            reader.ReadInt32(); // m_MipsStripped
            int textureFormat = reader.ReadInt32();
            int mipCount = reader.ReadInt32();
            reader.ReadByte(); // m_IsReadable
            reader.ReadByte(); // m_IsPreProcessed
            reader.ReadByte(); // m_IgnoreMasterTextureLimit
            reader.ReadByte(); // m_StreamingMipmaps
            reader.ReadInt32(); // m_StreamingMipmapsPriority
            reader.ReadInt32(); // m_ImageCount
            reader.ReadInt32(); // m_TextureDimension
            int filterMode = reader.ReadInt32();
            reader.ReadInt32(); // m_Aniso
            reader.ReadSingle(); // m_MipBias
            int wrapU = reader.ReadInt32();
            int wrapV = reader.ReadInt32();
            int wrapW = reader.ReadInt32();
            reader.ReadInt32(); // m_LightmapFormat
            reader.ReadInt32(); // m_ColorSpace
            reader.ReadSizedByteArray(end); // m_PlatformBlob
            byte[] inlineData = reader.ReadSizedByteArray(end); // image data
            ulong streamOffset = reader.ReadUInt64();
            uint streamSize = reader.ReadUInt32();
            string streamPath = reader.ReadAlignedUtf8String(end, MaximumStringBytes);

            if (width <= 0 || height <= 0 || width > 65536 || height > 65536)
                throw new InvalidDataException("Texture2D has invalid dimensions: " + width + "x" + height);
            if (mipCount <= 0)
                throw new InvalidDataException("Texture2D has no mip levels: " + name);
            if (reader.Position > end)
                throw new InvalidDataException("Texture2D fields extend beyond the serialized object: " + name);

            return new UnityTexture2DInfo(item, name, width, height, completeImageSize,
                textureFormat, mipCount, filterMode, wrapU, wrapV, wrapW, inlineData,
                streamOffset, streamSize, streamPath);
        }

        private UnityAudioClipInfo ReadAudioClip(UnitySerializedObjectInfo item)
        {
            reader.Position = item.ByteOffset;
            long end = ObjectEnd(item);
            string name = reader.ReadAlignedUtf8String(end, MaximumStringBytes);
            reader.ReadInt32(); // m_LoadType
            int channels = reader.ReadInt32();
            int frequency = reader.ReadInt32();
            int bitsPerSample = reader.ReadInt32();
            float lengthSeconds = reader.ReadSingle();
            reader.ReadByte(); // m_IsTrackerFormat
            reader.Align(4);
            reader.ReadInt32(); // m_SubsoundIndex
            reader.ReadByte(); // m_PreloadAudioData
            reader.ReadByte(); // m_LoadInBackground
            reader.ReadByte(); // m_Legacy3D
            reader.ReadByte(); // m_Ambisonic
            string source = reader.ReadAlignedUtf8String(end, MaximumStringBytes);
            ulong offset = reader.ReadUInt64();
            ulong size = reader.ReadUInt64();
            int compressionFormat = reader.ReadInt32();
            if (channels <= 0 || channels > 8 || frequency <= 0 ||
                bitsPerSample <= 0 || bitsPerSample > 32 || reader.Position > end)
                throw new InvalidDataException("AudioClip has invalid fields: " + name);
            return new UnityAudioClipInfo(item, name, channels, frequency,
                bitsPerSample, lengthSeconds, source, offset, size, compressionFormat);
        }

        private string ResolveExternalResourcePath(string relativePath)
        {
            if (relativePath.IndexOf("://", StringComparison.Ordinal) >= 0 || Path.IsPathRooted(relativePath))
                throw new NotSupportedException("Unity archive/rooted resource paths are not supported: " + relativePath);

            string baseDirectory = Path.GetDirectoryName(filePath);
            if (baseDirectory == null) throw new InvalidDataException("Serialized file has no parent directory.");
            string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(baseDirectory, normalized));
            string allowedPrefix = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unity resource path escapes RainWorld_Data: " + relativePath);
            if (!File.Exists(candidate)) throw new FileNotFoundException("Unity texture stream was not found.", candidate);
            return candidate;
        }

        private int ReadCollectionCount(string description, int maximum)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximum)
                throw new InvalidDataException("Invalid " + description + " count: " + count);
            return count;
        }

        private long ObjectEnd(UnitySerializedObjectInfo item)
        {
            return checked(item.ByteOffset + item.ByteSize);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("UnitySerializedFileReader");
        }

        private static int CheckedInt(uint value, string message)
        {
            if (value > int.MaxValue) throw new InvalidDataException(message);
            return (int)value;
        }

        private static long CheckedLong(ulong value, string message)
        {
            if (value > long.MaxValue) throw new InvalidDataException(message);
            return (long)value;
        }

        private static void EnsureRange(long offset, uint size, long length, string message)
        {
            if (size > int.MaxValue) throw new InvalidDataException(message);
            EnsureRange(offset, (int)size, length, message);
        }

        private static void EnsureRange(long offset, int size, long length, string message)
        {
            if (offset < 0 || size < 0 || offset > length || size > length - offset)
                throw new InvalidDataException(message);
        }

        private static byte[] ReadExactly(Stream source, int count)
        {
            byte[] result = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = source.Read(result, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
            return result;
        }
    }

    internal sealed class UnityEndianReader
    {
        private readonly Stream stream;
        private readonly byte[] scratch = new byte[8];

        public UnityEndianReader(Stream stream, bool isLittleEndian)
        {
            this.stream = stream;
            IsLittleEndian = isLittleEndian;
        }

        public bool IsLittleEndian { get; set; }
        public long Position { get { return stream.Position; } set { stream.Position = value; } }

        public byte ReadByte()
        {
            int value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException();
            return (byte)value;
        }

        public byte[] ReadBytes(int count)
        {
            if (count < 0) throw new InvalidDataException("Negative byte count.");
            byte[] result = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(result, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
            return result;
        }

        public short ReadInt16() { return unchecked((short)ReadUInt16()); }
        public int ReadInt32() { return unchecked((int)ReadUInt32()); }
        public long ReadInt64() { return unchecked((long)ReadUInt64()); }

        public ushort ReadUInt16()
        {
            FillScratch(2);
            if (IsLittleEndian) return (ushort)(scratch[0] | (scratch[1] << 8));
            return (ushort)((scratch[0] << 8) | scratch[1]);
        }

        public uint ReadUInt32()
        {
            FillScratch(4);
            if (IsLittleEndian)
            {
                return (uint)(scratch[0] | (scratch[1] << 8) | (scratch[2] << 16) | (scratch[3] << 24));
            }
            return ((uint)scratch[0] << 24) | ((uint)scratch[1] << 16) | ((uint)scratch[2] << 8) | scratch[3];
        }

        public ulong ReadUInt64()
        {
            FillScratch(8);
            ulong result = 0;
            if (IsLittleEndian)
            {
                for (int i = 7; i >= 0; i--) result = (result << 8) | scratch[i];
            }
            else
            {
                for (int i = 0; i < 8; i++) result = (result << 8) | scratch[i];
            }
            return result;
        }

        public float ReadSingle()
        {
            uint bits = ReadUInt32();
            byte[] bytes = BitConverter.GetBytes(bits);
            return BitConverter.ToSingle(bytes, 0);
        }

        public string ReadNullTerminatedUtf8(int maximumBytes)
        {
            using (MemoryStream bytes = new MemoryStream())
            {
                while (true)
                {
                    byte value = ReadByte();
                    if (value == 0) return Encoding.UTF8.GetString(bytes.ToArray());
                    if (bytes.Length >= maximumBytes) throw new InvalidDataException("Unity string is unreasonably large.");
                    bytes.WriteByte(value);
                }
            }
        }

        public string ReadAlignedUtf8String(long endPosition, int maximumBytes)
        {
            int length = ReadInt32();
            if (length < 0 || length > maximumBytes || length > endPosition - Position)
                throw new InvalidDataException("Invalid Unity string length: " + length);
            string value = Encoding.UTF8.GetString(ReadBytes(length));
            Align(4);
            if (Position > endPosition) throw new InvalidDataException("Aligned Unity string extends beyond its object.");
            return value;
        }

        public byte[] ReadSizedByteArray(long endPosition)
        {
            int length = ReadInt32();
            if (length < 0 || length > endPosition - Position)
                throw new InvalidDataException("Invalid Unity byte-array length: " + length);
            byte[] value = ReadBytes(length);
            Align(4);
            if (Position > endPosition) throw new InvalidDataException("Aligned Unity byte array extends beyond its object.");
            return value;
        }

        public void Align(int alignment)
        {
            long remainder = Position % alignment;
            if (remainder != 0) Position += alignment - remainder;
        }

        private void FillScratch(int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(scratch, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }
    }
}
