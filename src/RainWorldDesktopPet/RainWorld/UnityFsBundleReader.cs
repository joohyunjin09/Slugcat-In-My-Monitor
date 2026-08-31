using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RainWorldDesktopPet.RainWorld
{
    public sealed class UnityFsNodeInfo
    {
        internal UnityFsNodeInfo(long offset, long size, string path)
        {
            Offset = offset;
            Size = size;
            Path = path;
        }

        public readonly long Offset;
        public readonly long Size;
        public readonly string Path;
    }

    // Minimal UnityFS v8 reader for Rain World's local sound AssetBundle.
    // Data blocks are decompressed on demand; the one-gigabyte resource node
    // is never copied wholesale or retained in memory.
    public sealed class UnityFsBundleReader : IDisposable
    {
        private const long MaximumBlockCacheBytes = 8L * 1024L * 1024L;

        private sealed class BlockInfo
        {
            public uint UncompressedSize;
            public uint CompressedSize;
            public ushort Flags;
            public long CompressedOffset;
            public long UncompressedOffset;
        }

        private readonly FileStream stream;
        private readonly List<BlockInfo> blocks = new List<BlockInfo>();
        private readonly List<UnityFsNodeInfo> nodes = new List<UnityFsNodeInfo>();
        private readonly Dictionary<int, byte[]> blockCache = new Dictionary<int, byte[]>();
        private readonly Queue<int> cacheOrder = new Queue<int>();
        private readonly object sync = new object();
        private long blockCacheBytes;

        public UnityFsBundleReader(string path)
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
            try { ReadHeader(); }
            catch { stream.Dispose(); throw; }
        }

        public IList<UnityFsNodeInfo> Nodes { get { return nodes.AsReadOnly(); } }

        public UnityFsNodeInfo FindSerializedFileNode()
        {
            UnityFsNodeInfo best = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                string extension = Path.GetExtension(nodes[i].Path);
                if (string.Equals(extension, ".resource", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".resS", StringComparison.OrdinalIgnoreCase)) continue;
                if (best == null || nodes[i].Size < best.Size) best = nodes[i];
            }
            return best;
        }

        public UnityFsNodeInfo FindResourceNode(string source)
        {
            string fileName = Path.GetFileName((source ?? string.Empty).Replace('/',
                Path.DirectorySeparatorChar));
            for (int i = 0; i < nodes.Count; i++)
            {
                if (string.Equals(Path.GetFileName(nodes[i].Path), fileName,
                    StringComparison.OrdinalIgnoreCase)) return nodes[i];
            }
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].Path.EndsWith(".resource", StringComparison.OrdinalIgnoreCase))
                    return nodes[i];
            return null;
        }

        public byte[] ReadNode(UnityFsNodeInfo node)
        {
            if (node == null) throw new ArgumentNullException("node");
            if (node.Size > int.MaxValue) throw new InvalidDataException("UnityFS node is too large.");
            return ReadNodeRange(node, 0, (int)node.Size);
        }

        public byte[] ReadNodeRange(UnityFsNodeInfo node, long offset, int count)
        {
            if (node == null) throw new ArgumentNullException("node");
            if (offset < 0 || count < 0 || offset > node.Size || count > node.Size - offset)
                throw new ArgumentOutOfRangeException("offset");
            byte[] result = new byte[count];
            long archiveOffset = node.Offset + offset;
            int resultOffset = 0;
            lock (sync)
            {
                for (int i = 0; i < blocks.Count && resultOffset < count; i++)
                {
                    BlockInfo block = blocks[i];
                    long blockEnd = block.UncompressedOffset + block.UncompressedSize;
                    if (archiveOffset >= blockEnd) continue;
                    if (archiveOffset + (count - resultOffset) <= block.UncompressedOffset) break;
                    byte[] data = ReadBlock(i);
                    int sourceOffset = (int)Math.Max(0L, archiveOffset - block.UncompressedOffset);
                    int take = Math.Min(data.Length - sourceOffset, count - resultOffset);
                    if (take <= 0) continue;
                    Buffer.BlockCopy(data, sourceOffset, result, resultOffset, take);
                    resultOffset += take;
                    archiveOffset += take;
                }
            }
            if (resultOffset != count) throw new EndOfStreamException("UnityFS node range is incomplete.");
            return result;
        }

        private void ReadHeader()
        {
            string signature = ReadNullString(stream);
            if (signature != "UnityFS") throw new InvalidDataException("Not a UnityFS bundle.");
            uint version = ReadUInt32Big(stream);
            ReadNullString(stream); // player version
            ReadNullString(stream); // engine version
            long declaredSize = ReadInt64Big(stream);
            uint compressedInfoSize = ReadUInt32Big(stream);
            uint uncompressedInfoSize = ReadUInt32Big(stream);
            uint flags = ReadUInt32Big(stream);
            if (version < 6 || declaredSize > stream.Length)
                throw new InvalidDataException("Unsupported or truncated UnityFS bundle.");

            if (version >= 7) AlignStream(stream, 16);
            long infoPosition = (flags & 0x80) != 0
                ? stream.Length - compressedInfoSize : stream.Position;
            stream.Position = infoPosition;
            byte[] compressedInfo = ReadExactly(stream, CheckedInt(compressedInfoSize));
            byte[] info = Decompress(compressedInfo, CheckedInt(uncompressedInfoSize),
                (int)(flags & 0x3f));
            using (MemoryStream metadata = new MemoryStream(info, false))
            {
                ReadExactly(metadata, 16); // bundle hash
                int blockCount = ReadInt32Big(metadata);
                long uncompressedOffset = 0;
                for (int i = 0; i < blockCount; i++)
                {
                    BlockInfo block = new BlockInfo();
                    block.UncompressedSize = ReadUInt32Big(metadata);
                    block.CompressedSize = ReadUInt32Big(metadata);
                    block.Flags = ReadUInt16Big(metadata);
                    block.UncompressedOffset = uncompressedOffset;
                    uncompressedOffset += block.UncompressedSize;
                    blocks.Add(block);
                }
                int nodeCount = ReadInt32Big(metadata);
                for (int i = 0; i < nodeCount; i++)
                {
                    long offset = ReadInt64Big(metadata);
                    long size = ReadInt64Big(metadata);
                    ReadUInt32Big(metadata);
                    nodes.Add(new UnityFsNodeInfo(offset, size, ReadNullString(metadata)));
                }
            }

            long dataPosition;
            if ((flags & 0x80) != 0)
            {
                stream.Position = HeaderEnd(version);
                dataPosition = stream.Position;
            }
            else
            {
                dataPosition = infoPosition + compressedInfoSize;
            }
            if ((flags & 0x200) != 0) dataPosition = AlignValue(dataPosition, 16);
            long compressedOffset = dataPosition;
            for (int i = 0; i < blocks.Count; i++)
            {
                blocks[i].CompressedOffset = compressedOffset;
                compressedOffset += blocks[i].CompressedSize;
            }
            if (compressedOffset > stream.Length)
                throw new InvalidDataException("UnityFS block table extends beyond the bundle.");
        }

        private long HeaderEnd(uint version)
        {
            stream.Position = 0;
            ReadNullString(stream);
            ReadUInt32Big(stream);
            ReadNullString(stream);
            ReadNullString(stream);
            ReadInt64Big(stream);
            ReadUInt32Big(stream);
            ReadUInt32Big(stream);
            ReadUInt32Big(stream);
            if (version >= 7) AlignStream(stream, 16);
            return stream.Position;
        }

        private byte[] ReadBlock(int index)
        {
            byte[] cached;
            if (blockCache.TryGetValue(index, out cached)) return cached;
            BlockInfo block = blocks[index];
            stream.Position = block.CompressedOffset;
            byte[] compressed = ReadExactly(stream, CheckedInt(block.CompressedSize));
            byte[] data = Decompress(compressed, CheckedInt(block.UncompressedSize),
                block.Flags & 0x3f);
            blockCache[index] = data;
            cacheOrder.Enqueue(index);
            blockCacheBytes += data.Length;
            // Keep at most one oversized archive block. Rain World's one-gigabyte
            // sound bundle must never turn into a proportional managed cache.
            while (blockCacheBytes > MaximumBlockCacheBytes && cacheOrder.Count > 1)
            {
                int expired = cacheOrder.Dequeue();
                byte[] removed;
                if (blockCache.TryGetValue(expired, out removed))
                {
                    blockCache.Remove(expired);
                    blockCacheBytes -= removed.Length;
                }
            }
            return data;
        }

        private static byte[] Decompress(byte[] input, int outputSize, int compression)
        {
            if (compression == 0)
            {
                if (input.Length != outputSize) throw new InvalidDataException("UnityFS raw block size mismatch.");
                return input;
            }
            if (compression != 2 && compression != 3)
                throw new NotSupportedException("UnityFS compression " + compression + " is unsupported.");
            byte[] output = new byte[outputSize];
            int source = 0;
            int destination = 0;
            while (source < input.Length)
            {
                int token = input[source++];
                int literalLength = token >> 4;
                if (literalLength == 15)
                {
                    int value;
                    do { value = input[source++]; literalLength += value; } while (value == 255);
                }
                if (source + literalLength > input.Length || destination + literalLength > output.Length)
                    throw new InvalidDataException("Invalid LZ4 literal length.");
                Buffer.BlockCopy(input, source, output, destination, literalLength);
                source += literalLength;
                destination += literalLength;
                if (source >= input.Length) break;
                if (source + 2 > input.Length) throw new InvalidDataException("Invalid LZ4 offset.");
                int offset = input[source] | (input[source + 1] << 8);
                source += 2;
                if (offset <= 0 || offset > destination) throw new InvalidDataException("Invalid LZ4 back-reference.");
                int matchLength = token & 0x0f;
                if (matchLength == 15)
                {
                    int value;
                    do { value = input[source++]; matchLength += value; } while (value == 255);
                }
                matchLength += 4;
                if (destination + matchLength > output.Length)
                    throw new InvalidDataException("Invalid LZ4 match length.");
                int copy = destination - offset;
                for (int i = 0; i < matchLength; i++) output[destination++] = output[copy++];
            }
            if (destination != output.Length) throw new InvalidDataException("LZ4 block ended early.");
            return output;
        }

        private static string ReadNullString(Stream source)
        {
            using (MemoryStream value = new MemoryStream())
            {
                int current;
                while ((current = source.ReadByte()) > 0) value.WriteByte((byte)current);
                if (current < 0) throw new EndOfStreamException();
                return Encoding.UTF8.GetString(value.ToArray());
            }
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

        private static ushort ReadUInt16Big(Stream source)
        {
            int a = source.ReadByte(); int b = source.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            return (ushort)((a << 8) | b);
        }

        private static uint ReadUInt32Big(Stream source)
        {
            byte[] bytes = ReadExactly(source, 4);
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
                ((uint)bytes[2] << 8) | bytes[3];
        }

        private static int ReadInt32Big(Stream source) { return unchecked((int)ReadUInt32Big(source)); }

        private static long ReadInt64Big(Stream source)
        {
            ulong value = ((ulong)ReadUInt32Big(source) << 32) | ReadUInt32Big(source);
            return unchecked((long)value);
        }

        private static int CheckedInt(uint value)
        {
            if (value > int.MaxValue) throw new InvalidDataException("UnityFS allocation is too large.");
            return (int)value;
        }

        private static long AlignValue(long value, int alignment)
        {
            long remainder = value % alignment;
            return remainder == 0 ? value : value + alignment - remainder;
        }

        private static void AlignStream(Stream source, int alignment)
        {
            source.Position = AlignValue(source.Position, alignment);
        }

        public void TrimBlockCache()
        {
            lock (sync)
            {
                blockCache.Clear();
                cacheOrder.Clear();
                blockCacheBytes = 0;
            }
        }

        public void Dispose()
        {
            TrimBlockCache();
            stream.Dispose();
        }
    }
}
