using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using RainWorldDesktopPet.Desktop;

namespace RainWorldDesktopPet.Audio
{
    // A voice is only a cursor into a shared cached PCM array. It never opens a
    // device and never copies or pins the source clip.
    internal sealed class WaveOutVoice
    {
        private readonly RainWorldPcmClip clip;
        private readonly int sourceFrameCount;
        private readonly double sourceStep;
        private readonly double leftGain;
        private readonly double rightGain;
        private readonly bool loop;
        private double sourceFrame;
        private bool done;
        private bool stopping;
        private int stopFramesRemaining;
        private int stopFramesTotal;

        internal WaveOutVoice(RainWorldPcmClip clip, double volume,
            double pan, double pitch, bool loop, int outputSampleRate)
        {
            if (clip == null) throw new ArgumentNullException("clip");
            if (clip.Data == null || clip.BitsPerSample != 16 ||
                clip.Channels < 1 || clip.SampleRate < 1 ||
                clip.DataOffset < 0 || clip.DataLength < clip.Channels * 2 ||
                clip.DataOffset + (long)clip.DataLength > clip.Data.Length)
                throw new ArgumentException("invalid PCM16 clip", "clip");
            if (outputSampleRate < 1)
                throw new ArgumentOutOfRangeException("outputSampleRate");

            this.clip = clip;
            this.loop = loop;
            sourceFrameCount = clip.DataLength / (clip.Channels * 2);
            sourceStep = clip.SampleRate * Math.Max(0.01, pitch) /
                outputSampleRate;
            volume = Math.Max(0.0, Math.Min(1.0, volume));
            pan = Math.Max(-1.0, Math.Min(1.0, pan));
            leftGain = volume * (pan > 0.0 ? 1.0 - pan : 1.0);
            rightGain = volume * (pan < 0.0 ? 1.0 + pan : 1.0);
        }

        internal bool IsDone { get { return done; } }
        internal bool IsStopping { get { return stopping && !done; } }

        internal void BeginStop(int milliseconds, int outputSampleRate)
        {
            if (done || stopping) return;
            if (milliseconds <= 0)
            {
                done = true;
                return;
            }
            stopping = true;
            stopFramesTotal = Math.Max(1, (int)Math.Round(
                outputSampleRate * milliseconds / 1000.0));
            stopFramesRemaining = stopFramesTotal;
        }

        internal void MixInto(double[] left, double[] right, int frameCount,
            double masterGain)
        {
            if (done || frameCount <= 0) return;
            double effectiveLeft = Math.Min(1.0,
                leftGain * Math.Max(0.0, masterGain));
            double effectiveRight = Math.Min(1.0,
                rightGain * Math.Max(0.0, masterGain));
            for (int outputFrame = 0; outputFrame < frameCount; outputFrame++)
            {
                if (sourceFrame >= sourceFrameCount)
                {
                    if (!loop)
                    {
                        done = true;
                        break;
                    }
                    sourceFrame %= sourceFrameCount;
                }

                int frame0 = (int)sourceFrame;
                int frame1 = frame0 + 1;
                if (frame1 >= sourceFrameCount)
                    frame1 = loop ? 0 : frame0;
                double fraction = sourceFrame - frame0;
                double sampleLeft = Interpolate(frame0, frame1, 0, fraction);
                double sampleRight = clip.Channels == 1
                    ? sampleLeft
                    : Interpolate(frame0, frame1, 1, fraction);
                double envelope = 1.0;
                if (stopping)
                {
                    envelope = stopFramesRemaining /
                        (double)Math.Max(1, stopFramesTotal);
                    stopFramesRemaining--;
                    if (stopFramesRemaining <= 0) done = true;
                }
                left[outputFrame] += sampleLeft * effectiveLeft * envelope;
                right[outputFrame] += sampleRight * effectiveRight * envelope;
                sourceFrame += sourceStep;
                if (done) break;
                if (!loop && sourceFrame >= sourceFrameCount) done = true;
            }
        }

        private double Interpolate(int frame0, int frame1, int channel,
            double fraction)
        {
            double a = ReadSample(frame0, channel);
            double b = ReadSample(frame1, channel);
            return a + (b - a) * fraction;
        }

        private double ReadSample(int frame, int channel)
        {
            int sourceChannel = Math.Min(channel, clip.Channels - 1);
            int offset = clip.DataOffset +
                (frame * clip.Channels + sourceChannel) * 2;
            short sample = unchecked((short)(clip.Data[offset] |
                (clip.Data[offset + 1] << 8)));
            return sample / 32768.0;
        }
    }

    // One process-wide output device mixes every active voice into a small
    // reusable ring. This avoids opening/closing dozens of WinMM handles when
    // eight pets produce footsteps, meows, and layered abilities together.
    internal sealed class WaveOutMixer : IDisposable
    {
        private sealed class OutputBuffer
        {
            internal byte[] Data;
            internal GCHandle DataHandle;
            internal IntPtr HeaderPointer;
            internal bool Prepared;
            internal bool Submitted;
        }

        internal const int OutputSampleRate = 48000;
        internal const int OutputChannels = 2;
        internal const int OutputBitsPerSample = 16;
        internal const int BufferMilliseconds = 10;
        internal const int OutputBufferCount = 4;
        internal const double LimiterPeak = 0.95;
        private const double LimiterReleasePerBuffer = 0.025;
        private static readonly int OutputFramesPerBuffer =
            OutputSampleRate * BufferMilliseconds / 1000;
        private static readonly int OutputBytesPerBuffer =
            OutputFramesPerBuffer * OutputChannels * 2;

        private readonly object sync = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly List<WaveOutVoice> voices = new List<WaveOutVoice>();
        private readonly OutputBuffer[] buffers =
            new OutputBuffer[OutputBufferCount];
        private readonly double[] mixLeft = new double[OutputFramesPerBuffer];
        private readonly double[] mixRight = new double[OutputFramesPerBuffer];
        private Thread thread;
        private IntPtr waveOut;
        private volatile bool stopping;
        private bool disposed;
        private double masterGain;
        private double limiterGain = 1.0;
        private long renderedBufferCount;
        private int peakActiveVoiceCount;
        private long maximumRenderTicks;
        private string lastError;

        internal WaveOutMixer(double masterGain)
        {
            this.masterGain = Math.Max(0.0, masterGain);
            NativeMethods.WAVEFORMATEX format = new NativeMethods.WAVEFORMATEX();
            format.wFormatTag = NativeMethods.WAVE_FORMAT_PCM;
            format.nChannels = (ushort)OutputChannels;
            format.nSamplesPerSec = (uint)OutputSampleRate;
            format.wBitsPerSample = (ushort)OutputBitsPerSample;
            format.nBlockAlign = (ushort)(OutputChannels * 2);
            format.nAvgBytesPerSec = (uint)(OutputSampleRate * format.nBlockAlign);
            format.cbSize = 0;

            int result = NativeMethods.waveOutOpen(out waveOut, new IntPtr(-1),
                ref format, wake.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero,
                NativeMethods.CALLBACK_EVENT);
            if (result != 0)
            {
                waveOut = IntPtr.Zero;
                throw new InvalidOperationException("waveOutOpen mixer failed: " + result);
            }
            try
            {
                NativeMethods.waveOutSetVolume(waveOut, uint.MaxValue);
                int headerSize = Marshal.SizeOf(typeof(NativeMethods.WAVEHDR));
                for (int i = 0; i < buffers.Length; i++)
                {
                    OutputBuffer buffer = new OutputBuffer();
                    buffer.Data = new byte[OutputBytesPerBuffer];
                    buffer.DataHandle = GCHandle.Alloc(buffer.Data,
                        GCHandleType.Pinned);
                    NativeMethods.WAVEHDR header = new NativeMethods.WAVEHDR();
                    header.lpData = buffer.DataHandle.AddrOfPinnedObject();
                    header.dwBufferLength = buffer.Data.Length;
                    buffer.HeaderPointer = Marshal.AllocHGlobal(headerSize);
                    Marshal.StructureToPtr(header, buffer.HeaderPointer, false);
                    result = NativeMethods.waveOutPrepareHeader(waveOut,
                        buffer.HeaderPointer, headerSize);
                    if (result != 0)
                        throw new InvalidOperationException(
                            "waveOutPrepareHeader mixer failed: " + result);
                    buffer.Prepared = true;
                    buffers[i] = buffer;
                }
                thread = new Thread(MixerMain);
                thread.IsBackground = true;
                thread.Name = "Rain World audio mixer";
                thread.Priority = ThreadPriority.AboveNormal;
                thread.Start();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal int DeviceCount { get { return waveOut == IntPtr.Zero ? 0 : 1; } }
        internal long RenderedBufferCount
        {
            get { return Interlocked.Read(ref renderedBufferCount); }
        }
        internal int PeakActiveVoiceCount
        {
            get { return Thread.VolatileRead(ref peakActiveVoiceCount); }
        }
        internal double MaximumRenderMilliseconds
        {
            get
            {
                return Interlocked.Read(ref maximumRenderTicks) * 1000.0 /
                    Stopwatch.Frequency;
            }
        }
        internal string LastError
        {
            get { lock (sync) return lastError; }
        }
        internal int ActiveVoiceCount
        {
            get { lock (sync) return voices.Count; }
        }

        internal void AddVoice(WaveOutVoice voice)
        {
            if (voice == null) throw new ArgumentNullException("voice");
            lock (sync)
            {
                if (disposed || stopping || waveOut == IntPtr.Zero)
                    throw new InvalidOperationException("audio mixer is unavailable");
                voices.Add(voice);
                UpdateMaximum(ref peakActiveVoiceCount, voices.Count);
            }
            wake.Set();
        }

        internal void BeginStop(WaveOutVoice voice, int milliseconds)
        {
            if (voice == null) return;
            lock (sync) voice.BeginStop(milliseconds, OutputSampleRate);
            wake.Set();
        }

        internal void SetMasterGain(double value)
        {
            lock (sync) masterGain = Math.Max(0.0, value);
        }

        internal void StopAll()
        {
            lock (sync)
            {
                voices.Clear();
                limiterGain = 1.0;
                Interlocked.Exchange(ref peakActiveVoiceCount, 0);
                Interlocked.Exchange(ref maximumRenderTicks, 0L);
                if (waveOut != IntPtr.Zero)
                    NativeMethods.waveOutReset(waveOut);
                for (int i = 0; i < buffers.Length; i++)
                    if (buffers[i] != null) buffers[i].Submitted = false;
            }
            wake.Set();
        }

        internal static double CalculateLimiterTarget(double peak)
        {
            peak = Math.Abs(peak);
            return peak <= LimiterPeak || peak <= 0.000001
                ? 1.0
                : LimiterPeak / peak;
        }

        private void MixerMain()
        {
            try
            {
                while (!stopping)
                {
                    bool wrote = false;
                    lock (sync)
                    {
                        for (int i = 0; i < buffers.Length && !stopping; i++)
                        {
                            OutputBuffer buffer = buffers[i];
                            if (buffer == null) continue;
                            if (buffer.Submitted && !HeaderIsDone(buffer)) continue;
                            buffer.Submitted = false;
                            if (voices.Count == 0) continue;
                            long renderStarted = Stopwatch.GetTimestamp();
                            Render(buffer.Data);
                            UpdateMaximum(ref maximumRenderTicks,
                                Stopwatch.GetTimestamp() - renderStarted);
                            int result = NativeMethods.waveOutWrite(waveOut,
                                buffer.HeaderPointer,
                                Marshal.SizeOf(typeof(NativeMethods.WAVEHDR)));
                            if (result != 0)
                                throw new InvalidOperationException(
                                    "waveOutWrite mixer failed: " + result);
                            buffer.Submitted = true;
                            Interlocked.Increment(ref renderedBufferCount);
                            wrote = true;
                        }
                    }
                    if (!wrote) wake.WaitOne(20);
                }
            }
            catch (Exception exception)
            {
                lock (sync) lastError = exception.Message;
                stopping = true;
            }
        }

        private bool HeaderIsDone(OutputBuffer buffer)
        {
            NativeMethods.WAVEHDR header = (NativeMethods.WAVEHDR)
                Marshal.PtrToStructure(buffer.HeaderPointer,
                    typeof(NativeMethods.WAVEHDR));
            return (header.dwFlags & NativeMethods.WHDR_DONE) != 0;
        }

        private void Render(byte[] output)
        {
            Array.Clear(mixLeft, 0, mixLeft.Length);
            Array.Clear(mixRight, 0, mixRight.Length);
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                WaveOutVoice voice = voices[i];
                voice.MixInto(mixLeft, mixRight, OutputFramesPerBuffer,
                    masterGain);
                if (voice.IsDone) voices.RemoveAt(i);
            }

            double peak = 0.0;
            for (int i = 0; i < OutputFramesPerBuffer; i++)
                peak = Math.Max(peak,
                    Math.Max(Math.Abs(mixLeft[i]), Math.Abs(mixRight[i])));
            double target = CalculateLimiterTarget(peak);
            if (target < limiterGain)
                limiterGain = target;
            else
                limiterGain = Math.Min(target,
                    limiterGain + LimiterReleasePerBuffer);

            int offset = 0;
            for (int i = 0; i < OutputFramesPerBuffer; i++)
            {
                WriteSample(output, ref offset, mixLeft[i] * limiterGain);
                WriteSample(output, ref offset, mixRight[i] * limiterGain);
            }
        }

        private static void WriteSample(byte[] output, ref int offset,
            double sample)
        {
            sample = Math.Max(-1.0, Math.Min(1.0, sample));
            short value = (short)Math.Round(sample *
                (sample < 0.0 ? 32768.0 : 32767.0));
            output[offset++] = (byte)(value & 0xff);
            output[offset++] = (byte)((value >> 8) & 0xff);
        }

        private static void UpdateMaximum(ref int target, int value)
        {
            int current = Thread.VolatileRead(ref target);
            while (value > current)
            {
                int observed = Interlocked.CompareExchange(ref target, value,
                    current);
                if (observed == current) return;
                current = observed;
            }
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long current = Interlocked.Read(ref target);
            while (value > current)
            {
                long observed = Interlocked.CompareExchange(ref target, value,
                    current);
                if (observed == current) return;
                current = observed;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stopping = true;
            wake.Set();
            Thread current = thread;
            if (current != null && current != Thread.CurrentThread)
                current.Join(2000);
            thread = null;

            lock (sync)
            {
                voices.Clear();
                if (waveOut != IntPtr.Zero) NativeMethods.waveOutReset(waveOut);
                int headerSize = Marshal.SizeOf(typeof(NativeMethods.WAVEHDR));
                for (int i = 0; i < buffers.Length; i++)
                {
                    OutputBuffer buffer = buffers[i];
                    if (buffer == null) continue;
                    if (buffer.Prepared && waveOut != IntPtr.Zero &&
                        buffer.HeaderPointer != IntPtr.Zero)
                    {
                        NativeMethods.waveOutUnprepareHeader(waveOut,
                            buffer.HeaderPointer, headerSize);
                        buffer.Prepared = false;
                    }
                    if (buffer.HeaderPointer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(buffer.HeaderPointer);
                        buffer.HeaderPointer = IntPtr.Zero;
                    }
                    if (buffer.DataHandle.IsAllocated)
                        buffer.DataHandle.Free();
                    buffers[i] = null;
                }
                if (waveOut != IntPtr.Zero)
                {
                    NativeMethods.waveOutClose(waveOut);
                    waveOut = IntPtr.Zero;
                }
            }
            wake.Dispose();
        }
    }
}
