using System;
using System.Threading;
using RainWorldDesktopPet.Core;
using RainWorldDesktopPet.Desktop;

namespace RainWorldDesktopPet.Physics
{
    // Weapon.Mode plus Spear's two terrain-resting outcomes. A mode transition,
    // rather than a sound timer, owns every one-shot collision sound.
    public enum DesktopSpearMode
    {
        Held,
        Thrown,
        Free,
        StuckInWall,
        StuckInGround,
        StuckInCreature
    }

    public sealed class DesktopSpear
    {
        private const double Gravity = 0.9;
        private const double ThrownGravity = 0.45;
        private const double AirFriction = 0.999;
        private const double Bounce = 0.4;
        private const double SurfaceFriction = 0.4;
        private const int NeedleFadeMaximum = 400;
        private readonly Random random;
        private static int nextAudioLoopId;
        private Vec2 thrownPosition;
        private int stillTicks;
        private Vec2[] umbilical;
        private Vec2[] lastUmbilical;
        private Vec2[] umbilicalVelocity;
        private double[] umbilicalLife;
        private double[] umbilicalLifeDecay;

        public DesktopSpear(Vec2 position)
            : this(position, 0)
        {
        }

        public DesktopSpear(Vec2 position, int needleType)
        {
            Chunk = new BodyChunk(0, position, 5.0, 0.07);
            Mode = DesktopSpearMode.Held;
            Rotation = Vec2.Right;
            LastRotation = Rotation;
            InFrontOfPlayer = true;
            NeedleType = MathUtil.Clamp(needleType, 0, 2);
            IsSpearmasterNeedle = true;
            NeedleHasConnection = true;
            NeedleFade = NeedleFadeMaximum;
            DamageBonus = 1.25;
            random = new Random(unchecked(0x51EA2 + needleType * 7919));
            AudioLoopKey = "spear:" + Interlocked.Increment(ref nextAudioLoopId);
        }

        public readonly BodyChunk Chunk;
        public DesktopSpearMode Mode { get; private set; }
        public Vec2 Rotation { get; private set; }
        public Vec2 LastRotation { get; private set; }
        public bool InFrontOfPlayer { get; private set; }
        public double RotationSpeed { get; private set; }
        public int NeedleType { get; private set; }
        public bool IsSpearmasterNeedle { get; private set; }
        public bool NeedleHasConnection { get; private set; }
        public int NeedleFade { get; private set; }
        public double DamageBonus { get; private set; }
        public Vec2 ConnectionAnchor { get; private set; }
        public Vec2 ThrowDirection { get; private set; }
        public long StuckSurfaceId { get; private set; }
        public DesktopSurfaceKind StuckSurfaceKind { get; private set; }
        public int Age { get; private set; }
        public string LastImpactSound { get; private set; }
        public string AudioLoopKey { get; private set; }
        public int ImpactSparkCount { get; private set; }
        public string AirLoopSound
        {
            get
            {
                if (Chunk.Velocity.Length <= 5.0) return null;
                if (Mode == DesktopSpearMode.Thrown)
                    return "Spear_Thrown_Through_Air_LOOP";
                if (Mode == DesktopSpearMode.Free)
                    return "Spear_Spinning_Through_Air_LOOP";
                return null;
            }
        }
        public double NeedleFadeFraction
        {
            get { return NeedleFade / (double)NeedleFadeMaximum; }
        }
        public bool HasUmbilical
        {
            get { return umbilical != null; }
        }
        public Vec2[] Umbilical { get { return umbilical; } }
        public Vec2[] LastUmbilical { get { return lastUmbilical; } }
        public double[] UmbilicalLife { get { return umbilicalLife; } }

        public void SetCreationVelocity(Vec2 velocity)
        {
            Chunk.Velocity = velocity;
        }

        public void SetConnectionAnchor(Vec2 position)
        {
            ConnectionAnchor = position;
        }

        public void HoldAt(Vec2 position, Vec2 direction)
        {
            HoldAt(position, direction, Vec2.Zero);
        }

        public void HoldAt(Vec2 position, Vec2 direction, Vec2 handVelocity)
        {
            Mode = DesktopSpearMode.Held;
            Chunk.LastPosition = Chunk.Position;
            Chunk.Position = position;
            Chunk.Velocity = handVelocity;
            LastRotation = Rotation;
            if (direction.LengthSquared > 0.001) Rotation = direction.Normalized;
            RotationSpeed = 0.0;
            stillTicks = 0;
        }

        public void Throw(Vec2 velocity)
        {
            Throw(velocity, velocity.Normalized);
        }

        public void Throw(Vec2 velocity, Vec2 direction)
        {
            Mode = DesktopSpearMode.Thrown;
            Chunk.Velocity = velocity;
            LastRotation = Rotation;
            ThrowDirection = direction.LengthSquared > 0.001
                ? direction.Normalized : velocity.Normalized;
            if (ThrowDirection.LengthSquared > 0.001) Rotation = ThrowDirection;
            RotationSpeed = 0.0;
            thrownPosition = Chunk.Position;
            Age = 0;
            stillTicks = 0;
            LastImpactSound = null;
            ImpactSparkCount = 0;
            if (NeedleHasConnection) CreateUmbilical();
            InFrontOfPlayer = true;
        }

        public void SetOverlap(bool inFront)
        {
            InFrontOfPlayer = inFront;
        }

        public void DisconnectNeedle()
        {
            if (!NeedleHasConnection) return;
            NeedleHasConnection = false;
            NeedleFade = NeedleFadeMaximum;
        }

        public bool HitCreature(bool sticks, bool shell)
        {
            if (Mode != DesktopSpearMode.Thrown) return false;
            if (shell)
            {
                ChangeMode(DesktopSpearMode.Free);
                Chunk.Velocity *= -0.5;
                RotationSpeed = RandomRotationSpeed();
                LastImpactSound = "Spear_Bounce_Off_Creauture_Shell";
                return true;
            }
            if (!sticks)
            {
                ChangeMode(DesktopSpearMode.Free);
                LastImpactSound = "Spear_Damage_Creature_But_Fall_Out";
                return true;
            }
            ChangeMode(DesktopSpearMode.StuckInCreature);
            Chunk.Velocity = Vec2.Zero;
            DisconnectNeedle();
            LastImpactSound = "Spear_Stick_In_Creature";
            return true;
        }

        public bool Step(DesktopCollisionWorld world)
        {
            LastImpactSound = null;
            ImpactSparkCount = 0;
            if (!NeedleHasConnection && NeedleFade > 0) NeedleFade--;
            if (Mode != DesktopSpearMode.Thrown && Mode != DesktopSpearMode.Free)
            {
                FollowStuckSurface(world);
                StepUmbilical();
                return false;
            }

            Age++;
            Chunk.BeginTick();
            LastRotation = Rotation;
            Chunk.Integrate(Mode == DesktopSpearMode.Thrown
                ? ThrownGravity : Gravity, AirFriction);
            world.Resolve(Chunk, world.CurrentSnapshot, 0, SurfaceFriction, Bounce);
            StepUmbilical();

            if (Mode == DesktopSpearMode.Thrown)
            {
                TerrainImpactData forwardImpact = FindForwardImpact();
                if (forwardImpact != null && forwardImpact.FirstContact)
                {
                    bool enoughSpeed = forwardImpact.ImpactSpeed >= 10.0;
                    bool closeToThrow = Vec2.Distance(thrownPosition, Chunk.Position) < 140.0;
                    bool sticks = enoughSpeed &&
                        (closeToThrow || random.NextDouble() < 0.33);
                    if (sticks)
                    {
                        ChangeMode(DesktopSpearMode.StuckInWall);
                        Chunk.Velocity = Vec2.Zero;
                        StuckSurfaceId = forwardImpact.SurfaceId;
                        StuckSurfaceKind = forwardImpact.SurfaceKind;
                        LastImpactSound = "Spear_Stick_In_Wall";
                    }
                    else
                    {
                        // Weapon.HitWall: the collision resolver already applied
                        // Spear.bounce=.4 and surfaceFriction=.4 exactly once.
                        ChangeMode(DesktopSpearMode.Free);
                        RotationSpeed = RandomRotationSpeed();
                        LastImpactSound = "Spear_Bounce_Off_Wall";
                        ImpactSparkCount = 7;
                    }
                    return true;
                }

                // Spear.Update only remains thrown when the terrain contact
                // is aligned with throwDir and becomes a wall stick.  A floor
                // contact from a horizontal throw is not aligned, so Weapon
                // physics drops it into Free; Spear's spinning branch then
                // chooses its diagonal ground-rest rotation.  Keeping it
                // thrown skipped that branch and left the sprite horizontal.
                if (Chunk.ContactFloor)
                {
                    ChangeMode(DesktopSpearMode.Free);
                    RotationSpeed = RandomRotationSpeed();
                }
            }

            if (Mode == DesktopSpearMode.Free)
            {
                RotateFreeSpear();
                bool restingOnGround = Chunk.ContactFloor &&
                    Chunk.Velocity.LengthSquared < 0.01;
                stillTicks = restingOnGround ? stillTicks + 1 : 0;
                // Spear.Update settles a spinning spear immediately on a
                // floor contact, or after twenty near-still ticks. Preserve
                // its original -50..50 degree resting spread instead of
                // retaining the last airborne rotation.
                if (Chunk.ContactFloor || stillTicks > 20)
                {
                    ChangeMode(DesktopSpearMode.StuckInGround);
                    RotationSpeed = 0.0;
                    Rotation = CalculateOriginalGroundRestDirection(random.NextDouble());
                    Chunk.Velocity = Vec2.Zero;
                    StuckSurfaceId = Chunk.SupportingSurfaceId;
                    StuckSurfaceKind = Chunk.SupportingSurfaceKind;
                    LastImpactSound = "Spear_Stick_In_Ground";
                    return true;
                }
            }
            return false;
        }

        private TerrainImpactData FindForwardImpact()
        {
            for (int i = 0; i < Chunk.TerrainImpactCount; i++)
            {
                TerrainImpactData impact = Chunk.TerrainImpacts[i];
                Vec2 directionIntoTerrain = -impact.CollisionNormal;
                if (Vec2.Dot(Rotation, directionIntoTerrain) > 0.5) return impact;
            }
            return null;
        }

        private void FollowStuckSurface(DesktopCollisionWorld world)
        {
            if ((Mode != DesktopSpearMode.StuckInWall &&
                Mode != DesktopSpearMode.StuckInGround) || StuckSurfaceId == 0)
                return;
            DesktopSurface surface;
            if (!world.TryGetSurface(StuckSurfaceId, StuckSurfaceKind, out surface))
            {
                ChangeMode(DesktopSpearMode.Free);
                Chunk.Velocity = Vec2.Zero;
                return;
            }
            Vec2 movement = world.GetSurfaceMovement(StuckSurfaceId,
                StuckSurfaceKind);
            Chunk.LastPosition = Chunk.Position;
            Chunk.Position += movement;
        }

        private void CreateUmbilical()
        {
            // Spear.Umbilical is a one-shot cosmetic object in the original.
            // The connection is not a taut rope: every segment starts with
            // life 2 and separately decays over 150..200 ticks, leaving a
            // visibly breaking trail after the needle detaches.
            int count = random.Next(10, 20);
            umbilical = new Vec2[count];
            lastUmbilical = new Vec2[count];
            umbilicalVelocity = new Vec2[count];
            umbilicalLife = new double[count];
            umbilicalLifeDecay = new double[count];
            for (int i = 0; i < count; i++)
            {
                Vec2 randomOffset = RandomUnit() * random.NextDouble();
                umbilical[i] = ConnectionAnchor + randomOffset;
                lastUmbilical[i] = umbilical[i];
                umbilicalVelocity[i] = Chunk.Velocity *
                    (0.3 * random.NextDouble()) + RandomUnit() *
                    (1.5 * random.NextDouble());
                umbilicalLife[i] = 2.0;
                umbilicalLifeDecay[i] = MathUtil.Lerp(150.0, 200.0,
                    Math.Pow(random.NextDouble(), 0.3));
            }
        }

        private void StepUmbilical()
        {
            if (umbilical == null) return;
            int last = umbilical.Length - 1;
            bool anyAlive = false;
            for (int i = 0; i <= last; i++)
            {
                lastUmbilical[i] = umbilical[i];
                double life = LifeOfUmbilicalSegment(i);
                umbilicalVelocity[i] *= MathUtil.Lerp(0.99, 0.8,
                    MathUtil.Clamp01((umbilicalVelocity[i].Length - 1.0) / 29.0));
                // Rain World's world Y axis points up; desktop Y points down.
                umbilicalVelocity[i].Y += MathUtil.Lerp(0.1, 0.6, life);
                umbilical[i] += umbilicalVelocity[i];
                if (i > 0 && Vec2.Distance(umbilical[i], umbilical[i - 1]) > 6.0)
                {
                    Vec2 correction = MathUtil.Direction(umbilical[i],
                        umbilical[i - 1]) * (Vec2.Distance(umbilical[i],
                        umbilical[i - 1]) - 6.0);
                    umbilical[i] += correction * 0.15;
                    umbilicalVelocity[i] += correction * 0.25;
                    umbilical[i - 1] -= correction * 0.15;
                    umbilicalVelocity[i - 1] -= correction * 0.25;
                }
                if (i > 1 && LifeOfUmbilicalSegment(i - 1) > 0.0)
                {
                    Vec2 pull = MathUtil.Direction(umbilical[i], umbilical[i - 2]);
                    umbilicalVelocity[i] += pull * 0.6;
                    umbilicalVelocity[i - 2] -= pull * 0.6;
                }
                umbilicalLife[i] -= 1.0 / umbilicalLifeDecay[i];
                if (umbilicalLife[i] > 0.0) anyAlive = true;
            }
            if (LifeOfUmbilicalSegment(0) > 0.0)
            {
                umbilical[0] = ConnectionAnchor;
                umbilicalVelocity[0] = Vec2.Zero;
            }
            if (LifeOfUmbilicalSegment(last) > 0.0)
            {
                umbilical[last] = Chunk.Position - Rotation * 25.0;
                umbilicalVelocity[last] = Vec2.Zero;
            }
            if (anyAlive) return;
            umbilical = null;
            lastUmbilical = null;
            umbilicalVelocity = null;
            umbilicalLife = null;
            umbilicalLifeDecay = null;
        }

        private double LifeOfUmbilicalSegment(int index)
        {
            if (index <= 0) return umbilicalLife[0];
            return Math.Min(umbilicalLife[index], umbilicalLife[index - 1]);
        }

        private Vec2 RandomUnit()
        {
            double angle = random.NextDouble() * Math.PI * 2.0;
            return new Vec2(Math.Cos(angle), Math.Sin(angle));
        }

        private void RotateFreeSpear()
        {
            if (Math.Abs(RotationSpeed) < 0.000001)
                RotationSpeed = RandomRotationSpeed();
            double radians = RotationSpeed * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            Rotation = new Vec2(Rotation.X * cosine - Rotation.Y * sine,
                Rotation.X * sine + Rotation.Y * cosine).Normalized;
        }

        private double RandomRotationSpeed()
        {
            double speed = MathUtil.Lerp(-100.0, 100.0, random.NextDouble());
            if (Math.Abs(speed) < 10.0) speed = speed < 0.0 ? -10.0 : 10.0;
            return speed;
        }

        public static Vec2 CalculateOriginalGroundRestDirection(double randomValue)
        {
            double angle = (MathUtil.Lerp(-50.0, 50.0, randomValue) + 180.0) *
                Math.PI / 180.0;
            // Custom.DegToVec is evaluated in Rain World's y-up world. The
            // desktop renderer uses y-down screen coordinates.
            return new Vec2(Math.Cos(angle), -Math.Sin(angle));
        }

        private void ChangeMode(DesktopSpearMode mode)
        {
            Mode = mode;
            stillTicks = 0;
        }
    }
}
