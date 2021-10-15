using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class CenterCamera {
        private static Vector2? savedCameraPosition;
        private static Vector2? lastPlayerPosition;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            // note: change the camera.position before level.BeforeRender will cause desync 3A-roof04
            On.Celeste.Level.Render += LevelOnRender;
            On.Celeste.LightingRenderer.BeforeRender += LightingRendererOnRender;
            On.Celeste.TalkComponent.TalkComponentUI.Render += TalkComponentUIOnRender;
            On.Celeste.BirdTutorialGui.Render += BirdTutorialGuiOnRender;
            On.Celeste.DustEdges.Render += DustEdgesOnRender;
        }

        public static void Unload() {
            On.Celeste.Level.Render -= LevelOnRender;
            On.Celeste.LightingRenderer.BeforeRender -= LightingRendererOnRender;
            On.Celeste.TalkComponent.TalkComponentUI.Render -= TalkComponentUIOnRender;
            On.Celeste.BirdTutorialGui.Render += BirdTutorialGuiOnRender;
            On.Celeste.DustEdges.Render -= DustEdgesOnRender;
        }

        private static void CenterTheCamera(Action action) {
            Camera camera = (Engine.Scene as Level)?.Camera;
            if (Settings.CenterCamera && camera != null) {
                if (Engine.Scene.GetPlayer() is { } player) {
                    lastPlayerPosition = player.Position;
                }

                if (lastPlayerPosition != null) {
                    savedCameraPosition = camera.Position;
                    camera.Position = lastPlayerPosition.Value - new Vector2(camera.Viewport.Width / 2f, camera.Viewport.Height / 2f);
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
        }

        private static void LightingRendererOnRender(On.Celeste.LightingRenderer.orig_BeforeRender orig, LightingRenderer self, Scene scene) {
            CenterTheCamera(() => orig(self, scene));
        }

        private static void TalkComponentUIOnRender(On.Celeste.TalkComponent.TalkComponentUI.orig_Render orig, TalkComponent.TalkComponentUI self) {
            CenterTheCamera(() => orig(self));
        }

        private static void BirdTutorialGuiOnRender(On.Celeste.BirdTutorialGui.orig_Render orig, BirdTutorialGui self) {
            CenterTheCamera(() => orig(self));
        }

        private static void DustEdgesOnRender(On.Celeste.DustEdges.orig_Render orig, DustEdges self) {
            Vector2? origCameraPosition = null;
            Camera camera = self.SceneAs<Level>()?.Camera;
            if (Settings.CenterCamera && savedCameraPosition != null && camera != null) {
                origCameraPosition = camera.Position;
                camera.Position = savedCameraPosition.Value;
            }

            orig(self);

            if (origCameraPosition != null) {
                camera.Position = origCameraPosition.Value;
            }
        }
    }
}