using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

// ReSharper disable AssignNullToNotNullAttribute
public static class DesyncFixer {
    private const string pushedRandomFlag = "CelesteTAS_PushedRandom";
    private static int debrisAmount;

    [Initialize]
    private static void Initialize() {
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

        if (ModUtils.GetModule("DeadzoneConfig")?.GetType() is { } deadzoneConfigModuleType) {
            HookHelper.SkipMethod(typeof(DesyncFixer), nameof(SkipDeadzoneConfig), deadzoneConfigModuleType.GetMethod("OnInputInitialize"));
        }

        if (ModUtils.GetType("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Entities.CustomAscendManager") is { } ascendManagerType) {
            ascendManagerType.GetMethodInfo("Routine")?.GetStateMachineTarget().IlHook(MakeRngConsistent);
        }
    }

    [Load]
    private static void Load() {
        typeof(DreamMirror).GetMethod("Added").HookAfter<DreamMirror>(FixDreamMirrorDesync);
        typeof(CS03_Memo.MemoPage).GetConstructors()[0].HookAfter<CS03_Memo.MemoPage>(FixMemoPageCrash);
        typeof(FinalBoss).GetMethod("Added").HookAfter<FinalBoss>(FixFinalBossDesync);
        typeof(Entity).GetMethod("Update").HookAfter(AfterEntityUpdate);
        typeof(AscendManager).GetMethodInfo("Routine").GetStateMachineTarget().IlHook(MakeRngConsistent);

        // https://github.com/EverestAPI/Everest/commit/b2a6f8e7c41ddafac4e6fde0e43a09ce1ac4f17e
        // Autosaving prevents opening the menu to skip cutscenes during fast forward before Everest v2865.
        if (Everest.Version < new Version(1, 2865)) {
            typeof(Level).GetProperty("CanPause").GetGetMethod().IlHook(AllowPauseDuringSaving);
        }
       
        // System.IndexOutOfRangeException: Index was outside the bounds of the array.
        // https://discord.com/channels/403698615446536203/1148931167983251466/1148931167983251466
        On.Celeste.LightingRenderer.SetOccluder += IgnoreSetOccluderCrash;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.LightingRenderer.SetOccluder -= IgnoreSetOccluderCrash;
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

    private static void MakeRngConsistent(ILCursor ilCursor, ILContext ilContent) {
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Stfld && ins.Operand.ToString().Contains("::<from>"))) {
            ILCursor cursor = ilCursor.Clone();
            if (ilCursor.TryGotoNext(ins => ins.OpCode == OpCodes.Newobj && ins.Operand.ToString().Contains("Fader::.ctor"))) {
                cursor.EmitDelegate(AscendManagerPushRandom);
                ilCursor.EmitDelegate(AscendManagerPopRandom);
            }
        }
    }

    private static void AscendManagerPushRandom() {
        if (Manager.Running && Engine.Scene.GetSession() is { } session && session.Area.GetLevelSet() != "Celeste") {
            Calc.PushRandom(session.LevelData.LoadSeed);
            session.SetFlag(pushedRandomFlag);
        }
    }

    private static void AscendManagerPopRandom() {
        if (Engine.Scene.GetSession() is { } session && session.GetFlag(pushedRandomFlag)) {
            Calc.PopRandom();
            session.SetFlag(pushedRandomFlag, false);
        }
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

    private static bool SkipDeadzoneConfig() {
        return Manager.Running;
    }

    private static void AllowPauseDuringSaving(ILCursor ilCursor, ILContext ilContext) {
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.MatchCall(typeof(UserIO), "get_Saving"))) {
            ilCursor.EmitDelegate(IsSaving);
        }
    }

    private static bool IsSaving(bool saving) {
        return !Manager.Running && saving;
    }

    private static void IgnoreSetOccluderCrash(On.Celeste.LightingRenderer.orig_SetOccluder orig, LightingRenderer self, Vector3 center, Color mask, Vector2 light, Vector2 edgeA, Vector2 edgeB) {
        try {
            orig(self, center, mask, light, edgeA, edgeB);
        } catch (IndexOutOfRangeException e) {
            if (Manager.Running) {
                e.Log(LogLevel.Debug);
            } else {
                throw;
            }
        }
    }
}