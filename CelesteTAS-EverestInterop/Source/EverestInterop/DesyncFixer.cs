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
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

// ReSharper disable AssignNullToNotNullAttribute
public static class DesyncFixer {
    private const string pushedRandomFlag = "CelesteTAS_PushedRandom";
    private static int debrisAmount;

    // this random needs to be used all through aura entity's lifetime
    internal static Random AuraHelperSharedRandom = new Random(1234);

    [Initialize]
    private static void Initialize() {
        Dictionary<MethodInfo, int> methods = new() {
            {typeof(Debris).GetMethodInfo(nameof(Debris.orig_Init))!, 1},
            {typeof(Debris).GetMethodInfo(nameof(Debris.Init), [typeof(Vector2), typeof(char), typeof(bool)])!, 1},
            {typeof(Debris).GetMethodInfo(nameof(Debris.BlastFrom))!, 1},
        };

        foreach (var type in ModUtils.GetTypes()) {
            if (!type.Name.EndsWith("Debris")) {
                continue;
            }

            foreach (var method in type.GetAllMethodInfos()) {
                if (method.Name != "Init" || method.IsStatic) {
                    continue;
                }

                int index = 1;
                foreach (var param in method.GetParameters()) {
                    if (param.ParameterType == typeof(Vector2)) {
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
            deadzoneConfigModuleType.GetMethodInfo("OnInputInitialize")!.SkipMethod(SkipDeadzoneConfig);
        }

        if (ModUtils.GetType("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Entities.CustomAscendManager") is { } ascendManagerType) {
            ascendManagerType.GetMethodInfo("Routine")?.GetStateMachineTarget()!.IlHook(MakeRngConsistent);
        }

        // https://discord.com/channels/403698615446536203/519281383164739594/1154486504475869236
        if (ModUtils.GetType("EmoteMod", "Celeste.Mod.EmoteMod.EmoteWheelModule") is { } emoteModuleType) {
            emoteModuleType.GetMethodInfo("Player_Update")?.IlHook(PreventEmoteMod);
        }

        if (ModUtils.GetType("AuraHelper", "AuraHelper.Lantern") is { } auraLanternType) {
            auraLanternType.GetConstructor(new Type[] { typeof(Vector2), typeof(string), typeof(int) })?.IlHook(SetupAuraHelperRandom);
            auraLanternType.GetMethodInfo("Update")?.IlHook(FixAuraEntityDesync);
            ModUtils.GetType("AuraHelper", "AuraHelper.Generator")?.GetMethodInfo("Update")?.IlHook(FixAuraEntityDesync);
        }
    }

    [Load]
    private static void Load() {
        typeof(DreamMirror).GetMethodInfo("Added")!.HookAfter<DreamMirror>(FixDreamMirrorDesync);
        typeof(CS03_Memo.MemoPage).GetConstructors()[0].HookAfter<CS03_Memo.MemoPage>(FixMemoPageCrash);
        typeof(FinalBoss).GetMethodInfo("Added")!.HookAfter<FinalBoss>(FixFinalBossDesync);
        typeof(Entity).GetMethodInfo("Update")!.HookAfter(AfterEntityUpdate);
        typeof(AscendManager).GetMethodInfo("Routine")!.GetStateMachineTarget()!.IlHook(MakeRngConsistent);

        // System.IndexOutOfRangeException: Index was outside the bounds of the array.
        // https://discord.com/channels/403698615446536203/1148931167983251466/1148931167983251466
        On.Celeste.LightingRenderer.SetOccluder += IgnoreSetOccluderCrash;
        On.Celeste.LightingRenderer.SetCutout += IgnoreSetCutoutCrash;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.LightingRenderer.SetOccluder -= IgnoreSetOccluderCrash;
        On.Celeste.LightingRenderer.SetCutout -= IgnoreSetCutoutCrash;
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
        if (ilCursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Stfld && ins.Operand.ToString()!.Contains("::<from>"))) {
            ILCursor cursor = ilCursor.Clone();
            if (ilCursor.TryGotoNext(ins => ins.OpCode == OpCodes.Newobj && ins.Operand.ToString()!.Contains("Fader::.ctor"))) {
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

    private static void IgnoreSetCutoutCrash(On.Celeste.LightingRenderer.orig_SetCutout orig, LightingRenderer self, Vector3 center, Color mask, Vector2 light, float x, float y, float width, float height) {
        try {
            orig(self, center, mask, light, x, y, width, height);
        } catch (IndexOutOfRangeException e) {
            if (Manager.Running) {
                e.Log(LogLevel.Debug);
            } else {
                throw;
            }
        }
    }

    private static void PreventEmoteMod(ILCursor ilCursor, ILContext ilContext) {
        if (ilCursor.TryGotoNext(
                ins => ins.OpCode == OpCodes.Call,
                ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString()!.Contains("::get_EmoteWheelBinding()"),
                ins => ins.OpCode == OpCodes.Callvirt,
                ins => ins.OpCode == OpCodes.Callvirt && ins.Operand.ToString()!.Contains("::get_Count()")
            )) {
            ilCursor.Index += 2;
            ilCursor.Emit(OpCodes.Dup);
            ilCursor.Index += 2;
            ilCursor.EmitDelegate(IsEmoteWheelBindingPressed);
        }
    }

    private static int IsEmoteWheelBindingPressed(ButtonBinding binding, int count) {
        return binding.Pressed ? count : 0;
    }

    private static void SetupAuraHelperRandom(ILContext il) {
        ILCursor cursor = new ILCursor(il);
        cursor.Emit(OpCodes.Ldarg_1);
        cursor.EmitDelegate(CreateAuraHelperRandom);
    }

    private static void CreateAuraHelperRandom(Vector2 vector2) {
        if (Manager.Running) {
            int seed = vector2.GetHashCode();
            if (Engine.Scene.GetLevel() is { } level) {
                seed += level.Session.LevelData.LoadSeed;
            }
            AuraHelperSharedRandom = new Random(seed);
        }
    }

    private static void FixAuraEntityDesync(ILContext il) {
        ILCursor cursor = new ILCursor(il);
        cursor.EmitDelegate(AuraPushRandom);
        while (cursor.TryGotoNext(MoveType.AfterLabel, i => i.OpCode == OpCodes.Ret)) {
            cursor.EmitDelegate(AuraPopRandom);
            cursor.Index++;
        }
    }

    private static void AuraPushRandom() {
        if (Manager.Running) {
            Calc.PushRandom(AuraHelperSharedRandom);
        }
    }

    private static void AuraPopRandom() {
        if (Manager.Running) {
            Calc.PopRandom();
        }
    }
}
