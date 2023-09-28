using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop.InfoHUD;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

[Tracked]
internal class CameraHitboxEntity : Entity {
    private static readonly Color color = Color.LightBlue * 0.75f;
    private Vector2 cameraTopLeft;
    private Vector2 cameraBottomRight;
    private Level level;

    private bool DrawCamera => TasSettings.CenterCamera && TasSettings.ShowCameraHitboxes;

    public override void Added(Scene scene) {
        base.Added(scene);
        Tag = Tags.Global | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.TransitionUpdate;
        level = scene as Level;
        Add(new PostUpdateHook(UpdateCameraHitbox));
    }

    private void UpdateCameraHitbox() {
        cameraTopLeft = level.MouseToWorld(Vector2.Zero);
        cameraBottomRight = level.MouseToWorld(new Vector2(Engine.ViewWidth, Engine.ViewHeight));
    }

    public override void DebugRender(Camera camera) {
        if (!DrawCamera) {
            return;
        }

        Draw.HollowRect(cameraTopLeft, cameraBottomRight.X - cameraTopLeft.X, cameraBottomRight.Y - cameraTopLeft.Y, color);
    }

    [Load]
    public static void Load() {
        On.Celeste.Level.LoadLevel += LevelOnLoadLevel;
    }

    [Unload]
    public static void Unload() {
        On.Celeste.Level.LoadLevel -= LevelOnLoadLevel;
    }

    private static void LevelOnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level self, Player.IntroTypes playerIntro, bool isFromLoader) {
        orig(self, playerIntro, isFromLoader);
        if (!self.Tracker.Entities.TryGetValue(typeof(CameraHitboxEntity), out var entities) || entities.IsEmpty()) {
            self.Add(new CameraHitboxEntity());
        }
    }
}

public static class CenterCamera {
    private static Vector2? savedCameraPosition;
    private static float? savedLevelZoom;
    private static float? savedLevelZoomTarget;
    private static Vector2? savedLevelZoomFocusPoint;
    private static float? savedLevelScreenPadding;
    private static Vector2? lastPlayerPosition;
    private static Vector2 moveOffset;
    private static Vector2 screenOffset;
    private static DateTime? arrowKeyPressTime;
    private static float viewportScale = 1f;
    private static int zoomInterval;
    private static Vector2? lockPosition;

    // this must be <= 4096 / 320 = 12.8, it's used in FreeCameraHitbox and 4096 is the maximum texture size
    public const float MaximumViewportScale = 12f;

    public static float LevelZoom {
        get => 1 / viewportScale;
        private set => viewportScale = 1 / value;
    }

    public static bool LevelZoomOut => LevelZoom < 0.999f;

    public static Camera ScreenCamera { get; private set; } = new();

    public static void Load() {
        On.Monocle.Engine.RenderCore += EngineOnRenderCore;
        On.Monocle.Commands.Render += CommandsOnRender;
        On.Celeste.Level.Render += LevelOnRender;
#if DEBUG
        moveOffset = Engine.Instance.GetDynamicDataInstance().Get<Vector2?>("CelesteTAS_MoveOffset") ?? Vector2.Zero;
        screenOffset = Engine.Instance.GetDynamicDataInstance().Get<Vector2?>("CelesteTAS_Screen_Offset") ?? Vector2.Zero;
        LevelZoom = Engine.Instance.GetDynamicDataInstance().Get<float?>("CelesteTAS_LevelZoom") ?? 1f;
#endif
    }

    public static void Unload() {
        On.Monocle.Engine.RenderCore -= EngineOnRenderCore;
        On.Monocle.Commands.Render -= CommandsOnRender;
        On.Celeste.Level.Render -= LevelOnRender;
#if DEBUG
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_MoveOffset", moveOffset);
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_Screen_Offset", screenOffset);
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_LevelZoom", LevelZoom);
#endif
    }

    private static void EngineOnRenderCore(On.Monocle.Engine.orig_RenderCore orig, Engine self) {
        CenterTheCamera();
        orig(self);
        RestoreTheCamera();
    }

    // fix: clicked entity error when console and center camera are enabled
    private static void CommandsOnRender(On.Monocle.Commands.orig_Render orig, Monocle.Commands self) {
        CenterTheCamera();
        orig(self);
        RestoreTheCamera();
    }

    private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);
        MoveCamera(self);
        ZoomCamera();
        LockCamera(self);
    }

    private static void LockCamera(Level level) {
        if (!TasSettings.CenterCamera) {
            return;
        }

        if (Hotkeys.LockCamera.Pressed) {
            if (lockPosition.HasValue) {
                lockPosition = null;
            } else {
                Camera camera = level.Camera;
                lockPosition = camera.Position - moveOffset + new Vector2(camera.Viewport.Width / 2f, camera.Viewport.Height / 2f);
            }
        }
    }

    private static void CenterTheCamera() {
        if (Engine.Scene is not Level level || !TasSettings.CenterCamera) {
            return;
        }

        Camera camera = level.Camera;
        if (Engine.Scene.GetPlayer() is { } player) {
            lastPlayerPosition = lockPosition ?? player.Position;
        } else if (Engine.Scene.Tracker.GetEntity<PlayerDeadBody>() is { } deadBody) {
            lastPlayerPosition = lockPosition ?? deadBody.Position;
        }

        if (lastPlayerPosition != null) {
            savedCameraPosition = camera.Position;
            savedLevelZoom = level.Zoom;
            savedLevelZoomTarget = level.ZoomTarget;
            savedLevelZoomFocusPoint = level.ZoomFocusPoint;
            savedLevelScreenPadding = level.ScreenPadding;

            camera.Position = lastPlayerPosition.Value + moveOffset - new Vector2(camera.Viewport.Width / 2f, camera.Viewport.Height / 2f);
            Vector2 offset = Vector2.Zero;
            if (TasSettings.CenterCameraHorizontallyOnly) {
                offset = camera.position - savedCameraPosition.Value - moveOffset;
                camera.position.Y -= offset.Y;
            }

            level.Zoom = LevelZoom;
            level.ZoomTarget = LevelZoom;
            level.ZoomFocusPoint = new Vector2(320f, 180f) / 2f;
            if (LevelZoomOut) {
                level.ZoomFocusPoint += screenOffset;
            }

            level.ScreenPadding = 0;

            ScreenCamera = new((int) Math.Round(320 * viewportScale), (int) Math.Round(180 * viewportScale));
            ScreenCamera.Position = lastPlayerPosition.Value + moveOffset -
                                    new Vector2(ScreenCamera.Viewport.Width / 2f, ScreenCamera.Viewport.Height / 2f);
            if (TasSettings.CenterCameraHorizontallyOnly) {
                ScreenCamera.position.Y -= offset.Y;
            }

            if (LevelZoomOut) {
                ScreenCamera.Position += screenOffset;
            }
        }
    }

    private static void RestoreTheCamera() {
        if (Engine.Scene is not Level level) {
            return;
        }

        if (savedCameraPosition != null) {
            level.Camera.Position = savedCameraPosition.Value;
            savedCameraPosition = null;
        }

        if (savedLevelZoom != null) {
            level.Zoom = savedLevelZoom.Value;
            savedLevelZoom = null;
        }

        if (savedLevelZoomTarget != null) {
            level.ZoomTarget = savedLevelZoomTarget.Value;
            savedLevelZoomTarget = null;
        }

        if (savedLevelZoomFocusPoint != null) {
            level.ZoomFocusPoint = savedLevelZoomFocusPoint.Value;
            savedLevelZoomFocusPoint = null;
        }

        if (savedLevelScreenPadding != null) {
            level.ScreenPadding = savedLevelScreenPadding.Value;
            savedLevelScreenPadding = null;
        }
    }

    private static float ArrowKeySensitivity {
        get {
            if (arrowKeyPressTime == null) {
                return 1;
            }

            float sensitivity = (float) ((DateTime.Now - arrowKeyPressTime.Value).TotalMilliseconds / 200f);
            return Calc.Clamp(sensitivity, 1, 6);
        }
    }

    public static void ResetCamera() {
        if (Hotkeys.FreeCamera.DoublePressed || MouseButtons.Right.DoublePressed) {
            moveOffset = Vector2.Zero;
            screenOffset = Vector2.Zero;
            LevelZoom = 1;
            lockPosition = null;
        }
    }

    private static void MoveCamera(Level level) {
        if (!TasSettings.CenterCamera) {
            return;
        }

        bool moveCamera = false;
        bool moveScreenCamera = false;

        // info hud hotkey + arrow key
        if (!Hotkeys.CameraUp.Check && !Hotkeys.CameraDown.Check && !Hotkeys.CameraLeft.Check && !Hotkeys.CameraRight.Check) {
            arrowKeyPressTime = null;
        } else if (arrowKeyPressTime == null) {
            arrowKeyPressTime = DateTime.Now;
        }

        int moveY = 0;
        int moveX = 0;

        if (Hotkeys.CameraUp.Check) {
            moveY -= 1;
        }

        if (Hotkeys.CameraDown.Check) {
            moveY += 1;
        }

        if (Hotkeys.CameraLeft.Check) {
            moveX -= 1;
        }

        if (Hotkeys.CameraRight.Check) {
            moveX += 1;
        }

        if (Hotkeys.InfoHud.Check) {
            moveOffset += new Vector2(ArrowKeySensitivity * moveX, ArrowKeySensitivity * moveY);
            moveCamera = moveX != 0 || moveY != 0;
        }

        if (Hotkeys.FreeCamera.Check) {
            if (LevelZoomOut) {
                screenOffset += new Vector2(ArrowKeySensitivity * moveX, ArrowKeySensitivity * moveY);
                moveScreenCamera = moveX != 0 || moveY != 0;
            } else {
                moveOffset += new Vector2(ArrowKeySensitivity * moveX, ArrowKeySensitivity * moveY);
                moveCamera = moveX != 0 || moveY != 0;
            }
        }

        // mouse right button
        if (!Hotkeys.InfoHud.Check && MouseButtons.Right.LastCheck && MouseButtons.Right.Check) {
            InfoMouse.DrawCursor(MouseButtons.Position);

            float scale = LevelZoom * level.Camera.Zoom * 6f * Engine.ViewWidth / Engine.Width;
            if (Hotkeys.FreeCamera.Check && LevelZoomOut) {
                screenOffset -= (MouseButtons.Position - MouseButtons.LastPosition) / scale;
                moveScreenCamera = true;
            } else {
                moveOffset -= (MouseButtons.Position - MouseButtons.LastPosition) / scale;
                moveCamera = true;
            }
        }

        if (lastPlayerPosition is { } playerPosition && level.Session.MapData.Bounds is var bounds) {
            if (moveCamera) {
                Vector2 result = (playerPosition + moveOffset).Clamp(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
                moveOffset = result - playerPosition;
            }

            if (moveScreenCamera) {
                Vector2 result = (playerPosition + moveOffset + screenOffset).Clamp(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
                screenOffset = result - playerPosition - moveOffset;
            }
        }
    }

    private static void ZoomCamera() {
        if (!TasSettings.CenterCamera) {
            return;
        }

        if (zoomInterval > 0) {
            zoomInterval--;
        }

        int direction = 0;
        if (Hotkeys.FreeCamera.Check) {
            if (Hotkeys.CameraZoomIn.Check && zoomInterval <= 0) {
                direction = -1;
            }

            if (Hotkeys.CameraZoomOut.Check && zoomInterval <= 0) {
                direction = 1;
            }

            if (direction != 0) {
                zoomInterval = 10;
            }
        }

        if (direction == 0) {
            direction = -Math.Sign(MouseButtons.Wheel);
        }

        // delta must be a multiple of 0.1 to let free camera hitboxes align properly
        float delta = (viewportScale + direction * 0.01f) switch {
            > 1 => direction * 0.2f,
            _ => direction * 0.1f
        };

        viewportScale = Calc.Clamp(viewportScale + delta, 0.2f, MaximumViewportScale);
    }
}