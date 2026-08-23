using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Desktop
{
    public sealed class MouseTracker
    {
        private Vec2 lastPosition;
        private Vec2 velocity;
        private bool initialized;

        public Vec2 Position { get; private set; }
        public Vec2 Velocity { get { return velocity; } }

        public void Sample(double elapsedSeconds)
        {
            NativeMethods.Point point;
            if (!NativeMethods.GetCursorPos(out point))
            {
                return;
            }

            Position = new Vec2(point.X, point.Y);
            if (!initialized)
            {
                initialized = true;
                lastPosition = Position;
                return;
            }

            if (elapsedSeconds > 0.0001)
            {
                Vec2 instantaneous = (Position - lastPosition) / elapsedSeconds;
                velocity = Vec2.Lerp(velocity, instantaneous, 0.35);
            }

            lastPosition = Position;
        }
    }
}
