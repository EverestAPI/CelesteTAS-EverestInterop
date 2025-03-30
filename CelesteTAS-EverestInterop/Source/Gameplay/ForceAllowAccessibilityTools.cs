using MonoMod.Cil;
using StudioCommunication;
using System;
using System.Reflection;
using TAS.Communication;
using TAS.ModInterop;
using TAS.Utils;

namespace TAS.Gameplay;

/// Some mods intentionally disable accessibility features, to prevent players from ruining their own experience.
/// However, this just causes issues for everyone whose not doing a casual playthrough (TAS routing, Speedrunning, etc.).
/// This feature returns those accessibility tools for those who intentionally want to use them.
internal static class ForceAllowAccessibilityTools {

    private static bool Enabled => TasSettings.Enabled && TasSettings.ForceAllowAccessibilityTools switch {
        EnableCondition.Never => false,
        EnableCondition.Always => true,
        EnableCondition.WhileStudioConnected => CommunicationWrapper.Connected,
        _ => throw new ArgumentOutOfRangeException()
    };

    /// Prevent rooms from being hidden in the debug map
    [ModILHook("VivHelper", "VivHelper.Entities.SpawnPointHooks", "MapEditor_ctor")]
    private static void PreventHideRoomInDebug(ILCursor cursor) {
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
}
