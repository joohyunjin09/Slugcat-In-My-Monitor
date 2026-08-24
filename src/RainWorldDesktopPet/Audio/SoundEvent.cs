using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Audio
{
    public sealed class SoundEvent
    {
        public SoundEvent(string id, Vec2 position, double volume, double pitch, int cooldownTicks)
        {
            Id = id;
            Position = position;
            Volume = volume;
            Pitch = pitch;
            CooldownTicks = cooldownTicks;
        }

        public readonly string Id;
        public readonly Vec2 Position;
        public readonly double Volume;
        public readonly double Pitch;
        public readonly int CooldownTicks;
    }
}
