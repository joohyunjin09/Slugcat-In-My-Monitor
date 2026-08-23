using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Desktop
{
    public sealed class MouseTracker
    {
        private Vec2 lastPosition;
        private Vec2 velocity;
        private bool initialized;
        private bool leftDown;
        private bool rightDown;
        private bool middleDown;
        private bool clickPending;

        public Vec2 Position { get; private set; }
        public Vec2 Velocity { get { return velocity; } }

        public bool ConsumeClick()
        {
            bool result = clickPending;
            clickPending = false;
            return result;
        }

        public void Sample(double elapsedSeconds)
        {
            NativeMethods.Point point;
            if (!NativeMethods.GetCursorPos(out point))
            {
                return;
            }

            Position = DesktopWorldTransform.ToSimulation(new Vec2(point.X, point.Y));
            short leftState = NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON);
            short rightState = NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON);
            short middleState = NativeMethods.GetAsyncKeyState(NativeMethods.VK_MBUTTON);
            bool currentLeft = (leftState & 0x8000) != 0;
            bool currentRight = (rightState & 0x8000) != 0;
            bool currentMiddle = (middleState & 0x8000) != 0;
            if ((leftState & 1) != 0 || (rightState & 1) != 0 ||
                (middleState & 1) != 0 || (currentLeft && !leftDown) ||
                (currentRight && !rightDown) || (currentMiddle && !middleDown))
                clickPending = true;
            leftDown = currentLeft;
            rightDown = currentRight;
            middleDown = currentMiddle;
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
