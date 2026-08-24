using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Audio
{
    public sealed class SoundEvent
    {
        public SoundEvent(string id, Vec2 position, double volume, double pitch, int cooldownTicks)
            : this(id, position, volume, pitch, cooldownTicks, false, false, null)
        {
        }

        private SoundEvent(string id, Vec2 position, double volume, double pitch,
            int cooldownTicks, bool loop, bool stopLoop, string loopKey)
        {
            Id = id;
            Position = position;
            Volume = volume;
            Pitch = pitch;
            CooldownTicks = cooldownTicks;
            Loop = loop;
            StopLoop = stopLoop;
            LoopKey = loopKey;
        }

        public readonly string Id;
        public readonly Vec2 Position;
        public readonly double Volume;
        public readonly double Pitch;
        public readonly int CooldownTicks;
        public readonly bool Loop;
        public readonly bool StopLoop;
        public readonly string LoopKey;

        public static SoundEvent StartLoop(string id, string loopKey, Vec2 position,
            double volume, double pitch)
        {
            return new SoundEvent(id, position, volume, pitch, 0,
                true, false, loopKey);
        }

        public static SoundEvent EndLoop(string id, string loopKey, Vec2 position)
        {
            return new SoundEvent(id, position, 0.0, 1.0, 0,
                false, true, loopKey);
        }
    }
}
