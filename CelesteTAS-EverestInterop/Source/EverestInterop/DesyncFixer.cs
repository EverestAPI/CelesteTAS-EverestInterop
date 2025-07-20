using System;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop;

// ReSharper disable AssignNullToNotNullAttribute
public static class DesyncFixer {
    [Initialize]
    private static void Initialize() {
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
}
