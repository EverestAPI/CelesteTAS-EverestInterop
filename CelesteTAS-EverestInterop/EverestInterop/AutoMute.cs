using Celeste;

namespace TAS.EverestInterop {
    public class AutoMute {
        public static AutoMute instance;
        private int? lastSFXVolume;

        public void Load() {
            On.Monocle.Scene.Update += SceneOnUpdate;
        }

        public void Unload() {
            On.Monocle.Scene.Update -= SceneOnUpdate;
        }

        private void SceneOnUpdate(On.Monocle.Scene.orig_Update orig, Monocle.Scene self) {
            orig(self);

            if (Manager.FrameLoops > 1 && lastSFXVolume == null) {
                lastSFXVolume = Settings.Instance.SFXVolume;
                Settings.Instance.SFXVolume = 0;
                Settings.Instance.ApplyVolumes();
            }

            if (Manager.FrameLoops < 2 && lastSFXVolume != null) {
                Settings.Instance.SFXVolume = (int) lastSFXVolume;
                Settings.Instance.ApplyVolumes();
                lastSFXVolume = null;
            }
        }
    }
}