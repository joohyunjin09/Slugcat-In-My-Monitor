using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Audio
{
    public sealed class SoundEvent
    {
        public SoundEvent(string sourceId, string id, Vec2 position,
            double volume, double pitch, int cooldownTicks, long simulationTick)
            : this(sourceId, id, position, Vec2.Zero, volume, pitch,
                cooldownTicks, simulationTick)
        {
        }

        public SoundEvent(string sourceId, string id, Vec2 position, Vec2 velocity,
            double volume, double pitch, int cooldownTicks, long simulationTick)
        {
            SourceId = sourceId ?? string.Empty;
            Id = id ?? string.Empty;
            Position = position;
            Velocity = velocity;
            Volume = volume;
            Pitch = pitch;
            CooldownTicks = cooldownTicks;
            SimulationTick = simulationTick;
        }

        public readonly string SourceId;
        public readonly string Id;
        public readonly Vec2 Position;
        public readonly Vec2 Velocity;
        public readonly double Volume;
        public readonly double Pitch;
        public readonly int CooldownTicks;
        public readonly long SimulationTick;
    }

    public interface ISoundEventSink
    {
        string Status { get; }
        void Play(SoundEvent sound);
        void StartLoop(SoundEvent sound, string loopKey);
        void StopLoop(string sourceId, string loopKey);
        void StopSource(string sourceId);
    }
}
