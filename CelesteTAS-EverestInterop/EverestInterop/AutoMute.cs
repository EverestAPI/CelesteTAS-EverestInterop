using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using FMOD;
using FMOD.Studio;

namespace TAS.EverestInterop {
public static class AutoMute {
    private static readonly HashSet<string> LoopAudioPaths = new HashSet<string> {
        "event:/char/madeline/wallslide",
        "event:/char/madeline/dreamblock_travel",
        "event:/char/madeline/water_move_shallow",
        "event:/char/badeline/boss_bullet",
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

    private static readonly Dictionary<WeakReference<EventInstance>, int> LoopAudioInstances = new Dictionary<WeakReference<EventInstance>, int>();
    private static bool settingMusic;
    private static CelesteTASModuleSettings tasSettings => CelesteTASModule.Settings;
    private static bool shouldBeMute => Manager.FrameLoops >= 2 && CelesteTASModule.Settings.AutoMute && !settingMusic;
    private static bool frameStep => Manager.Running && (Manager.state & State.FrameStep) != 0;

    public static void Load() {
        On.Celeste.Audio.SetMusic += AudioOnSetMusic;
        On.Celeste.Audio.SetAltMusic += AudioOnSetAltMusic;
        On.FMOD.Studio.EventDescription.createInstance += EventDescriptionOnCreateInstance;
        On.Monocle.Scene.Update += SceneOnUpdate;
        On.Celeste.Level.Render += LevelOnRender;
    }

    public static void Unload() {
        On.Celeste.Audio.SetMusic -= AudioOnSetMusic;
        On.Celeste.Audio.SetAltMusic -= AudioOnSetAltMusic;
        On.FMOD.Studio.EventDescription.createInstance -= EventDescriptionOnCreateInstance;
        On.Monocle.Scene.Update -= SceneOnUpdate;
        On.Celeste.Level.Render -= LevelOnRender;
    }

    private static void AudioOnSetAltMusic(On.Celeste.Audio.orig_SetAltMusic orig, string path) {
        settingMusic = true;
        orig(path);
        settingMusic = false;
    }

    private static bool AudioOnSetMusic(On.Celeste.Audio.orig_SetMusic orig, string path, bool startPlaying, bool allowFadeOut) {
        settingMusic = true;
        bool result = orig(path, startPlaying, allowFadeOut);
        settingMusic = false;
        return result;
    }

    private static RESULT EventDescriptionOnCreateInstance(On.FMOD.Studio.EventDescription.orig_createInstance orig, EventDescription self,
        out EventInstance instance) {
        RESULT result = orig(self, out instance);

        if (instance != null && self.getPath(out string path) == RESULT.OK && path != null) {
            if (shouldBeMute) {
                instance.setVolume(0);
            }

            int delayFrames = -1;
            if (LoopAudioPaths.Contains(path)) {
                delayFrames = 10;
            } else if (path.StartsWith("event:/env/local/") || path.StartsWith("event:/new_content/env/")) {
                delayFrames = 0;
            }

            if (delayFrames >= 0) {
                LoopAudioInstances.Add(new WeakReference<EventInstance>(instance), delayFrames);
            }
        }

        return result;
    }

    private static void SceneOnUpdate(On.Monocle.Scene.orig_Update orig, Monocle.Scene self) {
        orig(self);

        if (shouldBeMute && tasSettings.LastSFXVolume < 0) {
            tasSettings.LastSFXVolume = Settings.Instance.SFXVolume;
            CelesteTASModule.Instance.SaveSettings();
            Settings.Instance.SFXVolume = 0;
            Settings.Instance.ApplyVolumes();
        }

        if (!shouldBeMute && tasSettings.LastSFXVolume >= 0) {
            Settings.Instance.SFXVolume = tasSettings.LastSFXVolume;
            Settings.Instance.ApplyVolumes();
            tasSettings.LastSFXVolume = -1;
            CelesteTASModule.Instance.SaveSettings();
        }
    }

    private static void LevelOnRender(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);

        if (frameStep) {
            Audio.CurrentAmbienceEventInstance?.setVolume(0);

            if (LoopAudioInstances.Count > 0) {
                WeakReference<EventInstance>[] copy = LoopAudioInstances.Keys.ToArray();
                foreach (WeakReference<EventInstance> loopAudioInstance in copy) {
                    if (loopAudioInstance.TryGetTarget(out EventInstance eventInstance)) {
                        if (LoopAudioInstances[loopAudioInstance] <= 0) {
                            eventInstance.setVolume(0);
                            LoopAudioInstances.Remove(loopAudioInstance);
                        } else {
                            LoopAudioInstances[loopAudioInstance]--;
                        }
                    } else {
                        LoopAudioInstances.Remove(loopAudioInstance);
                    }
                }
            }
        }
    }
}
}