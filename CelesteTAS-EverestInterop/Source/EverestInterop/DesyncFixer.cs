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
    private static int debrisAmount;

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
        typeof(DreamMirror).GetMethod("Added").HookAfter<DreamMirror>(FixDreamMirrorDesync);
        typeof(CS03_Memo.MemoPage).GetConstructors()[0].HookAfter<CS03_Memo.MemoPage>(FixMemoPageCrash);
        typeof(FinalBoss).GetMethod("Added").HookAfter<FinalBoss>(FixFinalBossDesync);
        typeof(Entity).GetMethod("Update").HookAfter(AfterEntityUpdate);
    }

    private static void FixDreamMirrorDesync(DreamMirror mirror) {
        mirror.Add(new PostUpdateHook(() => {
            if (Manager.Running) {
                // DreamMirror does some dirty stuff in BeforeRender.
                mirror.BeforeRender();
            }
        }));
    }

    private static void FixFinalBossDesync(FinalBoss finalBoss) {
        finalBoss.Add(new PostUpdateHook(() => {
            if (!Manager.Running) {
                return;
            }

            // FinalBoss does some dirty stuff in Render.
            // finalBoss.ShotOrigin => base.Center + Sprite.Position + new Vector2(6f * Sprite.Scale.X, 2f);
            if (finalBoss.Sprite is { } sprite) {
                sprite.Scale.X = finalBoss.facing;
                sprite.Scale.Y = 1f;
                sprite.Scale *= 1f + finalBoss.scaleWiggler.Value * 0.2f;
            }
        }));
    }

    private static void FixMemoPageCrash(CS03_Memo.MemoPage memoPage) {
        memoPage.Add(new PostUpdateHook(() => {
            if (Manager.Running && memoPage.target == null) {
                // initialize memoPage.target, fix game crash when fast forward
                memoPage.BeforeRender();
            }
        }));
    }

    private static void AfterEntityUpdate() {
        debrisAmount = 0;
    }

    private static ILContext.Manipulator SeededRandom(int index) {
        return context => {
            ILCursor cursor = new(context);
            cursor.Emit(OpCodes.Ldarg, index).EmitDelegate(PushRandom);
            while (cursor.TryGotoNext(MoveType.AfterLabel, i => i.OpCode == OpCodes.Ret)) {
                cursor.EmitDelegate(PopRandom);
                cursor.Index++;
            }
        };
    }

    private static void PushRandom(Vector2 vector2) {
        if (Manager.Running) {
            debrisAmount++;
            int seed = debrisAmount + vector2.GetHashCode();
            if (Engine.Scene is Level level) {
                seed += level.Session.LevelData.LoadSeed;
            }

            Calc.PushRandom(seed);
        }
    }

    private static void PopRandom() {
        if (Manager.Running) {
            Calc.PopRandom();
        }
    }
}