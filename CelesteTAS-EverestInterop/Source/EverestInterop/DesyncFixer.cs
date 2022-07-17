using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

public static class DesyncFixer {
    private static readonly Action<DreamMirror> DreamMirrorBeforeRender =
        typeof(DreamMirror).GetMethodInfo("BeforeRender").CreateDelegate<Action<DreamMirror>>();

    private static readonly GetDelegate<FinalBoss, int> GetFacing = FastReflection.CreateGetDelegate<FinalBoss, int>("facing");
    private static readonly GetDelegate<FinalBoss, Wiggler> GetScaleWiggler = FastReflection.CreateGetDelegate<FinalBoss, Wiggler>("scaleWiggler");

    [Load]
    private static void Load() {
        On.Celeste.DreamMirror.ctor += DreamMirrorOnCtor;
        On.Celeste.FinalBoss.ctor_Vector2_Vector2Array_int_float_bool_bool_bool += FinalBossOnCtor_Vector2_Vector2Array_int_float_bool_bool_bool;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.DreamMirror.ctor -= DreamMirrorOnCtor;
        On.Celeste.FinalBoss.ctor_Vector2_Vector2Array_int_float_bool_bool_bool -= FinalBossOnCtor_Vector2_Vector2Array_int_float_bool_bool_bool;
    }

    private static void DreamMirrorOnCtor(On.Celeste.DreamMirror.orig_ctor orig, DreamMirror self, Vector2 position) {
        orig(self, position);

        self.Add(new PostUpdateHook(() => {
            if (Manager.Running) {
                // DreamMirror does some dirty stuff in BeforeRender.
                DreamMirrorBeforeRender(self);
            }
        }));
    }

    private static void FinalBossOnCtor_Vector2_Vector2Array_int_float_bool_bool_bool(
        On.Celeste.FinalBoss.orig_ctor_Vector2_Vector2Array_int_float_bool_bool_bool orig, FinalBoss self, Vector2 position, Vector2[] nodes,
        int patternIndex, float cameraYPastMax, bool dialog, bool startHit, bool cameraLockY) {
        orig(self, position, nodes, patternIndex, cameraYPastMax, dialog, startHit, cameraLockY);

        self.Add(new PostUpdateHook(() => {
            if (!Manager.Running) {
                return;
            }

            // FinalBoss does some dirty stuff in Render.
            // finalBoss.ShotOrigin => base.Center + Sprite.Position + new Vector2(6f * Sprite.Scale.X, 2f);
            if (self.Sprite is { } sprite) {
                sprite.Scale.X = GetFacing(self);
                sprite.Scale.Y = 1f;
                sprite.Scale *= 1f + GetScaleWiggler(self).Value * 0.2f;
            }
        }));
    }
}