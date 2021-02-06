using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using FMOD.Studio;
using Microsoft.Xna.Framework;

namespace TAS.EverestInterop {
public static class AutoMute {
    private static WeakReference<EventInstance> dummy;
    private static int? lastSFXVolume;
    private static readonly FieldInfo SoundSourceInstance = typeof(SoundSource).GetFieldInfo("instance");

    private static readonly HashSet<string> LoopAudioPaths = new HashSet<string> {
        "event:/char/madeline/wallslide",
        "event:/char/madeline/dreamblock_travel",
        "event:/game/03_resort/platform_vert_down_loop",
        "event:/game/03_resort/platform_vert_up_loop",
        "event:/game/05_mirror_temple/redbooster_move",
        "event:/game/05_mirror_temple/swapblock_return",
        "event:/game/06_reflection/crushblock_move_loop",
        "event:/game/06_reflection/crushblock_move_loop_covert",
        "event:/game/06_reflection/crushblock_return_loop",
        "event:/game/06_reflection/feather_state_loop",
    };

    private static readonly List<WeakReference<EventInstance>> LoopAudioInstances = new List<WeakReference<EventInstance>>();
    private static bool shouldBeMute => Manager.FrameLoops >= 2 && CelesteTASModule.Settings.AutoMute;
    private static bool frameStep => Manager.Running && (Manager.state & State.FrameStep) != 0;

    public static void Load() {
        On.Celeste.SoundSource.Play += SoundSourceOnPlay;
        On.Celeste.Audio.Play_string += AudioOnPlay_string;
        On.Celeste.Audio.Play_string_Vector2 += AudioOnPlay_string_Vector2;
        On.Celeste.Audio.Play_string_string_float += AudioOnPlay_string_string_float;
        On.Celeste.Audio.Play_string_Vector2_string_float_string_float += AudioOnPlay_string_Vector2_string_float_string_float;
        On.Celeste.Audio.CreateInstance += AudioOnCreateInstance;
        On.Monocle.Scene.Update += SceneOnUpdate;
        On.Celeste.Level.Render += LevelOnRender;
    }

    public static void Unload() {
        On.Celeste.SoundSource.Play -= SoundSourceOnPlay;
        On.Celeste.Audio.Play_string -= AudioOnPlay_string;
        On.Celeste.Audio.Play_string_Vector2 -= AudioOnPlay_string_Vector2;
        On.Celeste.Audio.Play_string_string_float -= AudioOnPlay_string_string_float;
        On.Celeste.Audio.Play_string_Vector2_string_float_string_float -= AudioOnPlay_string_Vector2_string_float_string_float;
        On.Monocle.Scene.Update -= SceneOnUpdate;
        On.Celeste.Level.Render -= LevelOnRender;
    }

    private static EventInstance getDummyEventInstance() {
        if (dummy == null || !dummy.TryGetTarget(out EventInstance dummyInstance)) {
            // this sound does exist, but is silent if we don't set any audio param to it.
            dummyInstance = Audio.CreateInstance("event:/char/madeline/footstep");
            dummyInstance.setVolume(0);
            dummy = new WeakReference<EventInstance>(dummyInstance);
        }

        return dummyInstance;
    }

    private static EventInstance AudioOnPlay_string_string_float(On.Celeste.Audio.orig_Play_string_string_float orig,
        string path, string param, float value) {
        if (shouldBeMute) {
            return getDummyEventInstance();
        }

        return orig(path, param, value);
    }

    private static EventInstance AudioOnPlay_string_Vector2_string_float_string_float(
        On.Celeste.Audio.orig_Play_string_Vector2_string_float_string_float orig, string path, Vector2 position,
        string param, float value, string param2, float value2) {
        if (shouldBeMute) {
            return getDummyEventInstance();
        }

        return orig(path, position, param, value, param2, value2);
    }

    private static EventInstance AudioOnPlay_string_Vector2(On.Celeste.Audio.orig_Play_string_Vector2 orig, string path,
        Vector2 position) {
        if (shouldBeMute) {
            return getDummyEventInstance();
        }

        return orig(path, position);
    }

    private static EventInstance AudioOnPlay_string(On.Celeste.Audio.orig_Play_string orig, string path) {
        if (shouldBeMute) {
            return getDummyEventInstance();
        }

        return orig(path);
    }

    private static SoundSource SoundSourceOnPlay(On.Celeste.SoundSource.orig_Play orig, SoundSource self, string path,
        string param, float value) {
        if (shouldBeMute) {
            return self;
        }

        SoundSource soundSource = orig(self, path, param, value);
        if (path != null && LoopAudioPaths.Contains(path) && SoundSourceInstance.GetValue(soundSource) is EventInstance eventInstance) {
            LoopAudioInstances.Add(new WeakReference<EventInstance>(eventInstance));
        }

        return soundSource;
    }

    private static EventInstance AudioOnCreateInstance(On.Celeste.Audio.orig_CreateInstance orig, string path, Vector2? position) {
        EventInstance eventInstance = orig(path, position);
        if (path != null && LoopAudioPaths.Contains(path)) {
            LoopAudioInstances.Add(new WeakReference<EventInstance>(eventInstance));
        }

        return eventInstance;
    }

    private static void SceneOnUpdate(On.Monocle.Scene.orig_Update orig, Monocle.Scene self) {
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

    private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);

        if (frameStep) {
            foreach (WeakReference<EventInstance> loopAudioInstance in LoopAudioInstances) {
                if (loopAudioInstance.TryGetTarget(out EventInstance eventInstance)) {
                    eventInstance.setVolume(0);
                }
            }

            LoopAudioInstances.Clear();
        }
    }
}
}