using System.Drawing;

namespace RainWorldDesktopPet.RainWorld
{
    public sealed class AtlasElement
    {
        public string Name;
        public Rectangle Frame;
        public Rectangle SpriteSource;
        public Size SourceSize;
        public bool Rotated;

        // Futile FSprite local rectangle after trim metadata is restored.
        // Returned coordinates use the desktop renderer's Y-down convention.
        public RectangleF GetLocalRectangle(double anchorX, double anchorY)
        {
            return new RectangleF(
                (float)(SpriteSource.X - anchorX * SourceSize.Width),
                (float)(SpriteSource.Y - (1.0 - anchorY) * SourceSize.Height),
                Frame.Width,
                Frame.Height);
        }
    }
}
