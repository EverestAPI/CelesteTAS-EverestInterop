using System;
using ExtendedVariants.Module;

namespace TAS.Utils;

internal static class ExtendedVariantsUtils {
    private static readonly Lazy<bool> installed =
        new(() => ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsModule") != null);

    private static bool upsideDown =>
        (bool) ExtendedVariantsModule.Instance.TriggerManager.GetCurrentVariantValue(ExtendedVariantsModule.Variant.UpsideDown);

    public static bool UpsideDown => installed.Value && upsideDown;
}