using System;
using System.IO;

namespace RainWorldDesktopPet.Audio
{
    internal static class WavPcmReader
    {
        internal static bool TryLoad(string path, string name,
            out RainWorldPcmClip clip, out string reason)
        {
            clip = null;
            reason = null;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length < 44 || ReadFourCc(data, 0) != "RIFF" ||
                    ReadFourCc(data, 8) != "WAVE")
                {
                    reason = "not a RIFF/WAVE file";
                    return false;
                }

                int channels = 0;
                int sampleRate = 0;
                int bits = 0;
                int dataOffset = 0;
                int dataLength = 0;
                int offset = 12;
                while (offset + 8 <= data.Length)
                {
                    string chunk = ReadFourCc(data, offset);
                    uint unsignedSize = ReadUInt32(data, offset + 4);
                    if (unsignedSize > int.MaxValue) break;
                    int size = (int)unsignedSize;
                    int content = offset + 8;
                    if (content > data.Length || size > data.Length - content) break;
                    if (chunk == "fmt " && size >= 16)
                    {
                        int format = ReadUInt16(data, content);
                        if (format != 1)
                        {
                            reason = "unsupported WAV format " + format;
                            return false;
                        }
                        channels = ReadUInt16(data, content + 2);
                        sampleRate = (int)ReadUInt32(data, content + 4);
                        bits = ReadUInt16(data, content + 14);
                    }
                    else if (chunk == "data")
                    {
                        dataOffset = content;
                        dataLength = size;
                    }
                    offset = content + size + (size & 1);
                }

                if ((channels != 1 && channels != 2) || sampleRate < 8000 ||
                    sampleRate > 192000 || bits != 16 || dataLength <= 0)
                {
                    reason = "WAV must be mono/stereo PCM16 at 8-192 kHz";
                    return false;
                }
                int blockAlign = channels * 2;
                dataLength -= dataLength % blockAlign;
                clip = new RainWorldPcmClip(name, data, dataOffset, dataLength,
                    channels, sampleRate, bits);
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        private static string ReadFourCc(byte[] data, int offset)
        {
            return new string(new[] { (char)data[offset], (char)data[offset + 1],
                (char)data[offset + 2], (char)data[offset + 3] });
        }

        private static int ReadUInt16(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) |
                (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }
    }
}
