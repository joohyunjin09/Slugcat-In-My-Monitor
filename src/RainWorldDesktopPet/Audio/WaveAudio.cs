using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
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
        private sealed class PlaybackCommand
        {
            public MeowSoundVariation Variation;
            public bool Stop;
            public int FadeOutMilliseconds;
        }

        private static int aliasCounter;
        private readonly WorkshopLog log;
        private readonly object commandSync = new object();
        private readonly Queue<PlaybackCommand> commands = new Queue<PlaybackCommand>();
        private readonly AutoResetEvent commandSignal = new AutoResetEvent(false);
        private readonly Thread worker;
        private SoundPlayer player;
        private string mciAlias;
        private int mciVolume;
        private volatile string lastEvent = "none";
        private volatile bool stopping;
        private bool disposed;

        public WorkshopAudioPlayer(WorkshopLog log)
        {
            this.log = log ?? new WorkshopLog(false);
            worker = new Thread(WorkerMain);
            worker.IsBackground = true;
            worker.Name = "Push To Meow playback";
            worker.Start();
        }

        internal string LastEvent { get { return lastEvent; } }

        public bool TryPlay(MeowSoundVariation variation)
        {
            if (variation == null || string.IsNullOrWhiteSpace(variation.FilePath) ||
                stopping) return false;
            lock (commandSync)
            {
                // Only the newest contextual meow matters if the audio device
                // is still opening the preceding request.
                commands.Clear();
                commands.Enqueue(new PlaybackCommand { Variation = variation });
            }
            lastEvent = "queued: " + variation.AssetName;
            commandSignal.Set();
            return true;
        }

        private void WorkerMain()
        {
            while (!stopping)
            {
                PlaybackCommand command = null;
                lock (commandSync)
                    if (commands.Count > 0) command = commands.Dequeue();
                if (command == null)
                {
                    commandSignal.WaitOne(250);
                    continue;
                }
                if (command.Stop) StopCore();
                else if (command.FadeOutMilliseconds > 0) FadeOutCore(command.FadeOutMilliseconds);
                else PlayCore(command.Variation);
            }
            StopCore();
        }

        private void PlayCore(MeowSoundVariation variation)
        {
            try
            {
                StopCore();
                if (!File.Exists(variation.FilePath))
                    throw new FileNotFoundException("Workshop sound file is missing",
                        variation.FilePath);
                mciAlias = "rwptm_" + System.Diagnostics.Process.GetCurrentProcess().Id + "_" +
                    Interlocked.Increment(ref aliasCounter);
                uint open = MciSendString("open \"" + variation.FilePath + "\" type waveaudio alias " +
                    mciAlias, null, 0, IntPtr.Zero);
                if (open == 0)
                {
                    int volume = (int)Math.Round(Math.Max(0f, Math.Min(1f,
                        variation.PlaybackVolume)) * 1000f);
                    mciVolume = volume;
                    MciSendString("setaudio " + mciAlias + " volume to " + volume,
                        null, 0, IntPtr.Zero);
                    int speed = (int)Math.Round(Math.Max(0.5f, Math.Min(2f,
                        variation.PlaybackPitch)) * 1000f);
                    uint speedResult = MciSendString("set " + mciAlias + " speed " + speed,
                        null, 0, IntPtr.Zero);
                    if (speedResult != 0) variation.PlaybackPitch = 1f;
                    uint play = MciSendString("play " + mciAlias + " from 0", null, 0, IntPtr.Zero);
                    if (play == 0)
                    {
                        lastEvent = "playback started: " + variation.AssetName;
                        LogDiagnostic("[Audio] Playback started: PushToMeow -> " +
                            variation.FilePath);
                        return;
                    }
                    MciSendString("close " + mciAlias, null, 0, IntPtr.Zero);
                    mciAlias = null;
                }

                // Some Wine/Windows audio configurations do not expose MCI waveaudio.
                // SoundPlayer remains a safe raw-WAV fallback without external libraries.
                variation.PlaybackPitch = 1f;
                player = new SoundPlayer(variation.FilePath);
                player.Load();
                player.Play();
                lastEvent = "playback started: " + variation.AssetName;
                LogDiagnostic("[Audio] Playback started: PushToMeow -> " +
                    variation.FilePath + " [SoundPlayer fallback]");
            }
            catch (Exception exception)
            {
                string message = "[Audio] Failed to load/play PushToMeow " +
                    variation.FilePath + ": " + exception.Message;
                lastEvent = "playback failed: " + variation.AssetName + " (" +
                    exception.Message + ")";
                log.Warning("PushToMeow", message);
                LogDiagnostic(message);
                StopCore();
            }
        }

        public void Stop()
        {
            if (stopping) return;
            lock (commandSync)
            {
                commands.Clear();
                commands.Enqueue(new PlaybackCommand { Stop = true });
            }
            commandSignal.Set();
        }

        public void FadeOut(int milliseconds)
        {
            if (stopping || milliseconds <= 0) return;
            lock (commandSync)
            {
                commands.Enqueue(new PlaybackCommand
                {
                    FadeOutMilliseconds = Math.Max(1, Math.Min(milliseconds, 250))
                });
            }
            commandSignal.Set();
        }

        private void FadeOutCore(int milliseconds)
        {
            if (string.IsNullOrEmpty(mciAlias))
            {
                // SoundPlayer has no volume API; its only safe release is a
                // stop. MCI is the normal path for the workshop WAV assets.
                StopCore();
                return;
            }
            const int steps = 6;
            int delay = Math.Max(1, milliseconds / steps);
            int startingVolume = mciVolume;
            for (int step = 1; step <= steps && !stopping; step++)
            {
                int volume = startingVolume * (steps - step) / steps;
                MciSendString("setaudio " + mciAlias + " volume to " + volume,
                    null, 0, IntPtr.Zero);
                Thread.Sleep(delay);
            }
            StopCore();
        }

        private void StopCore()
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
                mciVolume = 0;
            }
            if (player == null) return;
            try { player.Stop(); }
            catch (Exception) { }
            player.Dispose();
            player = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stopping = true;
            commandSignal.Set();
            if (worker.IsAlive) worker.Join(2000);
            commandSignal.Dispose();
        }

        private static void LogDiagnostic(string message)
        {
            Debug.WriteLine(message);
            Trace.WriteLine(message);
        }

        [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
        private static extern uint MciSendString(string command, StringBuilder returnValue,
            int returnLength, IntPtr callback);
    }
}
