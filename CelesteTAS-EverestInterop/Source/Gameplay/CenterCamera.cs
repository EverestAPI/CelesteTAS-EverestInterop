using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using System;
using TAS.EverestInterop;
using TAS.EverestInterop.InfoHUD;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay;

/// Centers the camera onto the player, to keep them in the same position while moving
internal static class CenterCamera {

    [Load]
    private static void Load() {
        On.Monocle.Engine.RenderCore += On_Engine_RenderCore;
        On.Monocle.Commands.Render += On_Commands_Render;
        On.Celeste.Level.Render += On_Level_Render;

#if DEBUG
        cameraOffset = Engine.Instance.GetDynamicDataInstance().Get<Vector2?>("CelesteTAS_CameraOffset") ?? Vector2.Zero;
        canvasOffset = Engine.Instance.GetDynamicDataInstance().Get<Vector2?>("CelesteTAS_CanvasOffset") ?? Vector2.Zero;
        viewportScale = Engine.Instance.GetDynamicDataInstance().Get<float?>("CelesteTAS_ViewportScale") ?? 1.0f;
#endif
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Engine.RenderCore -= On_Engine_RenderCore;
        On.Monocle.Commands.Render -= On_Commands_Render;
        On.Celeste.Level.Render -= On_Level_Render;

#if DEBUG
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_CameraOffset", cameraOffset);
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_CanvasOffset", canvasOffset);
        Engine.Instance.GetDynamicDataInstance().Set("CelesteTAS_ViewportScale", viewportScale);
#endif
    }

    private static void On_Engine_RenderCore(On.Monocle.Engine.orig_RenderCore orig, Engine self) {
        AdjustCamera();
        orig(self);
        RestoreCamera();
    }

    // Commands check which entity was clicked, which is dependant on the current camera
    private static void On_Commands_Render(On.Monocle.Commands.orig_Render orig, Monocle.Commands self) {
        AdjustCamera();
        orig(self);
        RestoreCamera();
    }

    private static void On_Level_Render(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);

        // Show cursor for dragging camera
        if (TasSettings.CenterCamera && !Hotkeys.InfoHud.Check && MouseInput.Right.Check) {
            InfoMouse.DrawCursor(MouseInput.Position);
        }
    }

    // Disable "Zoom Level" Extended Variant while using center camera, to avoid it causing issues with rendering
    private static bool DisableZoomLevelVariant() => TasSettings.CenterCamera;

    [ModILHook("ExtendedVariantMode", "ExtendedVariants.Variants.ZoomLevel", "modZoom")]
    private static void IL_ZoomLevel_modZoom(ILCursor cursor) {
        var start = cursor.MarkLabel();
        cursor.MoveBeforeLabels();

        cursor.EmitDelegate(DisableZoomLevelVariant);
        cursor.EmitBrfalse(start);

        cursor.EmitLdarg1();
        cursor.EmitRet();
    }
    [ModILHook("ExtendedVariantMode", "ExtendedVariants.Variants.ZoomLevel", "getScreenPosition")]
    private static void IL_ZoomLevel_getScreenPosition(ILCursor cursor) {
        var start = cursor.MarkLabel();
        cursor.MoveBeforeLabels();

        cursor.EmitDelegate(DisableZoomLevelVariant);
        cursor.EmitBrfalse(start);

        cursor.EmitLdarg1();
        cursor.EmitRet();
    }

    private static Vector2? cameraTargetPosition;
    private static Vector2? lockPosition;

    private static Vector2 cameraOffset;
    private static Vector2 canvasOffset;
    private static float viewportScale = 1.0f;

    public static float ZoomLevel => 1.0f / viewportScale;
    public static bool ZoomedOut => viewportScale > 1.0f;

    public static readonly Camera ScreenCamera = new();

    private static Vector2? savedCameraPosition;
    private static float? savedLevelZoom;
    private static float? savedLevelZoomTarget;
    private static Vector2? savedLevelZoomFocusPoint;
    private static float? savedLevelScreenPadding;

    // ExCameraDynamics
    private static bool tasEnabledExCameraDynamics;
    private static bool? savedAutomaticZooming;
    private static float? savedTriggerZoomOverride;

    public static void Toggled() {
        // Enable ExCameraDynamics if needed
        if (TasSettings.EnableExCameraDynamicsForCenterCamera && TasSettings.CenterCamera ) {
            if (!ExCameraDynamicsInterop.Enabled) {
                ExCameraDynamicsInterop.EnableHooks();
                tasEnabledExCameraDynamics = true;
            }
        } else if (tasEnabledExCameraDynamics) {
            if (ExCameraDynamicsInterop.Enabled) {
                ExCameraDynamicsInterop.DisableHooks();
            }
            tasEnabledExCameraDynamics = false;
        }
    }

    private delegate void orig_ExCameraDynamics_EnableHooks();

    // Avoid disabling ExCameraDynamics if a map enabled it
    [ModOnHook("ExtendedCameraDynamics", "Celeste.Mod.ExCameraDynamics.Code.Hooks.CameraZoomHooks", "Hook")]
    private static void On_ExCameraDynamics_EnableHooks(orig_ExCameraDynamics_EnableHooks orig) {
        tasEnabledExCameraDynamics = false;
        orig();
    }

    /// Adjust offset and zoom of the centered camera
    [UpdateMeta]
    private static void UpdateMeta() {
        if (!TasSettings.CenterCamera || Engine.Scene is not Level level) {
            return;
        }

        // Double press to reset camera
        if (Hotkeys.FreeCamera.DoublePressed || MouseInput.Right.DoublePressed) {
            cameraOffset = Vector2.Zero;
            canvasOffset = Vector2.Zero;
            viewportScale = 1.0f;
            lockPosition = null;
            return;
        }

        var moveOffset = Vector2.Zero;
        int zoomDirection = 0;

        const float ArrowKeySensitivity = 2.0f;
        if (Hotkeys.CameraLeft.Check) {
            moveOffset.X -= ArrowKeySensitivity;
        }
        if (Hotkeys.CameraRight.Check) {
            moveOffset.X += ArrowKeySensitivity;
        }
        if (Hotkeys.CameraUp.Check) {
            moveOffset.Y -= ArrowKeySensitivity;
        }
        if (Hotkeys.CameraDown.Check) {
            moveOffset.Y += ArrowKeySensitivity;
        }

        if (Hotkeys.CameraZoomIn.Check) {
            zoomDirection -= 1;
        }
        if (Hotkeys.CameraZoomOut.Check) {
            zoomDirection += 1;
        }
        if (zoomDirection == 0) {
            zoomDirection = -Math.Sign(MouseInput.WheelDelta);
        }

        // (Un)lock current camera target
        if (Hotkeys.LockCamera.Pressed) {
            if (lockPosition != null) {
                lockPosition = null;
            } else if (cameraTargetPosition != null) {
                lockPosition = cameraTargetPosition.Value;
            }
        }

        // Account for upside down camera
        if (ExtendedVariantsInterop.UpsideDown) {
            moveOffset.Y *= -1;
        }

        bool changedCamera = false, changedCanvas = false;

        // Keep the gameplay canvas locked while using the FreeCamera hotkey
        if (Hotkeys.FreeCamera.Check && ZoomedOut) {
            canvasOffset += moveOffset;
            changedCanvas = moveOffset != Vector2.Zero;
        } else if (Hotkeys.InfoHud.Check) {
            cameraOffset += moveOffset;
            changedCamera = moveOffset != Vector2.Zero;
        }

        // Drag support while holding right mouse button
        if (MouseInput.Right.Check) {
            float scale = ZoomLevel * level.Camera.Zoom * (Celeste.Celeste.TargetWidth / Celeste.Celeste.GameWidth) * Engine.ViewWidth / Engine.Width;

            var mouseOffset = MouseInput.PositionDelta / scale;
            if (ExtendedVariantsInterop.UpsideDown) {
                mouseOffset.Y *= -1;
            }

            if (Hotkeys.FreeCamera.Check && ZoomedOut) {
                canvasOffset -= mouseOffset;
                changedCanvas = mouseOffset != Vector2.Zero;
            } else {
                cameraOffset -= mouseOffset;
                changedCamera = mouseOffset != Vector2.Zero;
            }
        }

        // Adjust camera zoom. Use faster speed for zooming out
        float delta = (viewportScale + zoomDirection * 0.1f) switch {
            > 1 => zoomDirection * 0.2f,
            _ => zoomDirection * 0.1f
        };
        viewportScale = Math.Max(0.2f, viewportScale + delta);

        if (cameraTargetPosition is { } target && level.Session.MapData.Bounds is var bounds) {
            if (changedCamera) {
                var result = (target + cameraOffset).Clamp(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
                cameraOffset = result - target;
            }
            if (changedCanvas) {
                var result = (target + cameraOffset + canvasOffset).Clamp(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
                canvasOffset = result - target - moveOffset;
            }
        }
    }

    /// Centers the camera onto the player or the currently locked position
    private static void AdjustCamera() {
        if (!TasSettings.CenterCamera || Engine.Scene is not Level level) {
            return;
        }

        var camera = level.Camera;
        if (Engine.Scene.GetPlayer() is { } player) {
            cameraTargetPosition = lockPosition ?? player.Position;
        } else if (Engine.Scene.Tracker.GetEntity<PlayerDeadBody>() is { } deadBody) {
            cameraTargetPosition = lockPosition ?? deadBody.Position;
        }

        if (cameraTargetPosition is not { } target) {
            return;
        }

        // Backup original values
        savedCameraPosition = camera.Position;
        savedLevelZoom = level.Zoom;
        savedLevelZoomTarget = level.ZoomTarget;
        savedLevelZoomFocusPoint = level.ZoomFocusPoint;
        savedLevelScreenPadding = level.ScreenPadding;

        // Apply camera changes
        if (TasSettings.CenterCameraHorizontallyOnly) {
            camera.Position = camera.Position with { X = camera.Position.X + target.X + cameraOffset.X - camera.Viewport.Width / 2.0f };
        } else {
            camera.Position = target + cameraOffset - new Vector2(camera.Viewport.Width / 2.0f, camera.Viewport.Height / 2.0f);
        }

        if (ExCameraDynamicsInterop.Enabled) {
            savedAutomaticZooming = ExCameraDynamicsInterop.AutomaticZooming;
            savedTriggerZoomOverride = ExCameraDynamicsInterop.TriggerZoomOverride;

            ExCameraDynamicsInterop.SetCamera(level, target + cameraOffset + canvasOffset, ZoomLevel);
        } else {
            level.Zoom = level.ZoomTarget = ZoomLevel;
            level.ZoomFocusPoint = new Vector2(Celeste.Celeste.GameWidth / 2.0f, Celeste.Celeste.GameHeight / 2.0f);
            if (ZoomedOut) {
                level.ZoomFocusPoint += canvasOffset;
            }
            level.ScreenPadding = 0;
        }

        // Prepare screen-space camera for usage with OffscreenHitbox
        ScreenCamera.Viewport.Width = (int) Math.Round(Celeste.Celeste.GameWidth * viewportScale);
        ScreenCamera.Viewport.Height = (int) Math.Round(Celeste.Celeste.GameHeight * viewportScale);
        if (TasSettings.CenterCameraHorizontallyOnly) {
            ScreenCamera.Position = ScreenCamera.Position with { X = ScreenCamera.Position.X + target.X + cameraOffset.X - ScreenCamera.Viewport.Width / 2f };
        } else {
            ScreenCamera.Position = target + cameraOffset - new Vector2(ScreenCamera.Viewport.Width / 2f, ScreenCamera.Viewport.Height / 2f);
        }

        if (ZoomedOut) {
            ScreenCamera.Position += canvasOffset;
        }

        ScreenCamera.UpdateMatrices();
    }

    /// Restore camera settings to previous values, to avoid altering gameplay
    private static void RestoreCamera() {
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

        if (savedAutomaticZooming != null) {
            ExCameraDynamicsInterop.AutomaticZooming = savedAutomaticZooming.Value;
            savedAutomaticZooming = null;
        }
        if (savedTriggerZoomOverride != null) {
            ExCameraDynamicsInterop.TriggerZoomOverride = savedTriggerZoomOverride.Value;
            savedTriggerZoomOverride = null;
        }
    }
}
