using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework;

namespace TAS.EverestInterop {
    public class AutoMute {
        public static AutoMute instance;
        private int? lastSFXVolume;

        public void Load() {
            On.Monocle.Scene.Update += SceneOnUpdate;
            On.Celeste.SoundSource.Play += SoundSourceOnPlay;
            On.Celeste.Audio.Play_string += AudioOnPlay_string;
            On.Celeste.Audio.Play_string_Vector2 += AudioOnPlay_string_Vector2;
            On.Celeste.Audio.Play_string_string_float += AudioOnPlay_string_string_float;
            On.Celeste.Audio.Play_string_Vector2_string_float_string_float +=
                AudioOnPlay_string_Vector2_string_float_string_float;
        }

        public void Unload() {
            On.Monocle.Scene.Update -= SceneOnUpdate;
            On.Celeste.SoundSource.Play -= SoundSourceOnPlay;
            On.Celeste.Audio.Play_string -= AudioOnPlay_string;
            On.Celeste.Audio.Play_string_Vector2 -= AudioOnPlay_string_Vector2;
            On.Celeste.Audio.Play_string_string_float -= AudioOnPlay_string_string_float;
            On.Celeste.Audio.Play_string_Vector2_string_float_string_float -=
                AudioOnPlay_string_Vector2_string_float_string_float;
        }

        private EventInstance AudioOnPlay_string_string_float(On.Celeste.Audio.orig_Play_string_string_float orig,
            string path, string param, float value) {
            EventInstance eventInstance = orig(path, param, value);
            if (Manager.FrameLoops >= 2) {
                eventInstance?.setVolume(0);
            }

            return eventInstance;
        }

        private EventInstance AudioOnPlay_string_Vector2_string_float_string_float(
            On.Celeste.Audio.orig_Play_string_Vector2_string_float_string_float orig, string path, Vector2 position,
            string param, float value, string param2, float value2) {
            EventInstance eventInstance = orig(path, position, param, value, param2, value2);
            if (Manager.FrameLoops >= 2) {
                eventInstance?.setVolume(0);
            }

            return eventInstance;
        }

        private EventInstance AudioOnPlay_string_Vector2(On.Celeste.Audio.orig_Play_string_Vector2 orig, string path,
            Vector2 position) {
            EventInstance eventInstance = orig(path, position);
            if (Manager.FrameLoops >= 2) {
                eventInstance?.setVolume(0);
            }

            return eventInstance;
        }

        private EventInstance AudioOnPlay_string(On.Celeste.Audio.orig_Play_string orig, string path) {
            EventInstance eventInstance = orig(path);
            if (Manager.FrameLoops >= 2) {
                eventInstance?.setVolume(0);
            }

            return eventInstance;
        }

        private SoundSource SoundSourceOnPlay(On.Celeste.SoundSource.orig_Play orig, SoundSource self, string path,
            string param, float value) {
            if (Manager.FrameLoops >= 2) {
                path = "";
            }
            
            return orig(self, path, param, value);
        }

        private void SceneOnUpdate(On.Monocle.Scene.orig_Update orig, Monocle.Scene self) {
            orig(self);

            if (Manager.FrameLoops >= 2 && lastSFXVolume == null) {
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