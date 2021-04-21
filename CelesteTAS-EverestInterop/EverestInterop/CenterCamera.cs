using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.EverestInterop {
    public static class CenterCamera {
        private static Vector2? savedCameraPosition;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            // note: change the camera.position before level.BeforeRender will cause desync 3A-roof04
            On.Celeste.Level.Render += LevelOnRender;
            On.Celeste.LightingRenderer.BeforeRender += LightingRendererOnRender;
            On.Celeste.DustEdges.Render += DustEdgesOnRender;
        }

        public static void Unload() {
            On.Celeste.Level.Render -= LevelOnRender;
            On.Celeste.LightingRenderer.BeforeRender -= LightingRendererOnRender;
            On.Celeste.DustEdges.Render -= DustEdgesOnRender;
        }

        private static void CenterTheCamera(Action action) {
            Camera camera = (Engine.Scene as Level)?.Camera;
            if (Settings.CenterCamera && camera != null && Engine.Scene.GetPlayer() is { } player) {
                savedCameraPosition = camera.Position;
                camera.Position = player.Position - new Vector2(camera.Viewport.Width / 2f, camera.Viewport.Height / 2f);
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