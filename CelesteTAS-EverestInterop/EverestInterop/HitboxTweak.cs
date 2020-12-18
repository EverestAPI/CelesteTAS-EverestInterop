using Celeste;
using Monocle;

namespace TAS.EverestInterop {
    public class HitboxTweak {
        public static HitboxTweak instance;
        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

        public void Load() {
            On.Monocle.Entity.DebugRender += HideHitbox;
        }

        public void Unload() {
            On.Monocle.Entity.DebugRender -= HideHitbox;
        }

        private static void HideHitbox(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
            if (self is Trigger && Settings.HideTriggerHitbox) {
                return;
            }
            orig(self, camera);
        }
    }
}