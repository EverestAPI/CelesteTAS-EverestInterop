using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

// ReSharper disable AssignNullToNotNullAttribute
public static class DesyncFixer {
    [Initialize]
    private static void Initialize() {
        if (ModUtils.GetType("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Entities.WonkyCassetteBlockController")
                ?.GetMethodInfo("Engine_Update") is { } methodInfo) {
            methodInfo.IlHook((cursor, _) => {
                if (cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<Engine>("DashAssistFreeze"))) {
                    cursor.Emit(OpCodes.Call, typeof(Manager).GetProperty(nameof(Manager.SkipFrame)).GetGetMethod()).Emit(OpCodes.Or);
                }
            });
        }

        Dictionary<MethodInfo, int> methods = new() {
            {typeof(Debris).GetMethod(nameof(Debris.orig_Init)), 1},
            {typeof(Debris).GetMethod(nameof(Debris.Init), new[] {typeof(Vector2), typeof(char), typeof(bool)}), 1},
            {typeof(Debris).GetMethod(nameof(Debris.BlastFrom)), 1},
            {typeof(MoveBlock.Debris).GetMethod(nameof(MoveBlock.Debris.Init)), 1}
        };

        foreach (Type type in ModUtils.GetTypes()) {
            if (type.Name.EndsWith("Debris") && type.GetMethodInfo("Init") is {IsStatic: false} method) {
                int index = 1;
                foreach (ParameterInfo parameterInfo in method.GetParameters()) {
                    if (parameterInfo.ParameterType == typeof(Vector2)) {
                        methods[method] = index;
                        break;
                    }

                    index++;
                }
            }
        }

        foreach (KeyValuePair<MethodInfo, int> pair in methods) {
            pair.Key.IlHook(SeededRandom(pair.Value));
        }
    }

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

    private static ILContext.Manipulator SeededRandom(int index) {
        return context => {
            ILCursor cursor = new(context);
            cursor.Emit(OpCodes.Ldarg, index).EmitDelegate(PushRandom);
            while (cursor.TryGotoNext(i => i.OpCode == OpCodes.Ret)) {
                cursor.Emit(OpCodes.Call, typeof(Calc).GetMethod(nameof(Calc.PopRandom)));
                cursor.Index++;
            }
        };
    }

    private static void PushRandom(Vector2 vector2) {
        int seed = vector2.GetHashCode();
        if (Engine.Scene is Level level) {
            seed += level.Session.LevelData.LoadSeed;
        }

        Calc.PushRandom(seed);
    }
}