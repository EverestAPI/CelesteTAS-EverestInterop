using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay.Hitboxes;

/// Hitbox indicating the real camera bounds, while using center camera
internal static class CameraHitbox {

    private static readonly Color color = Color.LightBlue * 0.75f;
    private static Vector2 cameraTopLeft = Vector2.Zero;
    private static Vector2 cameraBottomRight = Vector2.Zero;

    [Load]
    private static void Load() {
        // Store values from update, since they are overwritten during Render
        Events.PostUpdate += scene => {
            if (scene is not Level level) {
                return;
            }

            cameraTopLeft = level.MouseToWorld(Vector2.Zero);
            cameraBottomRight = level.MouseToWorld(new Vector2(Engine.ViewWidth, Engine.ViewHeight));
        };
        Events.PostDebugRender += scene => {
            if (!TasSettings.CenterCamera || !TasSettings.ShowCameraHitboxes || scene is not Level) {
                return;
            }

            Draw.HollowRect(cameraTopLeft, cameraBottomRight.X - cameraTopLeft.X, cameraBottomRight.Y - cameraTopLeft.Y, color);
        };
    }
}
