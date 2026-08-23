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
        public VirtualPosture Posture;
        public bool DropThrough;

        public VirtualInput(int x, int y, bool jump, bool pickup)
            : this(x, y, jump, pickup, VirtualPosture.None)
        {
        }

        public VirtualInput(int x, int y, bool jump, bool pickup, VirtualPosture posture)
            : this(x, y, jump, pickup, posture, false)
        {
        }

        public VirtualInput(int x, int y, bool jump, bool pickup, VirtualPosture posture, bool dropThrough)
        {
            X = x < -1 ? -1 : (x > 1 ? 1 : x);
            Y = y < -1 ? -1 : (y > 1 ? 1 : y);
            Jump = jump;
            Pickup = pickup;
            Posture = posture;
            DropThrough = dropThrough;
        }

        public static readonly VirtualInput Neutral = new VirtualInput(0, 0, false, false);

        public override string ToString()
        {
            return string.Format("x:{0} y:{1} jump:{2} pickup:{3} posture:{4} drop:{5}",
                X, Y, Jump, Pickup, Posture, DropThrough);
        }
    }
}
