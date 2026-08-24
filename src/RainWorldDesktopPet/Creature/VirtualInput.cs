using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Creature
{
    public enum VirtualPosture
    {
        None,
        Sit,
        Sleep
    }

    public struct VirtualInput
    {
        public int X;
        public int Y;
        public bool Jump;
        public bool Pickup;
        public bool Throw;
        public VirtualPosture Posture;
        public bool DropThrough;
        public bool JumpPressed;
        public bool PickupPressed;
        public bool ThrowPressed;

        public VirtualInput(int x, int y, bool jump, bool pickup)
            : this(x, y, jump, pickup, VirtualPosture.None)
        {
        }

        public VirtualInput(int x, int y, bool jump, bool pickup, VirtualPosture posture)
            : this(x, y, jump, pickup, posture, false)
        {
        }

        public VirtualInput(int x, int y, bool jump, bool pickup, VirtualPosture posture, bool dropThrough)
            : this(x, y, jump, pickup, false, posture, dropThrough)
        {
        }

        public VirtualInput(int x, int y, bool jump, bool pickup, bool throwObject,
            VirtualPosture posture, bool dropThrough)
        {
            X = x < -1 ? -1 : (x > 1 ? 1 : x);
            Y = y < -1 ? -1 : (y > 1 ? 1 : y);
            Jump = jump;
            Pickup = pickup;
            Throw = throwObject;
            Posture = posture;
            DropThrough = dropThrough;
            JumpPressed = false;
            PickupPressed = false;
            ThrowPressed = false;
        }

        public void ResolveEdges(VirtualInput previous)
        {
            JumpPressed = Jump && !previous.Jump;
            PickupPressed = Pickup && !previous.Pickup;
            ThrowPressed = Throw && !previous.Throw;
        }

        public static readonly VirtualInput Neutral = new VirtualInput(0, 0, false, false);

        public override string ToString()
        {
            return string.Format("x:{0} y:{1} jump:{2}/{3} pickup:{4}/{5} throw:{6}/{7} posture:{8} drop:{9}",
                X, Y, Jump, JumpPressed, Pickup, PickupPressed, Throw, ThrowPressed,
                Posture, DropThrough);
        }
    }
}
