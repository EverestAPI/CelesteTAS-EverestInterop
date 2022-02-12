using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;
using TAS.EverestInterop.InfoHUD;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class CenterCamera {
        private static Vector2? savedCameraPosition;
        private static float? savedLevelZoom;
        private static float? savedLevelZoomTarget;
        private static Vector2? savedLevelZoomFocusPoint;
        private static Vector2? lastPlayerPosition;
        private static Vector2 offset;
        private static float levelZoom = 1f;
        private static DateTime? arrowKeyPressTime;
        private static bool waitForAllResetKeysRelease;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            On.Monocle.Engine.RenderCore += EngineOnRenderCore;
            On.Monocle.Commands.Render += CommandsOnRender;
            On.Celeste.Level.Render += LevelOnRender;
            offset = new DynamicData(Engine.Instance).Get<Vector2?>("CelesteTAS_Offset") ?? Vector2.Zero;
            levelZoom = new DynamicData(Engine.Instance).Get<float?>("CelesteTAS_LevelZoom") ?? 1f;
        }

        public static void Unload() {
            On.Monocle.Engine.RenderCore -= EngineOnRenderCore;
            On.Monocle.Commands.Render -= CommandsOnRender;
            On.Celeste.Level.Render -= LevelOnRender;
            new DynamicData(Engine.Instance).Set("CelesteTAS_Offset", offset);
            new DynamicData(Engine.Instance).Set("CelesteTAS_LevelZoom", levelZoom);
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
        }

        private static void CenterTheCamera() {
            if (Engine.Scene is not Level level || !Settings.CenterCamera) {
                return;
            }

            Camera camera = level.Camera;
            if (Engine.Scene.GetPlayer() is { } player) {
                lastPlayerPosition = player.Position;
            }

            if (lastPlayerPosition != null) {
                savedCameraPosition = camera.Position;
                savedLevelZoom = level.Zoom;
                savedLevelZoomTarget = level.ZoomTarget;
                savedLevelZoomFocusPoint = level.ZoomFocusPoint;

                camera.Position = lastPlayerPosition.Value + offset - new Vector2(camera.Viewport.Width / 2f, camera.Viewport.Height / 2f);
                level.Zoom = levelZoom;
                level.ZoomTarget = levelZoom;
                level.ZoomFocusPoint = new Vector2(320f, 180f) / 2f;
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
                savedLevelZoom = null;
            }

            if (savedLevelZoomFocusPoint != null) {
                level.ZoomFocusPoint = savedLevelZoomFocusPoint.Value;
                savedLevelZoomFocusPoint = null;
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

        private static void MoveCamera(Level level) {
            if (!Settings.CenterCamera) {
                return;
            }

            if (Hotkeys.InfoHud.Check) {
                // info hud hotkey + arrow key
                if (waitForAllResetKeysRelease) {
                    if (!Hotkeys.CameraUp.Check && !Hotkeys.CameraDown.Check) {
                        waitForAllResetKeysRelease = false;
                    }
                } else {
                    if (!Hotkeys.CameraUp.Check && !Hotkeys.CameraDown.Check && !Hotkeys.CameraLeft.Check && !Hotkeys.CameraRight.Check) {
                        arrowKeyPressTime = null;
                    } else if (arrowKeyPressTime == null) {
                        arrowKeyPressTime = DateTime.Now;
                    }

                    if (Hotkeys.CameraUp.Check) {
                        offset += new Vector2(0, -ArrowKeySensitivity);
                    }

                    if (Hotkeys.CameraDown.Check) {
                        offset += new Vector2(0, ArrowKeySensitivity);
                    }

                    if (Hotkeys.CameraLeft.Check) {
                        offset += new Vector2(-ArrowKeySensitivity, 0);
                    }

                    if (Hotkeys.CameraRight.Check) {
                        offset += new Vector2(ArrowKeySensitivity, 0);
                    }

                    if (Hotkeys.CameraUp.Check && Hotkeys.CameraDown.Check) {
                        offset = Vector2.Zero;
                        levelZoom = 1f;
                        waitForAllResetKeysRelease = true;
                    }
                }
            } else {
                // mouse right button
                if (MouseButtons.Right.LastCheck && MouseButtons.Right.Check) {
                    InfoMouse.DrawCursor(MouseButtons.Position);

                    float scale = level.Zoom * levelZoom * ((320f - level.ScreenPadding * 2f) / 320f) * level.Camera.Zoom * 6f * Engine.ViewWidth /
                                  Engine.Width;
                    offset -= (MouseButtons.Position - MouseButtons.LastPosition) / scale;
                }

                if (MouseButtons.Right.DoublePressed) {
                    offset = Vector2.Zero;
                    levelZoom = 1;
                }
            }

            if (lastPlayerPosition is { } playerPosition && level.Session.MapData.Bounds is var bounds) {
                Vector2 result = (playerPosition + offset).Clamp(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
                offset = result - playerPosition;
            }
        }

        private static void ZoomCamera() {
            if (!Settings.CenterCamera) {
                return;
            }

            if (Hotkeys.InfoHud.Check) {
                if (Hotkeys.CameraZoomIn.Check) {
                    levelZoom += 0.05f;
                }

                if (Hotkeys.CameraZoomOut.Check) {
                    levelZoom -= 0.05f;
                }
            } else {
                levelZoom += Math.Sign(MouseButtons.Wheel) * 0.05f;
            }

            if (levelZoom < 1f) {
                levelZoom = 1f;
            }
        }
    }
}