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

    // set these fields to manip RNG

    public static int DebrisRandomOffset = 0;

    public static int AscendManagerRandomOffset = 0;

    public static int AuraHelperRandomOffset = 0;

    public static int VortexHelperRandomOffset = 0;

    [Initialize]
    private static void Initialize() {
        #region RNG
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
            pair.Key.IlHook(DebrisRandom.DebrisSeededRandom(pair.Value));
        }
        typeof(Entity).GetMethodInfo("Update")!.HookAfter(DebrisRandom.AfterEntityUpdate);

        typeof(AscendManager).GetMethodInfo("Routine")!.GetStateMachineTarget()!.IlHook(AscendManagerRandom.MakeRngConsistent);

        if (ModUtils.GetType("StrawberryJam2021", "Celeste.Mod.StrawberryJam2021.Entities.CustomAscendManager") is { } ascendManagerType) {
            ascendManagerType.GetMethodInfo("Routine")?.GetStateMachineTarget()!.IlHook(AscendManagerRandom.MakeRngConsistent);
        }

        if (ModUtils.GetType("AuraHelper", "AuraHelper.Lantern") is { } auraLanternType) {
            auraLanternType.GetConstructor([typeof(Vector2), typeof(string), typeof(int)])?.IlHook(AuraHelperRandom.SetupAuraHelperRandom);
            auraLanternType.GetMethodInfo("Update")?.IlHook(AuraHelperRandom.FixAuraEntityDesync);
            ModUtils.GetType("AuraHelper", "AuraHelper.Generator")?.GetMethodInfo("Update")?.IlHook(AuraHelperRandom.FixAuraEntityDesync);
        }

        if (ModUtils.GetType("VortexHelper", "Celeste.Mod.VortexHelper.Entities.ColorSwitch") is { } colorSwitchType) {
            colorSwitchType.GetConstructor([typeof(Vector2), typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)])?.IlHook(VortexHelperRandom.SetupVortexHelperRandom);
            colorSwitchType.GetMethodInfo("Switch")?.IlHook(VortexHelperRandom.FixVortexEntityDesync);
        }
        #endregion

        if (ModUtils.GetModule("DeadzoneConfig")?.GetType() is { } deadzoneConfigModuleType) {
            deadzoneConfigModuleType.GetMethodInfo("OnInputInitialize")!.SkipMethod(SkipDeadzoneConfig);
        }

        // https://discord.com/channels/403698615446536203/519281383164739594/1154486504475869236
        if (ModUtils.GetType("EmoteMod", "Celeste.Mod.EmoteMod.EmoteWheelModule") is { } emoteModuleType) {
            emoteModuleType.GetMethodInfo("Player_Update")?.IlHook(PreventEmoteMod);
        }
    }

    [Load]
    private static void Load() {
        typeof(DreamMirror).GetMethodInfo("Added")!.HookAfter<DreamMirror>(FixDreamMirrorDesync);
        typeof(CS03_Memo.MemoPage).GetConstructors()[0].HookAfter<CS03_Memo.MemoPage>(FixMemoPageCrash);
        typeof(FinalBoss).GetMethodInfo("Added")!.HookAfter<FinalBoss>(FixFinalBossDesync);

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

    private static class DebrisRandom {
        private static int Offset => DebrisRandomOffset;

        private static int debrisAmount;

        internal static ILContext.Manipulator DebrisSeededRandom(int index) {
            return context => {
                ILCursor cursor = new(context);
                cursor.Emit(OpCodes.Ldarg, index).EmitDelegate(DebrisPushRandom);
                while (cursor.TryGotoNext(MoveType.AfterLabel, i => i.OpCode == OpCodes.Ret)) {
                    cursor.EmitDelegate(DebrisPopRandom);
                    cursor.Index++;
                }
            };
        }

        private static void DebrisPushRandom(Vector2 vector2) {
            if (Manager.Running) {
                debrisAmount++;
                int seed = debrisAmount + vector2.GetHashCode() + Offset;
                if (Engine.Scene is Level level) {
                    seed += level.Session.LevelData.LoadSeed;
                }

                Calc.PushRandom(seed);
            }
        }

        private static void DebrisPopRandom() {
            if (Manager.Running) {
                Calc.PopRandom();
            }
        }

        internal static void AfterEntityUpdate() {
            debrisAmount = 0;
        }
    }
    private static class AscendManagerRandom {

        private const string pushedRandomFlag = "CelesteTAS_AscendManagerPushedRandom";

        private static int Offset => AscendManagerRandomOffset;
        internal static void MakeRngConsistent(ILCursor ilCursor, ILContext ilContent) {
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
                Calc.PushRandom(session.LevelData.LoadSeed + Offset);
                session.SetFlag(pushedRandomFlag);
            }
        }

        private static void AscendManagerPopRandom() {
            if (Engine.Scene.GetSession() is { } session && session.GetFlag(pushedRandomFlag)) {
                Calc.PopRandom();
                session.SetFlag(pushedRandomFlag, false);
            }
        }
    }
    internal static class AuraHelperRandom {

        private static int Offset => AuraHelperRandomOffset;

        // this random needs to be used all through aura entity's lifetime
        internal static Random AuraHelperSharedRandom = new Random(1234);
        internal static void SetupAuraHelperRandom(ILContext il) {
            ILCursor cursor = new ILCursor(il);
            cursor.Emit(OpCodes.Ldarg_1);
            cursor.EmitDelegate(CreateAuraHelperRandom);
        }

        private static void CreateAuraHelperRandom(Vector2 vector2) {
            if (Manager.Running) {
                int seed = vector2.GetHashCode() + Offset;
                if (Engine.Scene.GetLevel() is { } level) {
                    seed += level.Session.LevelData.LoadSeed;
                }
                AuraHelperSharedRandom = new Random(seed);
            }
        }

        internal static void FixAuraEntityDesync(ILContext il) {
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

    internal static class VortexHelperRandom {

        private static int Offset => VortexHelperRandomOffset;

        // this random needs to be used all through VortexHelper entity's lifetime
        // TODO: after merging this PR and the multiple save slot PR, need to change how it's saved to speedrun tool
        internal static Random VortexHelperSharedRandom = new Random(2345);
        internal static void SetupVortexHelperRandom(ILContext il) {
            ILCursor cursor = new ILCursor(il);
            cursor.Emit(OpCodes.Ldarg_1);
            cursor.Emit(OpCodes.Ldarg_S, 7);
            cursor.EmitDelegate(CreateVortexHelperRandom);
        }

        private static void CreateVortexHelperRandom(Vector2 vector2, bool random) {
            if (random && Manager.Running) {
                int seed = vector2.GetHashCode() + Offset;
                if (Engine.Scene.GetLevel() is { } level) {
                    seed += level.Session.LevelData.LoadSeed;
                }
                VortexHelperSharedRandom = new Random(seed);
            }
        }

        internal static void FixVortexEntityDesync(ILContext il) {
            ILCursor cursor = new ILCursor(il);
            cursor.EmitDelegate(VortexPushRandom);
            while (cursor.TryGotoNext(MoveType.AfterLabel, i => i.OpCode == OpCodes.Ret)) {
                cursor.EmitDelegate(VortexPopRandom);
                cursor.Index++;
            }
        }

        private static void VortexPushRandom() {
            if (Manager.Running) {
                Calc.PushRandom(VortexHelperSharedRandom);
            }
        }

        private static void VortexPopRandom() {
            if (Manager.Running) {
                Calc.PopRandom();
            }
        }
    }
}
