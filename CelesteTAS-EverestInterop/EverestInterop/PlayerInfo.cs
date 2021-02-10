using System;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop {
public static class PlayerInfo {
    public static int TransitionFrames { get; private set; }
    private static float framesPerSecond => 60f / Engine.TimeRateB;

    public static void Load() {
        Everest.Events.Level.OnTransitionTo += LevelOnOnTransitionTo;
        On.Celeste.Level.Update += LevelOnUpdate;
    }

    public static void Unload() {
        Everest.Events.Level.OnTransitionTo -= LevelOnOnTransitionTo;
        On.Celeste.Level.Update -= LevelOnUpdate;
    }

    private static void LevelOnOnTransitionTo(Level level, LevelData next, Vector2 direction) {
        TransitionFrames = GetTransitionFrames(level, next);
    }

    private static void LevelOnUpdate(On.Celeste.Level.orig_Update orig, Level self) {
        orig(self);

        if (TransitionFrames > 0) {
            TransitionFrames--;
        }
    }

    private static int GetTransitionFrames(Level level, LevelData nextLevelData) {
        int result = 0;
        Session session = level.Session;

        bool DarkRoom = nextLevelData.Dark && !session.GetFlag("ignore_darkness_" + nextLevelData.Name);

        float lightingStart = level.Lighting.Alpha;
        float lightingCurrent = lightingStart;
        float lightingEnd = DarkRoom ? session.DarkRoomAlpha : level.BaseLightingAlpha + session.LightingAlphaAdd;
        bool lightingWait = lightingStart >= session.DarkRoomAlpha || lightingEnd >= session.DarkRoomAlpha;
        if (lightingWait) {
            while (Math.Abs(lightingCurrent - lightingEnd) > 0.000001f) {
                result++;
                lightingCurrent = Calc.Approach(lightingCurrent, lightingEnd, 2f * Engine.DeltaTime);
            }
        }

        result += (int) (level.NextTransitionDuration * framesPerSecond) + 2;
        return result;
    }
}
}