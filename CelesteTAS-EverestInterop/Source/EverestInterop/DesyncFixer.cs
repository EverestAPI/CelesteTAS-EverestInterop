using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;

namespace TAS.EverestInterop;

public static class DesyncFixer {
    [Load]
    private static void Load() {
        On.Celeste.DreamMirror.ctor += DreamMirrorOnCtor;
        On.Celeste.FinalBoss.ctor_Vector2_Vector2Array_int_float_bool_bool_bool += FinalBossOnCtor_Vector2_Vector2Array_int_float_bool_bool_bool;
        On.Celeste.CS03_Memo.MemoPage.ctor += MemoPageOnCtor;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.DreamMirror.ctor -= DreamMirrorOnCtor;
        On.Celeste.FinalBoss.ctor_Vector2_Vector2Array_int_float_bool_bool_bool -= FinalBossOnCtor_Vector2_Vector2Array_int_float_bool_bool_bool;
        On.Celeste.CS03_Memo.MemoPage.ctor -= MemoPageOnCtor;
    }

    private static void DreamMirrorOnCtor(On.Celeste.DreamMirror.orig_ctor orig, DreamMirror self, Vector2 position) {
        orig(self, position);

        self.Add(new PostUpdateHook(() => {
            if (Manager.Running) {
                // DreamMirror does some dirty stuff in BeforeRender.
                self.BeforeRender();
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
                sprite.Scale.X = self.facing;
                sprite.Scale.Y = 1f;
                sprite.Scale *= 1f + self.scaleWiggler.Value * 0.2f;
            }
        }));
    }

    private static void MemoPageOnCtor(On.Celeste.CS03_Memo.MemoPage.orig_ctor orig, Entity self) {
        orig(self);
        self.Add(new PostUpdateHook(() => {
            if (Manager.Running && self is CS03_Memo.MemoPage {target: null} memoPage) {
                // initialize memoPage.target, fix game crash when fast forward
                memoPage.BeforeRender();
            }
        }));
    }
}