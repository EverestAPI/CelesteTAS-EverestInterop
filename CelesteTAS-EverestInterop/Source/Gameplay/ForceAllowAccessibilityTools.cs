using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using StudioCommunication;
using System;
using System.Reflection;
using TAS.Communication;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay;

/// Some mods intentionally disable accessibility features, to prevent players from ruining their own experience.
/// However, this just causes issues for everyone whose not doing a casual playthrough (TAS routing, Speedrunning, etc.).
/// This feature returns those accessibility tools for those who intentionally want to use them.
internal static class ForceAllowAccessibilityTools {

    private static bool Enabled => TasSettings.Enabled && TasSettings.ForceAllowAccessibilityTools switch {
        StudioEnableCondition.Never => false,
        StudioEnableCondition.Always or StudioEnableCondition.ForCurrentSession => true,
        StudioEnableCondition.WhileStudioConnected => CommunicationWrapper.Connected,
        _ => throw new ArgumentOutOfRangeException()
    };

    /// Only active in RTA gameplay, since it would change the intended gameplay experience inside the TAS
    private static bool EnabledRTA => Enabled && !Manager.Running;

    [Initialize]
    private static void Initialize() {
        // Prevent the "Custom Pause Controller" from disabling pausing / 'Save & Quit'
        if (ModUtils.GetType("KoseiHelper", "Celeste.Mod.KoseiHelper.Entities.CustomPauseController") is { } t_CustomPauseController) {
            t_CustomPauseController
                .GetConstructor([typeof(EntityData), typeof(Vector2)])!
                .HookAfter((Entity controller) => {
                    if (EnabledRTA) {
                        controller.SetFieldValue("canPause", true);
                        controller.SetFieldValue("canSaveAndQuit", true);
                    }
                });
        }
    }

    #region Debug Map

    /// Prevent rooms from being hidden in the debug map by VivHelper
    [ModILHook("VivHelper", "VivHelper.Entities.SpawnPointHooks", "MapEditor_ctor")]
    private static void PreventHideRoomInDebugViv(ILCursor cursor) {
        // Goto 'EntityData entityData = levelData.Entities.FirstOrDefault((e) => e.Name == "VivHelper/HideRoomInMap");'
        var f_FirstOrDefault = ModUtils
            .GetType("VivHelper", "VivHelper.Entities.SpawnPointHooks")!
            .GetNestedType("<>c", BindingFlags.NonPublic)!
            .GetFieldInfo("<>9__12_1")!;
        cursor.GotoNext(instr => instr.MatchStsfld(f_FirstOrDefault));

        // Goto 'if (entityData != default(EntityData))'
        ILLabel? skipRoomHide = null;
        cursor.GotoNext(MoveType.After, instr => instr.MatchBrfalse(out skipRoomHide));

        // Skip room hiding
        cursor.EmitCall(typeof(ForceAllowAccessibilityTools).GetGetMethod(nameof(Enabled))!);
        cursor.EmitBrtrue(skipRoomHide!);
    }

    /// Prevent rooms from being hidden in the debug map by KoseiHelper
    [ModILHook("KoseiHelper", "Celeste.Mod.KoseiHelper.Entities.DebugMapController", "MapEditorCtor")]
    private static void PreventHideRoomInDebugKosei(ILCursor cursor) {
        // Find 'return' statement
        cursor.GotoNext(instr => instr.MatchRet());
        var ret = cursor.Next!;

        // Return after calling 'orig(...)'
        cursor.Index = 0;
        cursor.GotoNext(MoveType.After, instr => instr.MatchCallvirt<On.Celeste.Editor.MapEditor.orig_ctor>(nameof(On.Celeste.Editor.MapEditor.orig_ctor.Invoke)));

        cursor.EmitCall(typeof(ForceAllowAccessibilityTools).GetGetMethod(nameof(Enabled))!);
        cursor.EmitBrtrue(ret);
    }

    /// Prevent keys from being hidden in the debug map by KoseiHelper
    [ModILHook("KoseiHelper", "Celeste.Mod.KoseiHelper.Entities.DebugMapController", "RenderKeys")]
    private static void PreventHideKeysInDebug(ILCursor cursor) {
        var start = cursor.MarkLabel();
        cursor.MoveBeforeLabels();

        // Check if active
        cursor.EmitCall(typeof(ForceAllowAccessibilityTools).GetGetMethod(nameof(Enabled))!);
        cursor.EmitBrfalse(start);

        // Only call 'orig(self)' and skip the rest of the hook
        cursor.EmitLdarg0(); // orig
        cursor.EmitLdarg1(); // self
        cursor.EmitCall(typeof(On.Celeste.Editor.MapEditor.orig_RenderKeys).GetMethodInfo(nameof(On.Celeste.Editor.MapEditor.orig_RenderKeys.Invoke))!);

        cursor.EmitRet();
    }

    /// Prevent rooms in debug map from being altered visually, including hiding certain things
    [ModILHook("KoseiHelper", "Celeste.Mod.KoseiHelper.Entities.DebugMapController", "RenderLevels")]
    private static void PreventRoomAlteredInDebug(ILCursor cursor) {
        ILLabel? jumpToOrig = null;
        cursor.GotoNext(instr => instr.MatchBrfalse(out jumpToOrig));

        cursor.Index = 0;
        cursor.MoveBeforeLabels();

        // Skip directly to the 'orig(...)' call
        cursor.EmitCall(typeof(ForceAllowAccessibilityTools).GetGetMethod(nameof(Enabled))!);
        cursor.EmitBrtrue(jumpToOrig!);
    }

    #endregion
    #region Annoyance

    /// Prevent the "Reset Game Trigger" from actually restarting the game
    [ModILHook("KoseiHelper", "Celeste.Mod.KoseiHelper.Triggers.RestartGameTrigger", nameof(Trigger.OnEnter))]
    private static void PreventGameRestart(ILCursor cursor) {
        cursor.GotoNext(MoveType.After, instr => instr.MatchCall<Trigger>(nameof(Trigger.OnEnter)));

        var skipPrevent = cursor.MarkLabel();
        cursor.MoveBeforeLabels();

        cursor.EmitCall(typeof(ForceAllowAccessibilityTools).GetGetMethod(nameof(Enabled))!);
        cursor.EmitBrfalse(skipPrevent);

        cursor.EmitStaticDelegate("ShowToast", () => {
            // Technically this would cause a game restart, and therefore a TAS should not be allowed to enter it ever.
            AbortTas("Prevented 'Restart Game Trigger'");
        });
        cursor.EmitRet();
    }

    /// Prevent the "Set Window Size Trigger" from actually resizing the game window
    [ModILHook("KoseiHelper", "Celeste.Mod.KoseiHelper.Triggers.SetWindowSizeTrigger", nameof(Trigger.OnStay))]
    private static void PreventWindowResize(ILCursor cursor) {
        cursor.GotoNext(MoveType.After, instr => instr.MatchCall<Trigger>(nameof(Trigger.OnStay)));

        var skipPrevent = cursor.MarkLabel();
        cursor.MoveBeforeLabels();

        cursor.EmitCall(typeof(ForceAllowAccessibilityTools).GetGetMethod(nameof(Enabled))!);
        cursor.EmitBrfalse(skipPrevent);
        cursor.EmitRet();
    }

    /// Prevent the "Crash Trigger" from actually throwing an exception
    [ModILHook("GameHelper", "Celeste.Mod.GameHelper.Triggers.CrashGameTrigger", nameof(Trigger.OnEnter))]
    private static void PreventGameCrash(ILCursor cursor) {
        var skipPrevent = cursor.MarkLabel();
        cursor.MoveBeforeLabels();

        cursor.EmitCall(typeof(ForceAllowAccessibilityTools).GetGetMethod(nameof(Enabled))!);
        cursor.EmitBrfalse(skipPrevent);

        cursor.EmitStaticDelegate("ShowToast", () => {
            // Technically this would cause a crash, and therefore a TAS should not be allowed to enter it ever.
            AbortTas("Prevented 'Crash Trigger'");
        });
        cursor.EmitRet();
    }

    #endregion

}
