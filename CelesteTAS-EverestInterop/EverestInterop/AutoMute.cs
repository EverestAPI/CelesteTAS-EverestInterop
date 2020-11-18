using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using Microsoft.Xna.Framework;

namespace TAS.EverestInterop {
    public class AutoMute {
        public static AutoMute instance;
        private static bool shouldBeMute => Manager.FrameLoops >= 2 && CelesteTASModule.Settings.AutoMute;

        private int? lastSFXVolume;
        private EventInstance dummy;

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

        private EventInstance getDummyEventInstance() {
            if (dummy == null) {
                // this sound does exist, but is silent if we don't set any audio param to it.
                dummy = Audio.CreateInstance("event:/char/madeline/footstep");
                dummy.setVolume(0);
            }
            return dummy;
        }

        private EventInstance AudioOnPlay_string_string_float(On.Celeste.Audio.orig_Play_string_string_float orig,
            string path, string param, float value) {
            if (shouldBeMute) {
                return getDummyEventInstance();
            }

            return orig(path, param, value);
        }

        private EventInstance AudioOnPlay_string_Vector2_string_float_string_float(
            On.Celeste.Audio.orig_Play_string_Vector2_string_float_string_float orig, string path, Vector2 position,
            string param, float value, string param2, float value2) {
            if (shouldBeMute) {
                return getDummyEventInstance();
            }

            return orig(path, position, param, value, param2, value2);
        }

        private EventInstance AudioOnPlay_string_Vector2(On.Celeste.Audio.orig_Play_string_Vector2 orig, string path,
            Vector2 position) {
            if (shouldBeMute) {
                return getDummyEventInstance();
            }

            return orig(path, position);
        }

        private EventInstance AudioOnPlay_string(On.Celeste.Audio.orig_Play_string orig, string path) {
            if (shouldBeMute) {
                return getDummyEventInstance();
            }

            return orig(path);
        }

        private SoundSource SoundSourceOnPlay(On.Celeste.SoundSource.orig_Play orig, SoundSource self, string path,
            string param, float value) {
            if (shouldBeMute) {
                return self;
            }

            return orig(self, path, param, value);
        }

        private void SceneOnUpdate(On.Monocle.Scene.orig_Update orig, Monocle.Scene self) {
            orig(self);

            if (shouldBeMute && lastSFXVolume == null) {
                lastSFXVolume = Settings.Instance.SFXVolume;
                Settings.Instance.SFXVolume = 0;
                Settings.Instance.ApplyVolumes();
            }

            if (!shouldBeMute && lastSFXVolume != null) {
                Settings.Instance.SFXVolume = (int) lastSFXVolume;
                Settings.Instance.ApplyVolumes();
                lastSFXVolume = null;
            }
        }
    }
}