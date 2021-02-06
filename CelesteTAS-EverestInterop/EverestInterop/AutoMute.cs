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
        "event:/char/madeline/water_move_shallow",
        "event:/ui/game/memorial_dream_loop",
        "event:/ui/game/memorial_dream_text_loop",
        "event:/ui/game/memorial_text_loop",
        "event:/game/general/birdbaby_tweet_loop",
        "event:/game/general/crystalheart_blue_get",
        "event:/game/general/crystalheart_red_get",
        "event:/game/general/crystalheart_gold_get",
        "event:/game/00_prologue/bridge_rumble_loop",
        "event:/game/01_forsaken_city/birdbros_fly_loop",
        "event:/game/01_forsaken_city/console_static_loop",
        "event:/game/02_old_site/sequence_phone_ring_loop",
        "event:/game/02_old_site/sequence_phone_ringtone_loop",
        "event:/game/03_resort/platform_vert_down_loop",
        "event:/game/03_resort/platform_vert_up_loop",
        "event:/game/04_cliffside/arrowblock_move",
        "event:/game/04_cliffside/gondola_movement_loop",
        "event:/game/04_cliffside/gondola_halted_loop",
        "event:/game/04_cliffside/gondola_movement_loop",
        "event:/game/05_mirror_temple/mainmirror_torch_loop",
        "event:/game/05_mirror_temple/redbooster_move",
        "event:/game/05_mirror_temple/swapblock_return",
        "event:/game/06_reflection/badeline_pull_rumble_loop",
        "event:/game/06_reflection/crushblock_move_loop",
        "event:/game/06_reflection/crushblock_move_loop_covert",
        "event:/game/06_reflection/crushblock_return_loop",
        "event:/game/06_reflection/feather_state_loop",
        "event:/game/06_reflection/badeline_pull_rumble_loop",
        "event:/game/09_core/conveyor_activate",
        "event:/game/09_core/rising_threat",
        "event:/new_content/game/10_farewell/glider_movement",
        "event:/new_content/game/10_farewell/fakeheart_get",
    };

    private const int DelayFrames = 30;

    private static readonly Dictionary<WeakReference<EventInstance>, int> LoopAudioInstances = new Dictionary<WeakReference<EventInstance>, int>();
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
        On.Celeste.Audio.CreateInstance -= AudioOnCreateInstance;
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
        if (path != null && (LoopAudioPaths.Contains(path) || path.StartsWith("event:/env/local/") || path.StartsWith("event:/new_content/env")) &&
            SoundSourceInstance.GetValue(soundSource) is EventInstance eventInstance) {
            LoopAudioInstances.Add(new WeakReference<EventInstance>(eventInstance), DelayFrames);
        }

        return soundSource;
    }

    private static EventInstance AudioOnCreateInstance(On.Celeste.Audio.orig_CreateInstance orig, string path, Vector2? position) {
        EventInstance eventInstance = orig(path, position);
        if (path != null && (LoopAudioPaths.Contains(path) || path.StartsWith("event:/env/local/") || path.StartsWith("event:/new_content/env"))) {
            LoopAudioInstances.Add(new WeakReference<EventInstance>(eventInstance), DelayFrames);
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
            Audio.CurrentAmbienceEventInstance?.setVolume(0);

            if (LoopAudioInstances.Count > 0) {
                List<WeakReference<EventInstance>> copy = new List<WeakReference<EventInstance>>(LoopAudioInstances.Keys);
                foreach (WeakReference<EventInstance> loopAudioInstance in copy) {
                    if (loopAudioInstance.TryGetTarget(out EventInstance eventInstance) && LoopAudioInstances[loopAudioInstance] < 0) {
                        eventInstance.setVolume(0);
                        LoopAudioInstances.Remove(loopAudioInstance);
                    } else {
                        LoopAudioInstances[loopAudioInstance]--;
                    }
                }
            }
        }
    }
}
}