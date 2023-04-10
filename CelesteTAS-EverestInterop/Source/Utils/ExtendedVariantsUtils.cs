using System;
using ExtendedVariants.Module;

namespace TAS.Utils;

internal static class ExtendedVariantsUtils {
    private static readonly Lazy<bool> installed = new(() => ModUtils.GetModule("ExtendedVariantMode") != null);

    // enum value might be different between different ExtendedVariantMode version
    private static readonly Lazy<object> upsideDownVariant =
        new(() => Enum.Parse(typeof(ExtendedVariantsModule.Variant), "UpsideDown"));

    private static readonly Lazy<object> superDashingVariant =
        new(() => Enum.Parse(typeof(ExtendedVariantsModule.Variant), "SuperDashing"));

    private static bool upsideDown =>
        (bool) ExtendedVariantsModule.Instance.TriggerManager.GetCurrentVariantValue((ExtendedVariantsModule.Variant) upsideDownVariant.Value);

    public static bool UpsideDown => installed.Value && upsideDown;

    private static bool superDashing =>
        (bool) ExtendedVariantsModule.Instance.TriggerManager.GetCurrentVariantValue((ExtendedVariantsModule.Variant) superDashingVariant.Value);

    public static bool SuperDashing => installed.Value && superDashing;
}