using Celeste;
using MonoMod.Cil;
using System;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay;

/// Disables / simplified rendering of various objects to reduce visual noise.
/// This helps to identify hitboxes more clearly.
internal static class SimplifiedGraphics {

    private static bool Enabled() => TasSettings.Enabled && TasSettings.SimplifiedGraphics;

    [Initialize]
    private static void Initialize() {
        // Hide Badeline laser since it covers the hitbox
        typeof(FinalBossBeam)
            .GetMethodInfo(nameof(FinalBossBeam.Render))!
            .SkipMethod(() => false);

        #region Extended Variants

        ModUtils.GetMethod("ExtendedVariantMode", "ExtendedVariants.Variants.BlurLevel", "blurLevelBuffer")?.SkipMethod(Enabled);
        ModUtils.GetMethod("ExtendedVariantMode", "ExtendedVariants.Variants.BackgroundBlurLevel", "BackgroundBlurLevelBuffer")?.SkipMethod(Enabled);

        ModUtils.GetMethod("ExtendedVariantMode", "ExtendedVariants.Variants.RoomBloom", "modBloomBase")
            ?.OverrideReturn(() => Enabled() && TasSettings.SimplifiedBloomBase.HasValue, () => TasSettings.SimplifiedBloomBase!.Value / 10.0f);
        ModUtils.GetMethod("ExtendedVariantMode", "ExtendedVariants.Variants.RoomBloom", "modBloomStrength")
            ?.OverrideReturn(() => Enabled() && TasSettings.SimplifiedBloomStrength.HasValue, () => TasSettings.SimplifiedBloomStrength!.Value / 10.0f);

        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Variants.SpinnerColor") is { } t_SpinnerColor) {
            static void PreventColorOverride(ILCursor cursor, ILContext _) {
                // Goto after 'Color spinnerColor = ...'
                cursor.GotoNext(MoveType.After, instr => instr.MatchStloc0());

                var skipOverride = cursor.MarkLabel();
                cursor.MoveBeforeLabels();

                cursor.EmitStaticDelegate("CheckSpinnerColorOverride", bool () => Enabled() && TasSettings.SimplifiedSpinnerColor.Name >= 0);
                cursor.EmitBrfalse(skipOverride);

                // SpinnerColor.Color and CrystalColor map to the same values
                cursor.EmitStaticDelegate("LoadSpinnerColorOverride", CrystalColor () => TasSettings.SimplifiedSpinnerColor.Name);
                cursor.EmitStloc0();
            }

            t_SpinnerColor.GetMethodInfo("onCrystalSpinnerConstructor")?.IlHook(PreventColorOverride);
            t_SpinnerColor.GetMethodInfo("onFrostHelperSpinnerConstructor")?.IlHook(PreventColorOverride);
            t_SpinnerColor.GetMethodInfo("onVivHelperSpinnerConstructor")?.IlHook(PreventColorOverride);
        }

        #endregion
    }
}
