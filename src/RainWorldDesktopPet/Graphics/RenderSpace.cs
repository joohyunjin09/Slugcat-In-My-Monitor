using System.Drawing;
using RainWorldDesktopPet.Core;

namespace RainWorldDesktopPet.Graphics
{
    // Simulation/Desktop World use virtual-screen coordinates. Overlay/Render
    // space is local to the one virtual-desktop layered window. Sprite local
    // coordinates are handled only by AtlasElement.GetLocalRectangle.
    public sealed class RenderSpace
    {
        public RenderSpace(Rectangle virtualDesktopBounds)
        {
            VirtualDesktopBounds = virtualDesktopBounds;
            WorldOrigin = new Vec2(virtualDesktopBounds.Left, virtualDesktopBounds.Top);
        }

        public readonly Rectangle VirtualDesktopBounds;
        public readonly Vec2 WorldOrigin;

        public Vec2 WorldToOverlay(Vec2 worldPosition)
        {
            return worldPosition - WorldOrigin;
        }

        public Vec2 OverlayToWorld(Vec2 overlayPosition)
        {
            return overlayPosition + WorldOrigin;
        }

        public RectangleF WorldToOverlay(RectangleF worldBounds)
        {
            return new RectangleF(worldBounds.X - (float)WorldOrigin.X,
                worldBounds.Y - (float)WorldOrigin.Y, worldBounds.Width, worldBounds.Height);
        }
    }
}
