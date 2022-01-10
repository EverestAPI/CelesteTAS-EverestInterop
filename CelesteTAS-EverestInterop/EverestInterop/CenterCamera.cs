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
        private static Vector2? lastPlayerPosition;
        private static Vector2 offset;
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
        }

        private static void CenterTheCamera(Action action) {
            Camera camera = (Engine.Scene as Level)?.Camera;
            if (Settings.CenterCamera && camera != null) {
                if (Engine.Scene.GetPlayer() is { } player) {
                    lastPlayerPosition = player.Position;
                }

                if (lastPlayerPosition != null) {
                    savedCameraPosition = camera.Position;
                    camera.Position = lastPlayerPosition.Value + offset - new Vector2(camera.Viewport.Width / 2f, camera.Viewport.Height / 2f);
                }
            }

            action();

            if (savedCameraPosition != null && camera != null) {
                camera.Position = savedCameraPosition.Value;
                savedCameraPosition = null;
            }
        }

        private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
            CenterTheCamera(() => orig(self));
            MoveCamera(self);
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
                // info hub hotkey + arrow key
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
                        waitForAllResetKeysRelease = true;
                    }
                }
            } else {
                // mouse right button
                if (MouseButtons.Right.LastCheck && MouseButtons.Right.Check) {
                    InfoMouse.DrawCursor(MouseButtons.Position);

                    float scale = level.Zoom * ((320f - level.ScreenPadding * 2f) / 320f) * level.Camera.Zoom * 6f * Engine.ViewWidth / Engine.Width;
                    offset -= (MouseButtons.Position - MouseButtons.LastPosition) / scale;

                    if (lastPlayerPosition is { } playerPosition && Engine.Scene.GetSession()?.MapData.Bounds is { } bounds) {
                        Vector2 result = (playerPosition + offset).Clamp(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
                        offset = result - playerPosition;
                    }
                }

                if (MouseButtons.Right.DoublePressed) {
                    offset = Vector2.Zero;
                }
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