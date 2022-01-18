using System;
using Celeste;
using Celeste.Mod.UI;
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
        private static Vector2? lastPlayerPosition;
        private static Vector2 offset;
        private static float levelZoom = 1f;
        private static DateTime? arrowKeyPressTime;
        private static bool waitForAllResetKeysRelease;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            // note: change the camera.position before level.BeforeRender will cause desync 3A-roof04
            On.Celeste.Level.Render += LevelOnRender;
            On.Celeste.DustEdges.BeforeRender += DustEdgesOnBeforeRender;
            On.Celeste.MirrorSurfaces.BeforeRender += MirrorSurfacesOnBeforeRender;
            On.Celeste.LightingRenderer.BeforeRender += LightingRendererOnRender;
            On.Celeste.DisplacementRenderer.BeforeRender += DisplacementRendererOnBeforeRender;
            On.Celeste.HudRenderer.RenderContent += HudRendererOnRenderContent;
            On.Celeste.Mod.UI.SubHudRenderer.RenderContent += SubHudRendererOnRenderContent;
            On.Monocle.Commands.Render += CommandsOnRender;
            offset = new DynamicData(Engine.Instance).Get<Vector2?>("CelesteTAS_Offset") ?? Vector2.Zero;
            levelZoom = new DynamicData(Engine.Instance).Get<float?>("CelesteTAS_LevelZoom") ?? 1f;
        }

        public static void Unload() {
            On.Celeste.Level.Render -= LevelOnRender;
            On.Celeste.DustEdges.BeforeRender -= DustEdgesOnBeforeRender;
            On.Celeste.MirrorSurfaces.BeforeRender -= MirrorSurfacesOnBeforeRender;
            On.Celeste.LightingRenderer.BeforeRender -= LightingRendererOnRender;
            On.Celeste.DisplacementRenderer.BeforeRender -= DisplacementRendererOnBeforeRender;
            On.Celeste.HudRenderer.RenderContent -= HudRendererOnRenderContent;
            On.Celeste.Mod.UI.SubHudRenderer.RenderContent -= SubHudRendererOnRenderContent;
            On.Monocle.Commands.Render -= CommandsOnRender;
            new DynamicData(Engine.Instance).Set("CelesteTAS_Offset", offset);
            new DynamicData(Engine.Instance).Set("CelesteTAS_LevelZoom", levelZoom);
        }

        private static void CenterTheCamera(Action action) {
            if (Engine.Scene is not Level level) {
                action();
                return;
            }

            Camera camera = level.Camera;
            if (Settings.CenterCamera) {
                if (Engine.Scene.GetPlayer() is { } player) {
                    lastPlayerPosition = player.Position;
                }

                if (lastPlayerPosition != null) {
                    savedCameraPosition = camera.Position;
                    savedLevelZoom = level.Zoom;
                    camera.Position = lastPlayerPosition.Value + offset - new Vector2(camera.Viewport.Width / 2f, camera.Viewport.Height / 2f);
                    level.Zoom = levelZoom;
                }
            }

            action();

            if (savedCameraPosition != null) {
                camera.Position = savedCameraPosition.Value;
                savedCameraPosition = null;
            }

            if (savedLevelZoom != null) {
                level.Zoom = savedLevelZoom.Value;
                savedLevelZoom = null;
            }
        }

        private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
            CenterTheCamera(() => orig(self));
            MoveCamera(self);
            ZoomCamera();
        }

        private static float ArrowKeySensitivity {
            get {
                if (arrowKeyPressTime == null) {
                    return 1;
                }

                float sensitivity = (float) ((DateTime.Now - arrowKeyPressTime.Value).TotalMilliseconds / 200f);
                Calc.Clamp(sensitivity, 1, 6).DebugLog();
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

                    float scale = level.Zoom * ((320f - level.ScreenPadding * 2f) / 320f) * level.Camera.Zoom * 6f * Engine.ViewWidth / Engine.Width;
                    offset -= (MouseButtons.Position - MouseButtons.LastPosition) / scale;

                    if (lastPlayerPosition is { } playerPosition && level.Session.MapData.Bounds is var bounds) {
                        Vector2 result = (playerPosition + offset).Clamp(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
                        offset = result - playerPosition;
                    }
                }

                if (MouseButtons.Right.DoublePressed) {
                    offset = Vector2.Zero;
                    levelZoom = 1;
                }
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

        private static void DustEdgesOnBeforeRender(On.Celeste.DustEdges.orig_BeforeRender orig, DustEdges self) {
            CenterTheCamera(() => orig(self));
        }

        private static void MirrorSurfacesOnBeforeRender(On.Celeste.MirrorSurfaces.orig_BeforeRender orig, MirrorSurfaces self) {
            CenterTheCamera(() => orig(self));
        }

        private static void LightingRendererOnRender(On.Celeste.LightingRenderer.orig_BeforeRender orig, LightingRenderer self, Scene scene) {
            CenterTheCamera(() => orig(self, scene));
        }

        private static void DisplacementRendererOnBeforeRender(On.Celeste.DisplacementRenderer.orig_BeforeRender orig, DisplacementRenderer self,
            Scene scene) {
            CenterTheCamera(() => orig(self, scene));
        }

        private static void SubHudRendererOnRenderContent(On.Celeste.Mod.UI.SubHudRenderer.orig_RenderContent orig, SubHudRenderer self,
            Scene scene) {
            CenterTheCamera(() => orig(self, scene));
        }

        private static void HudRendererOnRenderContent(On.Celeste.HudRenderer.orig_RenderContent orig, HudRenderer self, Scene scene) {
            CenterTheCamera(() => orig(self, scene));
        }

        private static void CommandsOnRender(On.Monocle.Commands.orig_Render orig, Monocle.Commands self) {
            CenterTheCamera(() => orig(self));
        }
    }
}