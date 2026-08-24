using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using RainWorldDesktopPet.Workshop;

namespace RainWorldDesktopPet.Audio
{
    public static class WaveAudio
    {
        public static bool TryReadDuration(string path, out double seconds)
        {
            seconds = 0.0;
            if (!Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (new string(reader.ReadChars(4)) != "RIFF") return false;
                    reader.ReadUInt32();
                    if (new string(reader.ReadChars(4)) != "WAVE") return false;
                    uint byteRate = 0;
                    long dataBytes = 0;
                    bool foundData = false;
                    while (stream.Position + 8 <= stream.Length)
                    {
                        string chunk = new string(reader.ReadChars(4));
                        uint size = reader.ReadUInt32();
                        long next = stream.Position + size + (size & 1);
                        if (next > stream.Length + 1) return false;
                        if (chunk == "fmt " && size >= 16)
                        {
                            reader.ReadUInt16();
                            reader.ReadUInt16();
                            reader.ReadUInt32();
                            byteRate = reader.ReadUInt32();
                        }
                        else if (chunk == "data")
                        {
                            dataBytes += size;
                            foundData = true;
                        }
                        stream.Position = Math.Min(next, stream.Length);
                    }
                    if (byteRate == 0 || !foundData || dataBytes <= 0) return false;
                    seconds = dataBytes / (double)byteRate;
                    return seconds > 0.0 && seconds < 600.0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public sealed class WorkshopAudioPlayer : IDisposable
    {
        private static int aliasCounter;
        private readonly WorkshopLog log;
        private SoundPlayer player;
        private string mciAlias;

        public WorkshopAudioPlayer(WorkshopLog log)
        {
            this.log = log ?? new WorkshopLog(false);
        }

        public bool TryPlay(MeowSoundVariation variation)
        {
            if (variation == null || !File.Exists(variation.FilePath)) return false;
            try
            {
                Stop();
                mciAlias = "rwptm_" + System.Diagnostics.Process.GetCurrentProcess().Id + "_" +
                    Interlocked.Increment(ref aliasCounter);
                uint open = MciSendString("open \"" + variation.FilePath + "\" type waveaudio alias " +
                    mciAlias, null, 0, IntPtr.Zero);
                if (open == 0)
                {
                    int volume = (int)Math.Round(Math.Max(0f, Math.Min(1f,
                        variation.PlaybackVolume)) * 1000f);
                    MciSendString("setaudio " + mciAlias + " volume to " + volume,
                        null, 0, IntPtr.Zero);
                    int speed = (int)Math.Round(Math.Max(0.5f, Math.Min(2f,
                        variation.PlaybackPitch)) * 1000f);
                    uint speedResult = MciSendString("set " + mciAlias + " speed " + speed,
                        null, 0, IntPtr.Zero);
                    if (speedResult != 0) variation.PlaybackPitch = 1f;
                    uint play = MciSendString("play " + mciAlias + " from 0", null, 0, IntPtr.Zero);
                    if (play == 0) return true;
                    MciSendString("close " + mciAlias, null, 0, IntPtr.Zero);
                    mciAlias = null;
                }

                // Some Wine/Windows audio configurations do not expose MCI waveaudio.
                // SoundPlayer remains a safe raw-WAV fallback without external libraries.
                variation.PlaybackPitch = 1f;
                player = new SoundPlayer(variation.FilePath);
                player.Load();
                player.Play();
                return true;
            }
            catch (Exception exception)
            {
                log.Warning("PushToMeow", "Could not play " + variation.FilePath + ": " +
                    exception.Message);
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            if (!string.IsNullOrEmpty(mciAlias))
            {
                try
                {
                    MciSendString("stop " + mciAlias, null, 0, IntPtr.Zero);
                    MciSendString("close " + mciAlias, null, 0, IntPtr.Zero);
                }
                catch (Exception) { }
                mciAlias = null;
            }
            if (player == null) return;
            try { player.Stop(); }
            catch (Exception) { }
            player.Dispose();
            player = null;
        }

        public void Dispose()
        {
            Stop();
        }

        [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
        private static extern uint MciSendString(string command, StringBuilder returnValue,
            int returnLength, IntPtr callback);
    }
}
