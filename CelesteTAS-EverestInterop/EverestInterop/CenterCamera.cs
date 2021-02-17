using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop {
    class CenterCamera {
        public static CenterCamera Instance;
        private Camera savedCamera;

        public static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public void Load() {
            // Optional: Center the camera
            On.Celeste.Level.BeforeRender += Level_BeforeRender;
            On.Celeste.Level.AfterRender += Level_AfterRender;
        }

        public void Unload() {
            On.Celeste.Level.BeforeRender -= Level_BeforeRender;
            On.Celeste.Level.AfterRender -= Level_AfterRender;
        }

        private void Level_BeforeRender(On.Celeste.Level.orig_BeforeRender orig, Level self) {
            orig.Invoke(self);
            if (Settings.CenterCamera) {
                Player player = self.Tracker.GetEntity<Player>();
                if (player != null) {
                    savedCamera = self.Camera;
                    Vector2 cameraPosition = player.Position - new Vector2(savedCamera.Viewport.Width / 2, savedCamera.Viewport.Height / 2);
                    self.Camera.Position = cameraPosition;
                } else {
                    savedCamera = null;
                }
            }
        }

        private void Level_AfterRender(On.Celeste.Level.orig_AfterRender orig, Level self) {
            if (savedCamera != null) {
                self.Camera.CopyFrom(savedCamera);
            }

            orig.Invoke(self);
        }
    }
}